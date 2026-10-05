using BatteryCharge.Core;
using BatteryCharge.App;
using System.Xml.Linq;
using System.Globalization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Collections;
using System.Resources;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

if (OperatingSystem.IsWindows() && Environment.GetCommandLineArgs().Contains("--instance-probe"))
{
    var arguments = Environment.GetCommandLineArgs();
    var namespaceName = arguments[Array.IndexOf(arguments, "--instance-probe") + 1];
    using var instance = new SingleInstance(namespaceName);
    Console.WriteLine($"INSTANCE {instance.IsFirst}|{instance.CanActivate}");
    if (instance.CanActivate) instance.ShowWindow.Set();
    return 0;
}

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
    ("Timed-out native work keeps its gate and rejects subsequent writes", DriverTimeoutIsolation),
    ("A late driver completion cannot continue a timed-out write sequence", DriverTimeoutStopsLateSequence),
    ("Startup task preserves executable paths and interactive battery operation", StartupTaskConfiguration),
    ("Startup accepts account names that resolve to the current user's SID", StartupAccountNames),
    ("Startup rejects foreign, unresolved and missing account identities", StartupAccountProtection),
    ("Disabled tasks and logon triggers are reported as disabled", DisabledStartupTask),
    ("Moved executable retains enabled state and reports stale path", MovedStartupExecutable),
    ("Foreign tasks and other users cannot be modified", ForeignStartupTask),
    ("Missing startup tasks handle COM and mapped file-not-found exceptions", MissingStartupTask),
    ("Startup lookup preserves existing tasks and propagates scheduler failures", StartupLookupFailures),
    ("Unsafe startup targets are disabled without losing paths or hiding failures", StartupSafetyMigration),
    ("Language choice is saved and survives restart", LanguagePreferenceRoundTrip),
    ("Missing, damaged and unsupported language settings fall back safely", LanguagePreferenceFallback),
    ("Oversized preferences fall back without reading the whole file", OversizedLanguagePreferences),
    ("UI and driver errors switch languages independently of thread culture", LanguageResources),
    ("Cleanup removes only app settings and recognized temporary files", CleanupPreservesOtherFiles),
    ("Startup removal failure preserves configuration for retry", CleanupTaskFailure),
    ("Configuration deletion failure is reported and preserves remaining files", CleanupFileFailure),
    ("Directory links cannot redirect settings writes or cleanup", DirectoryLinkProtection),
    ("Window bounds fit small screens, scaled displays and disconnected monitors", AdaptiveWindowBounds),
    ("Fluent UI resources have matching keys and format arguments in both languages", FluentResourceCoverage),
    ("Light and dark palettes keep body text readable on their surfaces", FluentPaletteContrast)
};

if (OperatingSystem.IsWindows())
    checks = [.. checks,
        ("Windows directory leases prevent parent replacement and release cleanly", WindowsDirectoryLease),
        ("Startup ACL policy rejects writable targets and untrusted owners", WindowsStartupAcl),
        ("Private instance objects ignore public mutex precreation and retain activation", WindowsPrivateInstance)];

