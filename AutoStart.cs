using Microsoft.Win32;

namespace ImmichUploader;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ImmichUploader";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        var existing = key?.GetValue(ValueName) as string;
        return !string.IsNullOrEmpty(existing);
    }

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                exe = Path.Combine(AppContext.BaseDirectory, "ImmichUploader.exe");
            }
            key.SetValue(ValueName, $"\"{exe}\" --tray");
        }
        else
        {
            if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
        }
    }
}
