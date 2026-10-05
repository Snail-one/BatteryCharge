using BatteryCharge.Core;

namespace BatteryCharge.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Prevent two instances from interleaving firmware command sequences.
        using var instance = new Mutex(true, @"Global\BatteryCharge.Standalone", out var firstInstance);
        if (!firstInstance)
        {
            if (!args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show("电池充电助手已在运行，请打开系统托盘中的图标。",
                    "电池充电助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
