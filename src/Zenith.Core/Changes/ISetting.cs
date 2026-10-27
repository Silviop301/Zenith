namespace Zenith.Core.Changes;

/// <summary>
/// One setting Zenith knows how to read and write. Values are opaque strings so the journal can store
/// any setting the same way; each implementation defines its own format.
/// </summary>
public interface ISetting
{
    /// <summary>Stable key stored in the journal, e.g. "power.active-scheme".</summary>
    string Key { get; }

    ChangeScope Scope { get; }

    string Read(string target);

    void Write(string target, string value);
}
