using System.Security.Principal;
using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Fixes;
using Zenith.Core.Ipc;
using Zenith.Core.Sensors;
using Zenith.Core.Settings;

namespace Zenith.Core.Agent;

/// <summary>
/// The user-session side of Zenith, shared by the probe and the app: user settings are changed here,
/// machine settings by the service, and every change is recorded in the service's journal.
/// One connection per agent; create one per operation.
/// </summary>
public sealed class SessionAgent : IDisposable
{
    private readonly ServiceClient _client;
    private readonly ChangeEngine _engine;

    private SessionAgent(ServiceClient client)
    {
        _client = client;
        _engine = new ChangeEngine(new RemoteJournal(client), [new RefreshRateSetting(), new HdrSetting(), new GpuPreferenceSetting(), new StartupSetting(machineWide: false)]);
    }

    /// <exception cref="ServiceUnavailableException">The service is not running.</exception>
    public static SessionAgent Connect(TimeSpan? timeout = null) => new(ServiceClient.Connect(timeout));

    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Não deu para identificar o usuário atual.");

    /// <summary>Findings Zenith can fix by itself. The others get a guide.</summary>
    public static bool CanApply(string findingId) => findingId is
        FindingIds.RefreshRate or FindingIds.Hdr or FindingIds.PowerPlan or FindingIds.GameOnIntegratedGpu;

    public PingResult Ping() => _client.Call<PingResult>(Protocol.Commands.Ping);

    /// <summary>
    /// Fixes one finding and returns the changes made (empty when there was nothing to change).
    /// For HDR the caller must run the keep-or-revert test and call <see cref="Revert"/> when the user does not confirm.
    /// </summary>
    public IReadOnlyList<JournalEntry> Apply(string findingId, MachineSnapshot machine) => findingId switch
    {
        FindingIds.RefreshRate => UserFixes.RefreshRate(_engine, machine, UserSid),
        FindingIds.Hdr => UserFixes.Hdr(_engine, machine, UserSid),
        FindingIds.PowerPlan => _client.Call<List<JournalEntry>>(Protocol.Commands.PowerFix),
        FindingIds.GameOnIntegratedGpu => UserFixes.GpuPreference(_engine, machine, UserSid),
        _ => throw new InvalidOperationException($"O achado {findingId} não tem conserto automático."),
    };

    /// <summary>Undoes changes just made, newest first (used when the HDR test is not confirmed).</summary>
    public IReadOnlyList<UndoResult> Revert(IEnumerable<JournalEntry> applied) =>
        applied.OrderByDescending(e => e.Id).Select(e => Undo(e)).ToList();

    /// <summary>
    /// Bestiário: stops a program from starting with Windows. This user's entries change here; machine-wide ones
    /// through the service. Returns null when it was already off.
    /// </summary>
    public JournalEntry? DisableStartup(string target) =>
        StartupTarget.IsMachineWide(StartupTarget.Parse(target).Location)
            ? _client.Call<JournalEntry?>(Protocol.Commands.StartupDisable, new StartupArgs(target))
            : StartupFix.Disable(_engine, target, UserSid);

    public IReadOnlyList<JournalEntry> Journal() => new RemoteJournal(_client).List();

    /// <summary>The newest reading of each sensor the service collects.</summary>
    public IReadOnlyList<SensorSample> LatestSensors() => _client.Call<List<SensorSample>>(Protocol.Commands.SensorsLatest);

    public UndoResult Undo(long id)
    {
        var entry = new RemoteJournal(_client).Get(id) ?? throw new InvalidOperationException($"A alteração {id} não existe.");
        return Undo(entry);
    }

    /// <summary>Undoes every active change: this user's first, newest first; then the service undoes the machine-wide ones.</summary>
    public IReadOnlyList<UndoResult> UndoAll()
    {
        var sid = UserSid;
        return _engine.UndoAll(e => e.Scope == ChangeScope.User && e.UserSid == sid)
            .Concat(_client.Call<List<UndoResult>>(Protocol.Commands.SystemUndoAll))
            .ToList();
    }

    private UndoResult Undo(JournalEntry entry) => entry.Scope == ChangeScope.System
        ? _client.Call<UndoResult>(Protocol.Commands.SystemUndo, new IdArgs(entry.Id))
        : _engine.Undo(entry.Id);

    public void Dispose() => _client.Dispose();
}
