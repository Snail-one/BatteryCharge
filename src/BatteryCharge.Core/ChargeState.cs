namespace BatteryCharge.Core;

public enum ChargeMode
{
    Normal,
    Conservation,
    RapidCharge
}

public sealed record FeatureReading<T>(T? Value, string? Error) where T : struct
{
    public bool IsAvailable => Value.HasValue;

    public static FeatureReading<T> Available(T value) => new(value, null);
    public static FeatureReading<T> Unavailable(string error) => new(null, error);
}

public sealed record ChargeSnapshot(
    FeatureReading<ChargeMode> Mode,
    FeatureReading<bool> NightCharge,
    DateTimeOffset ReadAt);
