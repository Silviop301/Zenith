using Zenith.Core.Changes;
using Zenith.Core.Findings;

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
}
