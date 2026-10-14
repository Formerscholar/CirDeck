using Microsoft.Win32;

namespace CirDeck.Interop;

public static class StartupRegistration
{
    private const string ValueName = "CirDeck";
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key == null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(ValueName, "\"" + executablePath + "\"");
    }
}