var failures = 0;
foreach (var check in checks)
{
    try
    {
        await check.Run();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (FileLoadException error) when (error.HResult == unchecked((int)0x800711C7))
    {
        Console.Error.WriteLine($"BLOCKED Windows Application Control prevented loading: {error.FileName}");
        Console.Error.WriteLine("Behavior checks could not complete; publishing must stop. This is an execution-policy block, not a failed behavior assertion.");
        Console.Error.WriteLine("Inspect Event ID 3077 in Event Viewer > Applications and Services Logs > Microsoft > Windows > CodeIntegrity > Operational.");
        Console.Error.WriteLine("Have the applicable policy trust this build/signature, or run checks in an approved development environment. PowerShell -ExecutionPolicy Bypass does not override Application Control.");
        return 1;
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

static async Task StartupSafetyMigration()
{
    var registration = new StartupRegistration(true, false, @"C:\Protected\BatteryCharge.exe", @"C:\Writable\BatteryCharge.exe");
    var disables = 0;
    var result = StartupRegistrationPolicy.Enforce(registration, path =>
    {
        Assert(path == registration.RegisteredExecutablePath, "The registered target must be validated, not just the running executable.");
        throw new IOException("Untrusted path");
    }, () => disables++);
    Assert(disables == 1 && !result.Enabled && !result.UsesCurrentPath && result.SecurityError is not null
        && result.RegisteredExecutablePath == registration.RegisteredExecutablePath, "Unsafe startup migration lost its warning or path.");
    Assert(StartupRegistrationPolicy.Enforce(registration, _ => { }, () => disables++) == registration && disables == 1,
        "A protected target must not be disabled.");
    var disabled = registration with { Enabled = false };
    Assert(StartupRegistrationPolicy.Enforce(disabled, _ => throw new Exception("Must not validate"),
        () => throw new Exception("Must not disable")) == disabled, "An already disabled task was modified.");
    await Throws<UnauthorizedAccessException>(() =>
    {
        StartupRegistrationPolicy.Enforce(registration, _ => throw new IOException("Unsafe"),
            () => throw new UnauthorizedAccessException("Scheduler denied disabling"));
        return Task.CompletedTask;
    });
}

static async Task DriverTimeoutIsolation()
{
    using var transport = new BlockingTransport();
    var controller = new ChargeController(transport, operationTimeout: TimeSpan.FromMilliseconds(200));
    var read = controller.ReadAsync();
    try
    {
        Assert(await Task.Run(() => transport.Entered.Wait(TimeSpan.FromSeconds(3))), "The native worker did not enter.");
        var queuedWrite = controller.SetModeAsync(ChargeMode.RapidCharge);
        await Throws<IOException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
        await Throws<IOException>(() => queuedWrite.WaitAsync(TimeSpan.FromSeconds(3)));
        await Throws<IOException>(() => controller.SetNightChargeAsync(true));
        Assert(transport.Writes == 0, "A timed-out driver allowed another write while its call was pending.");
    }
    finally
    {
        transport.Release.Set();
        Assert(await Task.Run(() => transport.Completed.Wait(TimeSpan.FromSeconds(3))), "The test worker failed to finish.");
    }
    await Throws<IOException>(() => controller.SetModeAsync(ChargeMode.Normal));
    Assert(transport.Writes == 0, "A late driver completion must not silently re-enable writes.");
}

static async Task DriverTimeoutStopsLateSequence()
{
    using var transport = new BlockingTransport { BlockWrites = true };
    var controller = new ChargeController(transport, operationTimeout: TimeSpan.FromMilliseconds(200));
    var write = controller.SetModeAsync(ChargeMode.RapidCharge);
    try
    {
        Assert(await Task.Run(() => transport.Entered.Wait(TimeSpan.FromSeconds(3))), "The first command did not enter the driver.");
        await Throws<IOException>(() => write.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert(transport.Writes == 1, "Only the already pending first command may have been sent.");
    }
    finally { transport.Release.Set(); }
    Assert(await Task.Run(() => transport.Completed.Wait(TimeSpan.FromSeconds(3))), "The late native call did not finish.");
    await Task.Delay(100); // Allow the native worker to unwind after returning from the first command.
    Assert(transport.Writes == 1, "The worker sent the rest of the sequence after the UI reported a timeout.");
    await Throws<IOException>(() => controller.SetModeAsync(ChargeMode.Normal));
}

static Task OversizedLanguagePreferences()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-large-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "settings.json");
    try
    {
        File.WriteAllText(path, "{\"language\":\"en-US\",\"padding\":\"" + new string('x', 1024 * 1024) + "\"}");
        // Warm up parsing and measure only the bounded oversized read.
        LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-CN"));
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-CN")) == "zh-CN", "Oversized JSON was accepted.");
        Assert(GC.GetAllocatedBytesForCurrentThread() - before < 256 * 1024, "Oversized settings still caused an unbounded allocation.");
        File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, .. System.Text.Encoding.UTF8.GetBytes("{\"language\":\"en-US\"}")]);
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-CN")) == "en-US", "UTF-8 BOM compatibility was lost.");
        File.WriteAllText(path, "{\"language\":\"en-US\"}", System.Text.Encoding.Unicode);
        Assert(LanguagePreferences.Load(path, CultureInfo.GetCultureInfo("zh-CN")) == "en-US", "UTF-16 BOM compatibility was lost.");
    }
    finally { Directory.Delete(directory, recursive: true); }
    return Task.CompletedTask;
}

