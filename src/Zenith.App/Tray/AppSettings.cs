using Microsoft.Win32;

namespace Zenith.App.Tray;

/// <summary>
/// The app's own preferences, per user, in HKCU\Software\Zenith. These are Zenith's settings, not changes to the PC,
/// so they do not go through the journal.
/// </summary>
public static class AppSettings
{
    private const string Key = @"Software\Zenith";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Zenith";
    public const string TrayArgument = "--bandeja";

    /// <summary>
    /// Whether zenith.exe starts in the tray when the user signs in. The first time the app runs for a user, it takes
    /// the installer's choice (HKLM\Software\Zenith\StartWithWindows); after that, the user's own.
    /// A copy that was not installed (run from Downloads, say) never registers itself.
    /// </summary>
    public static bool StartWithWindows
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(RunValue) is string;
        }
        set
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) run.SetValue(RunValue, $"\"{Environment.ProcessPath}\" {TrayArgument}");
            else run.DeleteValue(RunValue, throwOnMissingValue: false);
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("StartWithWindowsChosen", 1, RegistryValueKind.DWord);
        }
    }

    /// <summary>Applies the installer's default once per user, and keeps the Run entry pointing at this exe after an update.</summary>
    public static void ApplyStartupDefault()
    {
        using var machine = Registry.LocalMachine.OpenSubKey(Key);
        if (machine?.GetValue("StartWithWindows") is not int installerDefault) return; // Not installed.

        using var user = Registry.CurrentUser.OpenSubKey(Key);
        var chosen = user?.GetValue("StartWithWindowsChosen") is int;
        StartWithWindows = chosen ? StartWithWindows : installerDefault != 0;
    }

    /// <summary>The "still in the tray" hint shows once, the first time the window is closed.</summary>
    public static bool TrayHintShown
    {
        get => Read<int>("TrayHintShown") == 1;
        set => Write("TrayHintShown", value ? 1 : 0);
    }

    /// <summary>True when this alert, with this value, was already shown to this user.</summary>
    public static bool AlertShown(string alertKey, double value) =>
        Read<string>(@"Alerts\" + alertKey) == value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static void MarkAlertShown(string alertKey, double value) =>
        Write(@"Alerts\" + alertKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static T? Read<T>(string name)
    {
        var (sub, value) = Split(name);
        using var key = Registry.CurrentUser.OpenSubKey(sub);
        return key?.GetValue(value) is T t ? t : default;
    }

    private static void Write(string name, object value)
    {
        var (sub, valueName) = Split(name);
        using var key = Registry.CurrentUser.CreateSubKey(sub);
        key.SetValue(valueName, value);
    }

    private static (string Key, string Value) Split(string name)
    {
        var slash = name.LastIndexOf('\\');
        return slash < 0 ? (Key, name) : ($@"{Key}\{name[..slash]}", name[(slash + 1)..]);
    }
}
