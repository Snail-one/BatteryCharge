using System.Globalization;
using System.Text.Json;
using BatteryCharge.Core;

namespace BatteryCharge.App;

internal static class LanguagePreferences
{
    internal static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    internal static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatteryCharge", "settings.json");

    internal static string Load(string path, CultureInfo systemCulture) =>
        ReadLanguage(path) ?? UiText.DefaultLanguage(systemCulture);

    private static string? ReadLanguage(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("language", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is string language && UiText.IsSupported(language))
                return language;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing, unreadable or damaged preferences must not prevent startup.
        }
        return null;
    }

    internal static void Save(string path, string language)
    {
        if (!UiText.IsSupported(language))
            throw new ArgumentOutOfRangeException(nameof(language));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { language }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
