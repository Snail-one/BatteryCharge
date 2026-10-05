namespace BatteryCharge.Core;

// Protocol values were researched from LLT. This implementation has no LLT dependency.
public static class ChargeProtocol
{
    public const uint ModeControlCode = 0x831020F8;
    public const uint NightControlCode = 0x83102150;
    public const uint ModeQuery = 0xFF;
    public const uint NightQuery = 0x11;

    public static ChargeMode DecodeMode(uint flags)
    {
        if ((flags & 0x20) != 0)
            return ChargeMode.Conservation;

        return (flags & 0x04) != 0 ? ChargeMode.RapidCharge : ChargeMode.Normal;
    }

    public static uint[] CommandsFor(ChargeMode mode) => mode switch
    {
        ChargeMode.Normal => [0x05, 0x08],
        ChargeMode.Conservation => [0x08, 0x03],
        ChargeMode.RapidCharge => [0x05, 0x07],
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static bool DecodeNightCharge(uint flags)
    {
        if ((flags & 1) == 0)
            throw new NotSupportedException(UiText.Get("InvalidNightState", flags));

        return (flags & 0x10) != 0;
    }

    public static uint NightCommand(bool enabled) => enabled ? 0x80000012 : 0x12;
}
