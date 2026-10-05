using BatteryCharge.Core;
using BatteryCharge.App;
using System.Xml.Linq;
using System.Globalization;

// Dependency-free behavioral checks: no device access, test SDK, or test packages.
var checks = new (string Name, Func<Task> Run)[]
{
    ("Read-only probe never writes and decodes flags", ProbeIsReadOnly),
    ("Unsupported night charge does not disable mode", IndependentSupport),
    ("Unavailable mode does not disable night charge", IndependentNightSupport),
    ("Unavailable driver reports both features", UnavailableDriver),
    ("All charge mode transitions preserve exact command order", ModeTransitions),
    ("Night charge transitions are verified", NightTransitions),
    ("Unsupported night charge cannot be written", UnsupportedNightCannotWrite),
    ("Unavailable charge mode cannot be written", UnavailableModeCannotWrite),
    ("Ignored write fails rather than reporting success", IgnoredWriteFails),
    ("Delayed state change is retried", DelayedStateChange),
    ("Driver write failure stops the sequence", FailedWriteStopsSequence),
    ("Invalid enum never accesses the device", InvalidModeDoesNotTouchDevice),
    ("Concurrent requests cannot split a mode sequence", ConcurrentRequestsAreSerialized),
    ("Startup task preserves executable paths and interactive battery operation", StartupTaskConfiguration),
    ("Disabled tasks and logon triggers are reported as disabled", DisabledStartupTask),
    ("Moved executable retains enabled state and reports stale path", MovedStartupExecutable),
    ("Foreign tasks and other users cannot be modified", ForeignStartupTask),
    ("Language choice is saved and survives restart", LanguagePreferenceRoundTrip),
    ("Missing, damaged and unsupported language settings fall back safely", LanguagePreferenceFallback),
    ("UI and driver errors switch languages independently of thread culture", LanguageResources),
    ("Cleanup removes only app settings and recognized temporary files", CleanupPreservesOtherFiles),
    ("Startup removal failure preserves configuration for retry", CleanupTaskFailure),
    ("Configuration deletion failure is reported and preserves remaining files", CleanupFileFailure)
};

