namespace BatteryCharge.App;

internal interface IStartupManager
{
    StartupRegistration Read();
    void SetEnabled(bool enabled);
    void RemoveForCleanup();
}
