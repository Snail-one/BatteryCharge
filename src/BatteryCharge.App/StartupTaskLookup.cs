namespace BatteryCharge.App;

internal static class StartupTaskLookup
{
    internal static object? Find(Func<object> getTask)
    {
        try
        {
            return getTask();
        }
        // COM interop maps ERROR_FILE_NOT_FOUND to FileNotFoundException.
        // Only a missing task from this lookup means startup is unregistered.
        catch (Exception error) when (error.HResult == unchecked((int)0x80070002))
        {
            return null;
        }
    }
}
