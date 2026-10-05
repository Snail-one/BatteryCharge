using BatteryCharge.Core;

namespace BatteryCharge.App;

internal static class CleanupService
{
    internal static void Run(Action removeStartupTask, string settingsPath)
    {
        try
        {
            // Keep preferences intact if the startup task could not be removed.
            removeStartupTask();
        }
        catch (Exception error)
        {
            throw new IOException(UiText.Get("CleanupStartupFailed", error.Message), error);
        }

        DeleteSettings(settingsPath);
    }

    private static void DeleteSettings(string settingsPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
        try
        {
            if (!Directory.Exists(directory))
                return;
            using var lease = SafeDirectory.Acquire(directory);
            File.Delete(settingsPath);
            foreach (var temporary in Directory.EnumerateFiles(directory, ".settings-*.tmp", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(temporary);
                var token = name[".settings-".Length..^".tmp".Length];
                if (Guid.TryParseExact(token, "N", out _))
                    File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException(UiText.Get("CleanupSettingsFailed", settingsPath, error.Message), error);
        }
    }
}
