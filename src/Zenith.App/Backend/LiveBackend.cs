using Zenith.Core;
using Zenith.Core.Agent;
using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Ipc;

namespace Zenith.App.Backend;

/// <summary>The real PC: readers run in this process, changes go through the Zenith service.</summary>
public sealed class LiveBackend : IBackend
{
    public string UserSid { get; } = SessionAgent.UserSid;

    public ServiceState CheckService()
    {
        try
        {
            using var agent = SessionAgent.Connect(TimeSpan.FromSeconds(1));
            return agent.Ping().CallerSid == UserSid ? ServiceState.Ready : ServiceState.WrongUser;
        }
        catch (ServiceUnavailableException)
        {
            return ServiceState.NotRunning;
        }
    }

    public ScanOutcome Scan()
    {
        var report = Scanner.Run();
        return new ScanOutcome(report.ScannedAt, report.Findings);
    }

    public IReadOnlyList<JournalEntry> Apply(string findingId)
    {
        using var agent = SessionAgent.Connect();
        return agent.Apply(findingId, MachineSnapshot.Capture());
    }

    public IReadOnlyList<UndoResult> Revert(IReadOnlyList<JournalEntry> applied)
    {
        using var agent = SessionAgent.Connect();
        return agent.Revert(applied);
    }

    public IReadOnlyList<JournalEntry> Journal()
    {
        using var agent = SessionAgent.Connect();
        return agent.Journal();
    }

    public UndoResult Undo(long id)
    {
        using var agent = SessionAgent.Connect();
        return agent.Undo(id);
    }

    public IReadOnlyList<UndoResult> UndoAll()
    {
        using var agent = SessionAgent.Connect();
        return agent.UndoAll();
    }
}
