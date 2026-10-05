using System.Globalization;
using System.Resources;

namespace BatteryCharge.Core;

public static class UiText
{
    private static readonly ResourceManager Resources = new(
        "BatteryCharge.Core.Resources.Strings", typeof(UiText).Assembly);
    private static CultureInfo _culture = CultureInfo.GetCultureInfo(DefaultLanguage(CultureInfo.CurrentUICulture));

    public static string Language => _culture.Name;
    public static bool IsSupported(string? language) => language is "zh-CN" or "en-US";
    public static string DefaultLanguage(CultureInfo systemCulture) =>
        systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US";

    public static void SetLanguage(string language)
    {
        if (!IsSupported(language))
            throw new ArgumentOutOfRangeException(nameof(language));
        // Use an explicit culture: pending async callbacks may retain an older thread culture.
        _culture = CultureInfo.GetCultureInfo(language);
    }

    public static string Get(string key, params object?[] arguments)
    {
        var culture = _culture;
        var text = Resources.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing translation: {key}");
        return arguments.Length == 0 ? text : string.Format(culture, text, arguments);
    }

    public static string ModeName(ChargeMode mode) => Get(mode switch
    {
        ChargeMode.Normal => "ModeNormal",
        ChargeMode.Conservation => "ModeConservation",
        ChargeMode.RapidCharge => "ModeRapid",
        _ => "ModeUnknown"
    });
}