var failures = 0;
foreach (var check in checks)
{
    try
    {
        await check.Run();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {check.Name}: {error}");
    }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static ChargeController Controller(FakeTransport transport, int attempts = 3) =>
    new(transport, attempts, TimeSpan.Zero);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task ProbeIsReadOnly()
{
    // Both flags set: conservation must win; unrelated high bits must be ignored.
    var transport = new FakeTransport { ModeFlags = 0x80000024, NightFlags = 0x80000011 };
    var snapshot = await Controller(transport).ReadAsync();
    Assert(snapshot.Mode.Value == ChargeMode.Conservation, "Wrong mode flag precedence.");
    Assert(snapshot.NightCharge.Value == true, "Wrong night charge bit.");
    Assert(transport.Writes.Count == 0, "Reading must not write firmware.");
    Assert(transport.Queries.SequenceEqual(new[] { (0x831020F8u, 0xFFu), (0x83102150u, 0x11u) }),
        "Incorrect query protocol.");
}

static async Task IndependentSupport()
{
    var transport = new FakeTransport { NightError = new IOException("Night unavailable") };
    var snapshot = await Controller(transport).ReadAsync();
    Assert(snapshot.Mode.IsAvailable, "Night failure must not disable charge mode.");
    Assert(!snapshot.NightCharge.IsAvailable && snapshot.NightCharge.Error is not null, "Missing failure reason.");
}

static async Task UnavailableDriver()
{
    var transport = new FakeTransport
    {
        ModeError = new IOException("Device missing"),
        NightError = new IOException("Device missing")
    };
    var snapshot = await Controller(transport).ReadAsync();
    Assert(!snapshot.Mode.IsAvailable && !snapshot.NightCharge.IsAvailable, "Missing device must disable both features.");
    Assert(transport.Writes.Count == 0, "Failed probes must not write.");
}

static async Task IndependentNightSupport()
{
    var transport = new FakeTransport { ModeError = new IOException("Mode unavailable") };
    var snapshot = await Controller(transport).ReadAsync();
    Assert(!snapshot.Mode.IsAvailable && snapshot.NightCharge.IsAvailable,
        "Mode failure must not disable night charge.");
}

static async Task ModeTransitions()
{
    var cases = new[]
    {
        (ChargeMode.Normal, new uint[] { 0x05, 0x08 }),
        (ChargeMode.Conservation, new uint[] { 0x08, 0x03 }),
        (ChargeMode.RapidCharge, new uint[] { 0x05, 0x07 })
    };
    foreach (var (mode, commands) in cases)
    {
        var transport = new FakeTransport { ModeFlags = 0x20 };
        var actual = await Controller(transport).SetModeAsync(mode);
        Assert(actual == mode, "Controller returned wrong state.");
        Assert(transport.Writes.SequenceEqual(commands.Select(value => (0x831020F8u, value))),
            $"Incorrect write sequence for {mode}.");
        Assert(transport.Queries.Count >= 2, "Must probe and verify.");
    }
}

static async Task NightTransitions()
{
    var transport = new FakeTransport();
    var controller = Controller(transport);
    Assert(await controller.SetNightChargeAsync(true), "Night charge enable failed.");
    Assert(!await controller.SetNightChargeAsync(false), "Night charge disable failed.");
    Assert(transport.Writes.SequenceEqual(new[] { (0x83102150u, 0x80000012u), (0x83102150u, 0x12u) }),
        "Incorrect night charge commands.");
}

static async Task UnsupportedNightCannotWrite()
{
    var transport = new FakeTransport { NightFlags = 0x10 }; // Bit 4 without validity bit.
    await Throws<NotSupportedException>(() => Controller(transport).SetNightChargeAsync(true));
    Assert(transport.Writes.Count == 0, "Unsupported night charge must never be written.");
}

static async Task IgnoredWriteFails()
{
    var transport = new FakeTransport { IgnoreWrites = true };
    await Throws<TimeoutException>(() => Controller(transport).SetModeAsync(ChargeMode.Conservation));
    Assert(transport.Queries.Count == 4, "Expected one preflight query and three verification attempts.");
    await Throws<TimeoutException>(() => Controller(transport).SetNightChargeAsync(true));
}

static async Task UnavailableModeCannotWrite()
{
    var transport = new FakeTransport { ModeError = new IOException("Mode unavailable") };
    await Throws<IOException>(() => Controller(transport).SetModeAsync(ChargeMode.RapidCharge));
    Assert(transport.Writes.Count == 0, "Unavailable mode must never be written.");
}

static async Task DelayedStateChange()
{
    var transport = new FakeTransport { DelayedModeQueries = 2 };
    Assert(await Controller(transport).SetModeAsync(ChargeMode.Conservation) == ChargeMode.Conservation,
        "Delayed firmware response should succeed within retry limit.");
    Assert(transport.Queries.Count == 4, "Expected retry reads for delayed firmware.");
}

static async Task FailedWriteStopsSequence()
{
    var transport = new FakeTransport { FailWriteNumber = 1 };
    await Throws<IOException>(() => Controller(transport).SetModeAsync(ChargeMode.Conservation));
    Assert(transport.Writes.Count == 1, "Second command must not run after the first fails.");
}

static async Task InvalidModeDoesNotTouchDevice()
{
    var transport = new FakeTransport();
    await Throws<ArgumentOutOfRangeException>(() => Controller(transport).SetModeAsync((ChargeMode)123));
    Assert(transport.Queries.Count == 0 && transport.Writes.Count == 0, "Invalid mode touched hardware.");
}

static async Task ConcurrentRequestsAreSerialized()
{
    var transport = new FakeTransport { SlowWrites = true };
    var controller = Controller(transport);
    var changes = Enumerable.Range(0, 18).Select(index => controller.SetModeAsync((ChargeMode)(index % 3))).ToArray();
    await Task.WhenAll(changes);
    Assert(transport.Writes.Count == 36, "Lost a write.");
    // The gate guarantees atomic operations, not caller arrival order.
    var completedModes = new List<ChargeMode>();
    for (var index = 0; index < changes.Length; index++)
    {
        var pair = transport.Writes.Skip(index * 2).Take(2).Select(write => write.Input).ToArray();
        var matchingModes = Enum.GetValues<ChargeMode>()
            .Where(mode => pair.SequenceEqual(ChargeProtocol.CommandsFor(mode))).ToArray();
        Assert(matchingModes.Length == 1, "A command pair was interleaved.");
        completedModes.Add(matchingModes[0]);
    }
    foreach (var mode in Enum.GetValues<ChargeMode>())
        Assert(completedModes.Count(item => item == mode) == 6, "A mode request was lost.");
}

static Task StartupTaskConfiguration()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    const string path = @"D:\电池 & tools\BatteryCharge.exe";
    var xml = StartupTaskDefinition.Create(sid, path);
    var task = XElement.Parse(xml);
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    Assert((string?)task.Element(ns + "Actions")?.Element(ns + "Exec")?.Element(ns + "Command") == path,
        "Paths containing spaces, Unicode and XML metacharacters must round-trip.");
    Assert((string?)task.Element(ns + "Actions")?.Element(ns + "Exec")?.Element(ns + "Arguments") == "--startup",
        "Autostart must use tray-only launch.");
    var status = StartupTaskDefinition.Read(xml, sid, path);
    Assert(status.Enabled && status.UsesCurrentPath, "New task should be enabled for this path.");
    var settings = task.Element(ns + "Settings")!;
    Assert((bool?)settings.Element(ns + "DisallowStartIfOnBatteries") == false,
        "Startup must work when running on battery.");
    Assert((bool?)settings.Element(ns + "StopIfGoingOnBatteries") == false,
        "Unplugging AC must not stop the application.");
    Assert((string?)settings.Element(ns + "ExecutionTimeLimit") == "PT0S",
        "A tray app must not be stopped after the scheduler's default time limit.");
    Assert((string?)task.Element(ns + "Triggers")?.Element(ns + "LogonTrigger")?.Element(ns + "UserId") == sid,
        "Startup must apply only to the selected user.");
    return Task.CompletedTask;
}

