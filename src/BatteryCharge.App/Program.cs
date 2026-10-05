using BatteryCharge.Core;
using System.Globalization;

namespace BatteryCharge.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        UiText.SetLanguage(LanguagePreferences.Load(LanguagePreferences.SettingsPath, CultureInfo.CurrentUICulture));
        ApplicationConfiguration.Initialize();

        // Prevent two instances from interleaving firmware command sequences.
        using var instance = new Mutex(true, @"Global\BatteryCharge.Standalone", out var firstInstance);
        if (!firstInstance)
        {
            if (!args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show(UiText.Get("AlreadyRunning"),
                    UiText.Get("AppName"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var device = new EnergyDevice();
            var controller = new ChargeController(device);
            using var window = new MainForm(controller,
                startInTray: args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
            Application.Run(window);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
