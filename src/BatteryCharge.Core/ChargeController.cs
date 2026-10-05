namespace BatteryCharge.Core;

/// <summary>
/// Owns no device or UI resources. All requests, including complete write sequences,
/// share one gate so operations cannot interleave.
/// </summary>
public sealed class ChargeController
{
    private readonly IEnergyTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _verificationAttempts;
    private readonly TimeSpan _verificationInterval;
    private readonly TimeSpan _operationTimeout;
    private readonly CancellationTokenSource _faulted = new();

    public ChargeController(
        IEnergyTransport transport,
        int verificationAttempts = 12,
        TimeSpan? verificationInterval = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentOutOfRangeException.ThrowIfLessThan(verificationAttempts, 1);
        _transport = transport;
        _verificationAttempts = verificationAttempts;
        _verificationInterval = verificationInterval ?? TimeSpan.FromMilliseconds(80);
        if (_verificationInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(verificationInterval));
        _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(15);
        if (_operationTimeout <= TimeSpan.Zero || _operationTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    public Task<ChargeSnapshot> ReadAsync() => SerializedAsync(() => new ChargeSnapshot(
        ReadFeature(ReadMode),
        ReadFeature(ReadNightCharge),
        DateTimeOffset.Now));

    public Task<ChargeMode> SetModeAsync(ChargeMode mode)
    {
        // Validate before acquiring the gate or talking to hardware.
        var commands = ChargeProtocol.CommandsFor(mode);
        return SerializedAsync(() =>
        {
            _ = ReadMode();
            foreach (var command in commands)
            {
                ThrowIfFaulted();
                _transport.Send(ChargeProtocol.ModeControlCode, command);
            }

            return Verify(ReadMode, mode, "ChargeMode");
        });
    }

    public Task<bool> SetNightChargeAsync(bool enabled) => SerializedAsync(() =>
    {
        // Invalid/unsupported night state must prevent the write.
        _ = ReadNightCharge();
        ThrowIfFaulted();
        _transport.Send(ChargeProtocol.NightControlCode, ChargeProtocol.NightCommand(enabled));
        return Verify(ReadNightCharge, enabled, "NightCharge");
    });

    private ChargeMode ReadMode()
    {
        ThrowIfFaulted();
        return ChargeProtocol.DecodeMode(_transport.Query(ChargeProtocol.ModeControlCode, ChargeProtocol.ModeQuery));
    }

    private bool ReadNightCharge()
    {
        ThrowIfFaulted();
        return ChargeProtocol.DecodeNightCharge(_transport.Query(ChargeProtocol.NightControlCode, ChargeProtocol.NightQuery));
    }

    private void ThrowIfFaulted()
    {
        if (_faulted.IsCancellationRequested)
            throw new IOException(UiText.Get("DriverOperationTimedOut"));
    }

    private static FeatureReading<T> ReadFeature<T>(Func<T> query) where T : struct
    {
        try
        {
            return FeatureReading<T>.Available(query());
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            return FeatureReading<T>.Unavailable(error.Message);
        }
    }

    private T Verify<T>(Func<T> query, T expected, string feature) where T : struct
    {
        T actual = default;
        for (var attempt = 0; attempt < _verificationAttempts; attempt++)
        {
            if (attempt > 0 && _verificationInterval > TimeSpan.Zero)
                Thread.Sleep(_verificationInterval);

            actual = query();
            if (EqualityComparer<T>.Default.Equals(actual, expected))
                return actual;
        }

        throw new TimeoutException(UiText.Get("VerificationFailed", UiText.Get(feature), ValueName(expected), ValueName(actual)));
    }

    private static string ValueName<T>(T value) where T : struct => value switch
    {
        ChargeMode mode => UiText.ModeName(mode),
        bool enabled => UiText.Get(enabled ? "Enabled" : "Disabled"),
        _ => value.ToString() ?? ""
    };

    private async Task<T> SerializedAsync<T>(Func<T> operation)
    {
        if (_faulted.IsCancellationRequested)
            throw new IOException(UiText.Get("DriverOperationTimedOut"));
        try
        {
            await _gate.WaitAsync(_faulted.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_faulted.IsCancellationRequested)
        {
            throw new IOException(UiText.Get("DriverOperationTimedOut"));
        }
        if (_faulted.IsCancellationRequested)
        {
            _gate.Release();
            throw new IOException(UiText.Get("DriverOperationTimedOut"));
        }
        // The worker owns the gate until the native operation actually returns.
        // A UI timeout never releases it or permits another firmware write.
        var worker = Task.Run(() =>
        {
            try { return operation(); }
            finally { _gate.Release(); }
        });
        using var deadline = new CancellationTokenSource();
        var timeout = Task.Delay(_operationTimeout, deadline.Token);
        if (await Task.WhenAny(worker, timeout).ConfigureAwait(false) == worker)
        {
            deadline.Cancel();
            return await worker.ConfigureAwait(false);
        }
        _faulted.Cancel();
        // Observe a late failure even after the caller has stopped waiting.
        _ = worker.ContinueWith(task => _ = task.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new IOException(UiText.Get("DriverOperationTimedOut"));
    }
}