static async Task DirectoryLinkProtection()
{
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-links-{Guid.NewGuid():N}");
    var outside = Path.Combine(directory, "target");
    var link = Path.Combine(directory, "alias");
    Directory.CreateDirectory(outside);
    var settings = Path.Combine(outside, "settings.json");
    var temporary = Path.Combine(outside, $".settings-{Guid.NewGuid():N}.tmp");
    const string sentinel = "{\"language\":\"en-US\"}";
    File.WriteAllText(settings, sentinel);
    File.WriteAllText(temporary, "untouched");
    try
    {
        Directory.CreateSymbolicLink(link, outside);
        Assert(LanguagePreferences.Load(Path.Combine(link, "settings.json"), CultureInfo.GetCultureInfo("zh-CN")) == "zh-CN",
            "Configuration reads must not follow a directory alias.");
        await Throws<IOException>(() =>
        {
            LanguagePreferences.Save(Path.Combine(link, "new", "settings.json"), "en-US");
            return Task.CompletedTask;
        });
        Assert(!Directory.Exists(Path.Combine(outside, "new")), "Saving created a directory through a linked ancestor.");
        await Throws<IOException>(() =>
        {
            CleanupService.Run(() => { }, Path.Combine(directory, "absent", "settings.json"), Path.Combine(link, "settings.json"));
            return Task.CompletedTask;
        });
        Assert(File.ReadAllText(settings) == sentinel && File.Exists(temporary), "Cleanup crossed a directory link.");
    }
    finally
    {
        if (Directory.Exists(link)) Directory.Delete(link);
        Directory.Delete(directory, recursive: true);
    }
}

[SupportedOSPlatform("windows")]
static async Task WindowsDirectoryLease()
{
    if (!OperatingSystem.IsWindows()) return;
    var directory = Path.Combine(Path.GetTempPath(), $"BatteryCharge-pinned-{Guid.NewGuid():N}");
    var parent = Path.Combine(directory, "parent");
    var leaf = Path.Combine(parent, "leaf");
    Directory.CreateDirectory(leaf);
    try
    {
        using (SafeDirectory.Acquire(leaf))
        {
            await Throws<IOException>(() => { Directory.Move(parent, parent + "-moved"); return Task.CompletedTask; });
            Assert(WindowsDirectoryProbe.TryWrite(leaf) == 32,
                "A checked directory must deny write handles that could modify its reparse data.");
            // Child operations must remain possible while its ancestors are pinned.
            LanguagePreferences.Save(Path.Combine(leaf, "settings.json"), "en-US");
        }
        Assert(WindowsDirectoryProbe.TryWrite(leaf) == 0, "Directory write sharing was not restored after disposal.");
        Directory.Move(parent, parent + "-moved");
        Assert(Directory.Exists(parent + "-moved"), "Directory handles leaked after the lease ended.");
    }
    finally { Directory.Delete(directory, recursive: true); }
}

[SupportedOSPlatform("windows")]
static Task WindowsStartupAcl()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    static DirectorySecurity Security(string sddl)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var result = new DirectorySecurity();
        result.SetSecurityDescriptorSddlForm(sddl);
        return result;
    }
    var protectedAcl = Security("O:BAG:BAD:P(A;;FA;;;BA)(A;;FR;;;BU)");
    Assert(StartupPathSecurity.IsProtected(protectedAcl, false), "An administrator-owned read-only target was rejected.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BAG:BAD:P(A;;FA;;;BA)(A;;FW;;;BU)"), false), "User write permission was accepted.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BAG:BAD:P(A;;GW;;;BU)"), false), "An unmapped generic write ACE was accepted.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BAG:BAD:P(A;;GA;;;BU)"), true), "An unmapped generic all ACE was accepted on an ancestor.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BUG:BAD:P(A;;FR;;;BU)"), false), "An untrusted owner was accepted.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BAG:BAD:NO_ACCESS_CONTROL"), false), "An unrestricted DACL was accepted.");
    Assert(!StartupPathSecurity.IsProtected(Security("O:BAG:BAD:P(A;;DC;;;BU)"), true), "An ancestor allowing deletion of children was accepted.");
    Assert(StartupPathSecurity.IsProtected(Security("O:BAG:BAD:P(A;;AD;;;BU)"), true), "Creating unrelated sibling directories must not reject a protected child.");
    return Task.CompletedTask;
}