static Task DisabledStartupTask()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    const string path = @"D:\BatteryCharge\BatteryCharge.exe";
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    var task = XElement.Parse(StartupTaskDefinition.Create(sid, path));
    task.Element(ns + "Settings")!.Element(ns + "Enabled")!.Value = "false";
    Assert(!StartupTaskDefinition.Read(task.ToString(), sid, path).Enabled, "A disabled task was shown as enabled.");
    task.Element(ns + "Settings")!.Element(ns + "Enabled")!.Value = "true";
    task.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "Enabled")!.Value = "false";
    Assert(!StartupTaskDefinition.Read(task.ToString(), sid, path).Enabled, "A disabled trigger was shown as enabled.");
    return Task.CompletedTask;
}

static Task MovedStartupExecutable()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    var xml = StartupTaskDefinition.Create(sid, @"D:\Old\BatteryCharge.exe");
    var status = StartupTaskDefinition.Read(xml, sid, @"D:\New\BatteryCharge.exe");
    Assert(status.Enabled && !status.UsesCurrentPath, "An active task at an old path must not look disabled.");
    return Task.CompletedTask;
}

static async Task ForeignStartupTask()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    var xml = StartupTaskDefinition.Create(sid, @"D:\BatteryCharge\BatteryCharge.exe");
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    var foreign = XElement.Parse(xml);
    foreign.Element(ns + "RegistrationInfo")!.Element(ns + "Source")!.Value = "AnotherApp";
    await Throws<InvalidOperationException>(() =>
    {
        StartupTaskDefinition.ParseOwned(foreign.ToString(), sid);
        return Task.CompletedTask;
    });
    await Throws<InvalidOperationException>(() =>
    {
        StartupTaskDefinition.ParseOwned(xml, "S-1-5-21-123-456-789-1002");
        return Task.CompletedTask;
    });
}

