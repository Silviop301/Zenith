namespace Zenith.Core.Findings;

public enum FindingStatus
{
    /// <summary>The problem is present on this PC.</summary>
    Detected,
    /// <summary>The check ran and the problem is not present.</summary>
    NotDetected,
    /// <summary>The check ran but the evidence is not enough to say either way.</summary>
    Inconclusive,
    /// <summary>The check does not apply to this hardware (e.g. no integrated GPU).</summary>
    NotApplicable,
    /// <summary>The check could not run; see <see cref="FindingResult.Error"/>.</summary>
    Error,
}

/// <summary>
/// Outcome of one read-only check. <see cref="Title"/> and <see cref="Summary"/> are user-facing
/// (Portuguese); <see cref="Evidence"/> holds the raw values the conclusion came from, so every
/// finding can show where it came from.
/// </summary>
public sealed record FindingResult(
    string Id,
    string Title,
    FindingStatus Status,
    string Summary,
    string Source,
    IReadOnlyDictionary<string, object?> Evidence,
    string? Error = null);

public interface IFindingReader
{
    string Id { get; }
    string Title { get; }
    FindingResult Read(MachineSnapshot machine);
}
