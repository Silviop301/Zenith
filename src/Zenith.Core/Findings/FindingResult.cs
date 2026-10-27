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
    string? Error = null,
    FindingChange? Change = null);

/// <summary>
/// What a fix would change, in user-facing words (e.g. "60 Hz" → "165 Hz"). Set only on detected
/// findings, and only for what Zenith would actually change or guide the user to change.
/// </summary>
public sealed record FindingChange(string From, string To);

/// <summary>Stable ids of the Resgate findings; they are also stored in the change journal.</summary>
public static class FindingIds
{
    public const string RefreshRate = "display-refresh-rate";
    public const string Hdr = "hdr-off";
    public const string PowerPlan = "power-plan";
    public const string GameOnIntegratedGpu = "game-on-integrated-gpu";
    public const string MonitorOnMotherboard = "monitor-on-motherboard";
    public const string MemorySpeed = "memory-below-rated-speed";
}

public interface IFindingReader
{
    string Id { get; }
    string Title { get; }
    FindingResult Read(MachineSnapshot machine);
}