[SupportedOSPlatform("windows")]
static Task WindowsPrivateInstance()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    using var identity = WindowsIdentity.GetCurrent();
    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
    {
        if (Environment.GetEnvironmentVariable("CI") == "true")
            throw new InvalidOperationException("Windows CI must run elevated instance security checks.");
        Console.WriteLine("SKIP Private namespace checks require an elevated Windows console.");
        return Task.CompletedTask;
    }
    var namespaceName = $"BatteryChargeSecurityChecks.{Guid.NewGuid():N}";
    using var publicMutex = new Mutex(false, $"Global\\{namespaceName}");
    using (var first = new SingleInstance(namespaceName))
    {
        Assert(first.IsFirst, "A public precreated mutex blocked the private namespace.");
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--instance-probe");
        start.ArgumentList.Add(namespaceName);
        using var second = System.Diagnostics.Process.Start(start)!;
        Assert(second.WaitForExit(10000), "The second secure process did not exit.");
        var output = second.StandardOutput.ReadToEnd();
        Assert(second.ExitCode == 0 && output.Contains("INSTANCE False|True"),
            "The second process did not recognize the protected instance: " + output + second.StandardError.ReadToEnd());
        Assert(first.ShowWindow.WaitOne(TimeSpan.FromSeconds(2)), "The secure activation event was not shared.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var copy = Path.Combine(Path.GetTempPath(), $"BatteryCharge-instance-{Guid.NewGuid():N}");
            Directory.CreateDirectory(copy);
            try
            {
                var source = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                foreach (var file in Directory.EnumerateFiles(source))
                    File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
                var alternate = new System.Diagnostics.ProcessStartInfo(Path.Combine(copy, Path.GetFileName(Environment.ProcessPath!)))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                alternate.ArgumentList.Add("--instance-probe");
                alternate.ArgumentList.Add(namespaceName);
                using var relocated = System.Diagnostics.Process.Start(alternate)!;
                Assert(relocated.WaitForExit(10000), "The relocated instance did not exit.");
                var relocatedOutput = relocated.StandardOutput.ReadToEnd();
                Assert(relocated.ExitCode == 0 && relocatedOutput.Contains("INSTANCE False|False"),
                    "A relocated executable silently activated the old location: " + relocatedOutput + relocated.StandardError.ReadToEnd());
            }
            finally { Directory.Delete(copy, recursive: true); }
        }
    }
    using var restarted = new SingleInstance(namespaceName);
    Assert(restarted.IsFirst, "Closing the namespace left an unusable startup lock.");
    return Task.CompletedTask;
}

