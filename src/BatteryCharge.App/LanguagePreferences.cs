using System.Globalization;
using System.Text.Json;
using BatteryCharge.Core;

namespace BatteryCharge.App;

internal static class LanguagePreferences
{
    internal const int MaximumSettingsBytes = 64 * 1024;
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
            var full = Path.GetFullPath(path);
            using var lease = SafeDirectory.Acquire(Path.GetDirectoryName(full)!);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                return null;
            using var stream = OperatingSystem.IsWindows()
                ? new FileStream(SafeDirectory.OpenRegularFile(full, readData: true), FileAccess.Read)
                : new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Bound the read itself, including files that grow after they were opened.
            var bytes = new byte[MaximumSettingsBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0)
                    break;
                count += read;
            }
            if (count > MaximumSettingsBytes)
                return null;
            // Preserve the previous BOM-aware decoding, using only the bounded bytes.
            using var reader = new StreamReader(new MemoryStream(bytes, 0, count, writable: false));
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("language", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is string language && UiText.IsSupported(language))
                return language;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
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
        using var lease = SafeDirectory.Acquire(directory, create: true);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, new { language });
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
