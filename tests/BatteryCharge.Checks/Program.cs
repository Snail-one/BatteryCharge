using BatteryCharge.Core;

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
    ("Concurrent requests cannot split a mode sequence", ConcurrentRequestsAreSerialized)
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
