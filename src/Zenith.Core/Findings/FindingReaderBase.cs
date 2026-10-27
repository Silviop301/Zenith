namespace Zenith.Core.Findings;

/// <summary>Turns any exception thrown by a reader into an <see cref="FindingStatus.Error"/> result.</summary>
public abstract class FindingReaderBase : IFindingReader
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    protected abstract string Source { get; }

    public FindingResult Read(MachineSnapshot machine)
    {
        try
        {
            return ReadCore(machine);
        }
        catch (Exception ex)
        {
            return new FindingResult(Id, Title, FindingStatus.Error, "Não foi possível fazer esta verificação.",
                Source, new Dictionary<string, object?>(), $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    protected abstract FindingResult ReadCore(MachineSnapshot machine);

    protected FindingResult Result(FindingStatus status, string summary, Dictionary<string, object?> evidence, FindingChange? change = null) =>
        new(Id, Title, status, summary, Source, evidence, Change: change);
}