static Task AdaptiveWindowBounds()
{
    var cases = new[]
    {
        // Preserve a manually resized window that already fits.
        (new Rectangle(100, 80, 620, 500), new Rectangle(0, 0, 1920, 1040), new Rectangle(100, 80, 620, 500)),
        // A 768-pixel screen has less space once the taskbar is excluded.
        (new Rectangle(388, 0, 590, 819), new Rectangle(0, 0, 1366, 728), new Rectangle(388, 0, 590, 728)),
        // A high-DPI window must still fit on a narrow display.
        (new Rectangle(50, 100, 1180, 1638), new Rectangle(0, 0, 1024, 728), new Rectangle(0, 0, 1024, 728)),
        // The taskbar and additional monitors need not start at (0, 0).
        (new Rectangle(-1800, -100, 590, 900), new Rectangle(-1920, 40, 1920, 1000), new Rectangle(-1800, 40, 590, 900)),
        // Restore a window whose previous monitor was disconnected.
        (new Rectangle(2400, 1200, 590, 780), new Rectangle(40, 0, 1880, 1080), new Rectangle(1330, 300, 590, 780))
    };
    foreach (var (bounds, workingArea, expected) in cases)
    {
        var actual = WindowBounds.Fit(bounds, workingArea);
        Assert(actual == expected, $"Incorrect fitted window: {actual}; expected {expected}.");
        Assert(workingArea.Contains(actual), "A fitted window extends outside the monitor's working area.");
    }
    return Task.CompletedTask;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static Task FluentResourceCoverage()
{
    var resources = new ResourceManager("BatteryCharge.Core.Resources.Strings", typeof(UiText).Assembly);
    try
    {
        var chinese = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!;
        var chineseKeys = chinese.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet();
        var englishKeys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet();
        Assert(chineseKeys.SetEquals(englishKeys), "The two languages must provide exactly the same resource keys.");
        foreach (var key in chineseKeys)
        {
            var zh = chinese.GetString(key)!;
            var en = english.GetString(key)!;
            Assert(!string.IsNullOrWhiteSpace(zh) && !string.IsNullOrWhiteSpace(en), $"Empty translation: {key}.");
            static IEnumerable<string> Arguments(string text) => Regex.Matches(text, @"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}")
                .Select(match => match.Groups[1].Value).Order();
            Assert(Arguments(zh).SequenceEqual(Arguments(en)), $"Translation lost a format argument: {key}.");
        }
    }
    finally { resources.ReleaseAllResources(); }
    return Task.CompletedTask;
}

static Task FluentPaletteContrast()
{
    static double Luminance(Color color)
    {
        static double Linear(byte component)
        {
            var value = component / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    foreach (var palette in new[] { FluentPalette.Light, FluentPalette.Dark })
    {
        foreach (var (text, background) in new[]
        {
            (palette.Text, palette.Background), (palette.Text, palette.Surface),
            (palette.Secondary, palette.Background), (palette.Secondary, palette.Surface),
            (palette.Secondary, palette.AccentSoft), (palette.OnAccent, palette.Accent),
            (palette.Accent, palette.AccentSoft), (palette.Success, palette.Surface), (palette.Error, palette.Surface)
        })
        {
            var a = Luminance(text); var b = Luminance(background);
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Assert(ratio >= 4.5, $"Insufficient text contrast ({ratio:F2}:1) in {(palette.IsDark ? "dark" : "light")} mode.");
        }
    }
    return Task.CompletedTask;
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

static Task StartupAccountNames()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    const string path = @"D:\BatteryCharge\BatteryCharge.exe";
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    var task = XElement.Parse(StartupTaskDefinition.Create(sid, path));
    var trigger = task.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!;
    trigger.Element(ns + "UserId")!.Value = @"LAPTOP\Alice";
    string? Resolve(string account) => account == @"LAPTOP\Alice" ? sid : null;
    var status = StartupTaskDefinition.Read(task.ToString(), sid, path, Resolve);
    Assert(status.Enabled && status.UsesCurrentPath,
        "A registered task returning an account name for the same SID must remain enabled.");
    task.Element(ns + "Principals")!.Element(ns + "Principal")!.Element(ns + "UserId")!.Value = @"LAPTOP\Alice";
    status = StartupTaskDefinition.Read(task.ToString(), sid, path, Resolve);
    Assert(status.Enabled && status.UsesCurrentPath, "The principal can also identify the current user by account name.");
    StartupTaskDefinition.ParseOwned(task.ToString(), sid, Resolve);
    trigger.Element(ns + "Enabled")!.Value = "false";
    Assert(!StartupTaskDefinition.Read(task.ToString(), sid, path, Resolve).Enabled,
        "Account resolution must not hide a disabled trigger.");
    trigger.Element(ns + "Enabled")!.Value = "true";
    status = StartupTaskDefinition.Read(task.ToString(), sid, @"D:\New\BatteryCharge.exe", Resolve);
    Assert(status.Enabled && !status.UsesCurrentPath, "Account resolution must still detect a moved executable.");
    StartupTaskDefinition.Read(StartupTaskDefinition.Create(sid, path), sid, path,
        _ => throw new InvalidOperationException("A SID must not require account lookup."));
    return Task.CompletedTask;
}

static async Task StartupAccountProtection()
{
    const string sid = "S-1-5-21-123-456-789-1001";
    const string otherSid = "S-1-5-21-123-456-789-1002";
    const string path = @"D:\BatteryCharge\BatteryCharge.exe";
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    string? Resolve(string account) => account switch
    {
        @"LAPTOP\Alice" => sid,
        @"LAPTOP\Bob" => otherSid,
        _ => null
    };
    foreach (var identity in new[] { @"LAPTOP\Bob", @"LAPTOP\Unknown", otherSid, "", " " })
    {
        var task = XElement.Parse(StartupTaskDefinition.Create(sid, path));
        var triggerUser = task.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "UserId")!;
        triggerUser.Value = identity;
        Assert(!StartupTaskDefinition.Read(task.ToString(), sid, path, Resolve).Enabled,
            "A foreign, unresolved or empty trigger identity must not enable startup.");
        triggerUser.Value = sid;
        task.Element(ns + "Principals")!.Element(ns + "Principal")!.Element(ns + "UserId")!.Value = identity;
        await Throws<InvalidOperationException>(() =>
        {
            StartupTaskDefinition.ParseOwned(task.ToString(), sid, Resolve);
            return Task.CompletedTask;
        });
    }
    var missing = XElement.Parse(StartupTaskDefinition.Create(sid, path));
    missing.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "UserId")!.Remove();
    Assert(!StartupTaskDefinition.Read(missing.ToString(), sid, path, Resolve).Enabled,
        "An all-users trigger must not count as startup for the current user.");
    missing.Element(ns + "Principals")!.Element(ns + "Principal")!.Element(ns + "UserId")!.Remove();
    await Throws<InvalidOperationException>(() =>
    {
        StartupTaskDefinition.ParseOwned(missing.ToString(), sid, Resolve);
        return Task.CompletedTask;
    });
    var foreign = XElement.Parse(StartupTaskDefinition.Create(sid, path));
    foreign.Element(ns + "RegistrationInfo")!.Element(ns + "Source")!.Value = "AnotherApp";
    await Throws<InvalidOperationException>(() =>
    {
        StartupTaskDefinition.ParseOwned(foreign.ToString(), sid, Resolve);
        return Task.CompletedTask;
    });
}

static Task MissingStartupTask()
{
    const int fileNotFound = unchecked((int)0x80070002);
    var mapped = Marshal.GetExceptionForHR(fileNotFound, new IntPtr(-1))!;
    Assert(mapped is FileNotFoundException, "The runtime must map ERROR_FILE_NOT_FOUND to FileNotFoundException.");
    foreach (var error in new Exception[] { new COMException("Task not found", fileNotFound), mapped })
    {
        var task = StartupTaskLookup.Find(() => throw error);
        Assert(task is null, "A missing task must be treated as unregistered.");
    }
    return Task.CompletedTask;
}

static Task StartupLookupFailures()
{
    var existing = new object();
    Assert(ReferenceEquals(StartupTaskLookup.Find(() => existing), existing),
        "An existing task must be returned unchanged.");
    foreach (var error in new Exception[]
    {
        new UnauthorizedAccessException("Access denied"),
        new COMException("Scheduler unavailable", unchecked((int)0x800706BA)),
        new DirectoryNotFoundException("Task folder unavailable"),
        new InvalidOperationException("Unexpected scheduler failure")
    })
    {
        try
        {
            StartupTaskLookup.Find(() => throw error);
        }
        catch (Exception actual)
        {
            Assert(ReferenceEquals(actual, error), "Task lookup must preserve the original scheduler error.");
            continue;
        }
        throw new InvalidOperationException("A scheduler failure was incorrectly treated as a missing task.");
    }
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
    Assert(status.CurrentExecutablePath == @"D:\New\BatteryCharge.exe"
        && status.RegisteredExecutablePath == @"D:\Old\BatteryCharge.exe",
        "Both paths must be retained so the UI can explain the mismatch.");
    XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    var disabled = XElement.Parse(xml);
    disabled.Element(ns + "Settings")!.Element(ns + "Enabled")!.Value = "false";
    status = StartupTaskDefinition.Read(disabled.ToString(), sid, @"D:\New\BatteryCharge.exe");
    Assert(!status.Enabled && !status.UsesCurrentPath && status.RegisteredExecutablePath == @"D:\Old\BatteryCharge.exe",
        "A disabled task must still expose its stale startup path.");
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

[SupportedOSPlatform("windows")]
static class WindowsDirectoryProbe
{
    internal static int TryWrite(string directory)
    {
        using var handle = CreateFileW(directory, 0x40000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        return handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access,
        uint share, IntPtr attributes, uint disposition, uint flags, IntPtr template);
}

sealed class BlockingTransport : IEnergyTransport, IDisposable
{
    internal ManualResetEventSlim Entered { get; } = new();
    internal ManualResetEventSlim Release { get; } = new();
    internal ManualResetEventSlim Completed { get; } = new();
    internal int Writes;
    internal bool BlockWrites { get; init; }
    public uint Query(uint code, uint input)
    {
        if (BlockWrites) return code == ChargeProtocol.NightControlCode ? 1u : 0u;
        Entered.Set();
        Release.Wait();
        Completed.Set();
        return code == ChargeProtocol.NightControlCode ? 1u : 0u;
    }
    public void Send(uint code, uint input)
    {
        Interlocked.Increment(ref Writes);
        if (BlockWrites)
        {
            Entered.Set();
            Release.Wait();
            Completed.Set();
        }
    }
    public void Dispose()
    {
        // The worker may be finishing its final query after Completed is signaled.
        // These lightweight test signals intentionally remain valid until process exit.
        Release.Set();
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
