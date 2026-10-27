using Zenith.Core.Bestiary;
using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Sensors;

namespace Zenith.App.Backend;

public enum ServiceState { Checking, Ready, NotRunning, WrongUser }

public sealed record ScanOutcome(DateTimeOffset ScannedAt, IReadOnlyList<FindingResult> Findings);

/// <summary>
/// Everything the screens need from the PC. <see cref="LiveBackend"/> reads and changes the real PC;
/// <see cref="DemoBackend"/> serves fixed data for screenshots and design reviews.
/// All calls may block, so the screens run them off the UI thread.
/// </summary>
public interface IBackend
{
    string UserSid { get; }
    ServiceState CheckService();
    ScanOutcome Scan();
    IReadOnlyList<JournalEntry> Apply(string findingId);
    IReadOnlyList<UndoResult> Revert(IReadOnlyList<JournalEntry> applied);
    IReadOnlyList<JournalEntry> Journal();
    UndoResult Undo(long id);
    IReadOnlyList<UndoResult> UndoAll();
    /// <summary>The newest reading of each sensor the service collects (for the tray's critical alerts).</summary>
    IReadOnlyList<SensorSample> LatestSensors();
    /// <summary>Bestiário: who lives in the PC. Reads only; works without the service.</summary>
    BestiaryScan Bestiary();
    /// <summary>Stops one startup entry from starting with Windows, through the journal. Null when it was already off.</summary>
    JournalEntry? DisableStartup(string target);
}
