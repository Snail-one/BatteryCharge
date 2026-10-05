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

        try
        {
            using var instance = new SingleInstance();
            if (!instance.IsFirst)
            {
                if (!args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
                {
                    if (instance.CanActivate)
                        instance.ShowWindow.Set();
                    else
                        MessageBox.Show(UiText.Get("DifferentInstance"), UiText.Get("AppName"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            using var device = new EnergyDevice();
            var controller = new ChargeController(device);
            using var window = new MainForm(controller,
                startInTray: args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
            // A tray-only launch also needs a handle for UI-thread dispatch.
            _ = window.Handle;
            var listener = ThreadPool.RegisterWaitForSingleObject(instance.ShowWindow,
                (_, _) => window.RequestShowWindow(), null, Timeout.Infinite, executeOnlyOnce: false);
            try
            {
                Application.Run(window);
            }
            finally
            {
                listener.Unregister(null);
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, UiText.Get("AppName"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
