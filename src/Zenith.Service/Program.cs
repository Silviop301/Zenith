using Zenith.Core.Changes;
using Zenith.Core.Fixes;
using Zenith.Core.Ipc;
using Zenith.Core.Sensors;
using Zenith.Core.Settings;
using Zenith.Service;

// Zenith core service: owns the change journal and applies machine-wide settings.
// Runs as LocalSystem in session 0, so it never touches per-user settings (display, profile).

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Zenith");

var dataDir = DataDirectory.Create();
var journalPath = Path.Combine(dataDir, "journal.db");

builder.Services.AddSingleton(_ => ChangeJournal.Open(journalPath));
builder.Services.AddSingleton(_ => SensorStore.Open(Path.Combine(dataDir, "sensors.db")));
builder.Services.AddSingleton(sp => new ChangeEngine(
    sp.GetRequiredService<ChangeJournal>(),
    [new ActivePowerSchemeSetting(), new ProcessorMaxStateAcSetting(), new StartupSetting(machineWide: true)]));
builder.Services.AddSingleton(sp =>
{
    var engine = sp.GetRequiredService<ChangeEngine>();
    var sensors = sp.GetRequiredService<SensorStore>();
    return new CommandHandler(sp.GetRequiredService<ChangeJournal>(), engine, () => PowerPlanFix.Apply(engine), journalPath, sensors.Latest,
        target => StartupFix.Disable(engine, target));
});
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<SensorWorker>();

builder.Build().Run();
