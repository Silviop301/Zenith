using Zenith.Core.Presentation;
using Zenith.Core.Sensors;

namespace Zenith.Core.Tests;

public sealed class TrayPresenterTests
{
    private static ResgateOverview Overview(int applyNow, int needsYou) =>
        new("", "", "", applyNow + needsYou, applyNow, needsYou, null, "", "", "");

    [Fact]
    public void Busy_wins_over_everything()
    {
        Assert.Equal(TrayStatus.Active, TrayPresenter.State(true, busy: true, scanning: false, Overview(2, 0), alerts: 1).Status);
    }

    [Fact]
    public void Fixable_findings_light_the_dot_but_guides_alone_do_not()
    {
        var fixable = TrayPresenter.State(true, false, false, Overview(1, 1), 0);
        Assert.Equal(TrayStatus.Alert, fixable.Status);
        Assert.Equal("Zenith: 2 achados para ver", fixable.Tooltip);

        var guideOnly = TrayPresenter.State(true, false, false, Overview(0, 1), 0);
        Assert.Equal(TrayStatus.Rest, guideOnly.Status);
        Assert.Equal("Zenith: 1 achado para ver", guideOnly.Tooltip);
    }

    [Fact]
    public void Service_off_and_disk_alerts_are_amber()
    {
        Assert.Equal(TrayStatus.Alert, TrayPresenter.State(false, false, false, Overview(0, 0), 0).Status);
        Assert.Equal(TrayStatus.Alert, TrayPresenter.State(true, false, false, Overview(0, 0), 1).Status);
        Assert.Equal(TrayStatus.Rest, TrayPresenter.State(true, false, false, Overview(0, 0), 0).Status);
        Assert.Equal(TrayStatus.Rest, TrayPresenter.State(null, false, true, null, 0).Status);
    }

    [Fact]
    public void Only_disks_with_uncorrected_read_errors_raise_an_alert()
    {
        var now = DateTimeOffset.Now;
        var alerts = TrayPresenter.Alerts([
            new(SensorKeys.DiskReadErrors, "ST1000DM003 (disco 0)", 0, now),
            new(SensorKeys.DiskReadErrors, "SSSTC CL1 (disco 2)", 3, now),
            new(SensorKeys.DiskTemperature, "SSSTC CL1 (disco 2)", 60, now),
        ]);
        var alert = Assert.Single(alerts);
        Assert.Equal("disk.read-errors-uncorrected|SSSTC CL1 (disco 2)", alert.Key);
        Assert.Contains("3 erros de leitura", alert.Text);
        Assert.Contains("SSSTC CL1 (disco 2)", alert.Text);
    }
}