static Task LanguagePreferenceRoundTrip()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-language-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    try
    {
        LanguagePreferences.Save(path, "en-US");
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-CN")) == "en-US",
            "Saved English choice must override the system language.");
        LanguagePreferences.Save(path, "zh-CN");
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("en-US")) == "zh-CN",
            "Changing to Chinese must replace the previous setting.");
        Assert(Directory.GetFiles(directory).Length == 1, "Temporary preference files were left behind.");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
    return Task.CompletedTask;
}

static Task LanguagePreferenceFallback()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-language-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    try
    {
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-TW")) == "zh-CN",
            "A Chinese system locale must select Chinese without a settings file.");
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("fr-FR")) == "en-US",
            "Other system locales must fall back to English.");
        Directory.CreateDirectory(directory);
        foreach (var invalid in new[] { "broken json", "null", "[]", "{}", "{\"language\":42}", "{\"language\":\"de-DE\"}" })
        {
            File.WriteAllText(path, invalid);
            Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("en-US")) == "en-US",
                "Invalid preferences must not prevent startup or select an unsupported language.");
        }
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
    return Task.CompletedTask;
}

static Task LanguageResources()
{
    var original = UiText.Language;
    try
    {
        foreach (var (language, modeName, errorText) in new[]
        {
            ("en-US", "Normal charging", "The device did not report"),
            ("zh-CN", "普通充电", "设备未报告")
        })
        {
            UiText.SetLanguage(language);
            Assert(UiText.ModeName(ChargeMode.Normal) == modeName, "Mode text did not change language.");
            Assert(UiText.Get("CurrentMode", modeName).Contains(modeName), "Formatted translation lost its argument.");
            try
            {
                ChargeProtocol.DecodeNightCharge(0x10);
                throw new InvalidOperationException("Expected an unsupported night state.");
            }
            catch (NotSupportedException error)
            {
                Assert(error.Message.Contains(errorText) && error.Message.Contains("0x00000010"),
                    "Driver error translation must preserve the diagnostic value.");
            }
        }
    }
    finally
    {
        UiText.SetLanguage(original);
    }
    return Task.CompletedTask;
}

