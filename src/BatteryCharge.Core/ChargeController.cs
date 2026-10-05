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

    public ChargeController(
        IEnergyTransport transport,
        int verificationAttempts = 12,
        TimeSpan? verificationInterval = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentOutOfRangeException.ThrowIfLessThan(verificationAttempts, 1);
        _transport = transport;
        _verificationAttempts = verificationAttempts;
        _verificationInterval = verificationInterval ?? TimeSpan.FromMilliseconds(80);
        if (_verificationInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(verificationInterval));
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
                _transport.Send(ChargeProtocol.ModeControlCode, command);

            return Verify(ReadMode, mode, "充电模式");
        });
    }

    public Task<bool> SetNightChargeAsync(bool enabled) => SerializedAsync(() =>
    {
        // Invalid/unsupported night state must prevent the write.
        _ = ReadNightCharge();
        _transport.Send(ChargeProtocol.NightControlCode, ChargeProtocol.NightCommand(enabled));
        return Verify(ReadNightCharge, enabled, "夜间充电");
    });

    private ChargeMode ReadMode() => ChargeProtocol.DecodeMode(
        _transport.Query(ChargeProtocol.ModeControlCode, ChargeProtocol.ModeQuery));

    private bool ReadNightCharge() => ChargeProtocol.DecodeNightCharge(
        _transport.Query(ChargeProtocol.NightControlCode, ChargeProtocol.NightQuery));

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

        throw new TimeoutException($"{feature}设置未生效。目标：{expected}；实际：{actual}。请刷新状态后重试。");
    }

    private async Task<T> SerializedAsync<T>(Func<T> operation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Synchronous driver I/O and verification must not block the UI thread.
            return await Task.Run(operation).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
