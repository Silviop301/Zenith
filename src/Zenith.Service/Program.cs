using Zenith.Core.Changes;
using Zenith.Core.Fixes;
using Zenith.Core.Ipc;
using Zenith.Core.Settings;
using Zenith.Service;

// Zenith core service: owns the change journal and applies machine-wide settings.
// Runs as LocalSystem in session 0, so it never touches per-user settings (display, profile).

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Zenith");

var dataDir = DataDirectory.Create();
var journalPath = Path.Combine(dataDir, "journal.db");

builder.Services.AddSingleton(_ => ChangeJournal.Open(journalPath));
builder.Services.AddSingleton(sp => new ChangeEngine(
    sp.GetRequiredService<ChangeJournal>(), [new ActivePowerSchemeSetting(), new ProcessorMaxStateAcSetting()]));
builder.Services.AddSingleton(sp =>
{
    var engine = sp.GetRequiredService<ChangeEngine>();
    return new CommandHandler(sp.GetRequiredService<ChangeJournal>(), engine, () => PowerPlanFix.Apply(engine), journalPath);
});
builder.Services.AddHostedService<PipeServer>();

builder.Build().Run();
