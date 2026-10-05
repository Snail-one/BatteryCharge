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

        // Create the signal before taking the mutex so a launch during initialization
        // remains pending until the first instance starts listening.
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\BatteryCharge.Standalone.ShowWindow");
        // Prevent two instances from interleaving firmware command sequences.
        using var instance = new Mutex(true, @"Global\BatteryCharge.Standalone", out var firstInstance);
        if (!firstInstance)
        {
            if (!args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
                showWindow.Set();
            return;
        }

        try
        {
            using var device = new EnergyDevice();
            var controller = new ChargeController(device);
            using var window = new MainForm(controller,
                startInTray: args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
            // A tray-only launch also needs a handle for UI-thread dispatch.
            _ = window.Handle;
            var listener = ThreadPool.RegisterWaitForSingleObject(showWindow,
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
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