static Task CleanupPreservesOtherFiles()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-cleanup-{Guid.NewGuid():N}");
    var current = Path.Combine(directory, "portable", "settings.json");
    var legacy = Path.Combine(directory, "legacy", "settings.json");
    try
    {
        LanguagePreferences.Save(current, "en-US");
        LanguagePreferences.Save(legacy, "zh-CN");
        var executable = Path.Combine(Path.GetDirectoryName(current)!, "BatteryCharge.exe");
        var unrelated = Path.Combine(Path.GetDirectoryName(current)!, ".settings-not-a-guid.tmp");
        var temporary = Path.Combine(Path.GetDirectoryName(current)!, $".settings-{Guid.NewGuid():N}.tmp");
        var nested = Path.Combine(Path.GetDirectoryName(current)!, "other-data", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        foreach (var path in new[] { executable, unrelated, temporary, nested })
            File.WriteAllText(path, "Keep unless owned by the app.");
        var removals = 0;
        CleanupService.Run(() =>
        {
            removals++;
            Assert(File.Exists(current), "Startup task must be removed before preferences.");
        }, current, legacy);
        Assert(removals == 1, "Startup removal must run once.");
        Assert(!File.Exists(current) && !File.Exists(legacy) && !File.Exists(temporary), "Owned settings were left behind.");
        Assert(File.Exists(executable) && File.Exists(unrelated) && File.Exists(nested), "Unrelated files were deleted.");
        Assert(!Directory.Exists(Path.GetDirectoryName(legacy)), "An empty legacy settings folder was left behind.");
        CleanupService.Run(() => { }, current, legacy); // Repeating a completed cleanup must be harmless.
        LanguagePreferences.Save(legacy, "zh-CN");
        var legacyOther = Path.Combine(Path.GetDirectoryName(legacy)!, "other.txt");
        File.WriteAllText(legacyOther, "Keep");
        CleanupService.Run(() => { }, current, legacy);
        Assert(File.Exists(legacyOther), "A nonempty legacy directory must not be removed recursively.");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
    return Task.CompletedTask;
}

static async Task CleanupTaskFailure()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-cleanup-{Guid.NewGuid():N}");
    var current = Path.Combine(directory, "portable", "settings.json");
    var legacy = Path.Combine(directory, "legacy", "settings.json");
    try
    {
        LanguagePreferences.Save(current, "en-US");
        LanguagePreferences.Save(legacy, "zh-CN");
        await Throws<IOException>(() =>
        {
            CleanupService.Run(() => throw new InvalidOperationException("Scheduler unavailable"), current, legacy);
            return Task.CompletedTask;
        });
        Assert(File.Exists(current) && File.Exists(legacy), "Task removal failure must preserve preferences for retry.");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

static async Task CleanupFileFailure()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-cleanup-{Guid.NewGuid():N}");
    var current = Path.Combine(directory, "portable", "settings.json");
    var legacy = Path.Combine(directory, "legacy", "settings.json");
    try
    {
        Directory.CreateDirectory(current); // A directory cannot be deleted using File.Delete.
        LanguagePreferences.Save(legacy, "zh-CN");
        var taskRemoved = false;
        await Throws<IOException>(() =>
        {
            CleanupService.Run(() => taskRemoved = true, current, legacy);
            return Task.CompletedTask;
        });
        Assert(taskRemoved && File.Exists(legacy) && Directory.Exists(current),
            "Partial cleanup must report failure and preserve unprocessed files.");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

sealed class FakeTransport : IEnergyTransport
{
    public uint ModeFlags { get; set; }
    public uint NightFlags { get; set; } = 1;
    public Exception? ModeError { get; init; }
    public Exception? NightError { get; init; }
    public bool IgnoreWrites { get; init; }
    public bool SlowWrites { get; init; }
    public int FailWriteNumber { get; init; }
    public int DelayedModeQueries { get; set; }
    public List<(uint Control, uint Input)> Writes { get; } = [];
    public List<(uint Control, uint Input)> Queries { get; } = [];
    private uint? _pendingMode;

    public uint Query(uint controlCode, uint input)
    {
        Queries.Add((controlCode, input));
        if (controlCode == ChargeProtocol.ModeControlCode && input == ChargeProtocol.ModeQuery)
        {
            if (ModeError is not null)
                throw ModeError;
            if (_pendingMode.HasValue)
            {
                if (DelayedModeQueries > 0)
                    DelayedModeQueries--;
                else
                {
                    ModeFlags = _pendingMode.Value;
                    _pendingMode = null;
                }
            }
            return ModeFlags;
        }
        if (controlCode == ChargeProtocol.NightControlCode && input == ChargeProtocol.NightQuery)
        {
            if (NightError is not null)
                throw NightError;
            return NightFlags;
        }
        throw new InvalidOperationException("Unexpected query.");
    }

    public void Send(uint controlCode, uint input)
    {
        Writes.Add((controlCode, input));
        if (Writes.Count == FailWriteNumber)
            throw new IOException("Injected driver failure");
        if (SlowWrites)
            Thread.Sleep(2);
        if (IgnoreWrites)
            return;

        if (controlCode == ChargeProtocol.ModeControlCode)
        {
            var next = input switch
            {
                0x05 => ModeFlags & ~0x20u,
                0x08 => ModeFlags & ~0x04u,
                0x03 => ModeFlags | 0x20u,
                0x07 => ModeFlags | 0x04u,
                _ => throw new InvalidOperationException("Unexpected mode command.")
            };
            if (DelayedModeQueries > 0)
                _pendingMode = next;
            else
                ModeFlags = next;
        }
        else if (controlCode == ChargeProtocol.NightControlCode)
        {
            NightFlags = input switch
            {
                0x80000012 => 0x11,
                0x12 => 1,
                _ => throw new InvalidOperationException("Unexpected night command.")
            };
        }
        else
        {
            throw new InvalidOperationException("Unexpected control code.");
        }
    }
}
