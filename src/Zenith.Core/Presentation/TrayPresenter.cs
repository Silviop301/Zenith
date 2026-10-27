using System.Globalization;
using Zenith.Core.Resources;
using Zenith.Core.Sensors;

namespace Zenith.Core.Presentation;

/// <summary>The top dot of the tray icon: night blue at rest, purple while Zenith changes something, amber when something needs the user.</summary>
public enum TrayStatus { Rest, Active, Alert }

public sealed record TrayState(TrayStatus Status, string Tooltip);

/// <summary>
/// A critical alert shown once as a Windows notification. <see cref="Key"/> and <see cref="Value"/> say what was
/// already shown, so the same alert is not repeated until the value changes.
/// </summary>
public sealed record TrayAlert(string Key, double Value, string Title, string Text);

/// <summary>What the tray icon shows. Security alerts are never behind Pro.</summary>
public static class TrayPresenter
{
    /// <summary>Critical alerts from the sensors. Today: disks with read errors they could not correct, a sign of a failing disk.</summary>
    public static IReadOnlyList<TrayAlert> Alerts(IEnumerable<SensorSample> sensors) =>
        sensors.Where(s => s.Sensor == SensorKeys.DiskReadErrors && s.Value > 0)
            .Select(s => new TrayAlert($"{s.Sensor}|{s.Target}", s.Value, Strings.DiskAlertTitle,
                s.Value == 1
                    ? string.Format(CultureInfo.CurrentCulture, Strings.DiskAlertText_One, s.Target)
                    : string.Format(CultureInfo.CurrentCulture, Strings.DiskAlertText_Other, s.Target, s.Value)))
            .ToList();

    /// <param name="serviceReady">null while still checking.</param>
    /// <param name="overview">null before the first scan finishes.</param>
    public static TrayState State(bool? serviceReady, bool busy, bool scanning, ResgateOverview? overview, int alerts)
    {
        if (busy) return new(TrayStatus.Active, Strings.TrayBusy);
        if (alerts > 0) return new(TrayStatus.Alert, Strings.TrayDiskAlert);
        if (serviceReady == false) return new(TrayStatus.Alert, Strings.TrayServiceOff);
        if (overview is null || scanning) return new(TrayStatus.Rest, Strings.TrayScanning);

        var toSee = overview.ApplyNow + overview.NeedsYou;
        if (toSee == 0) return new(TrayStatus.Rest, Strings.TrayRest);
        // Amber only for what Zenith can fix now; a guide the user already read should not keep the icon lit.
        return new(overview.ApplyNow > 0 ? TrayStatus.Alert : TrayStatus.Rest,
            FindingPresenter.Plural(toSee, Strings.TrayPending_One, Strings.TrayPending_Other));
    }
}
