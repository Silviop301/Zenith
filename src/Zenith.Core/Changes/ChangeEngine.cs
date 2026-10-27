namespace Zenith.Core.Changes;

public sealed record UndoResult(long EntryId, JournalStatus Status, string Message);

/// <summary>
/// Applies and undoes settings through the journal. Every write is verified by reading the setting back,
/// and undo never overwrites a value someone else set after Zenith.
/// </summary>
public sealed class ChangeEngine(IChangeJournal journal, IEnumerable<ISetting> settings)
{
    private readonly Dictionary<string, ISetting> _settings = settings.ToDictionary(s => s.Key);

    /// <summary>Writes <paramref name="newValue"/> and records the change. Returns null when the setting already had that value.</summary>
    public JournalEntry? Apply(string findingId, string settingKey, string target, string newValue, string? userSid = null)
    {
        var setting = Setting(settingKey);
        var previous = setting.Read(target);
        if (previous == newValue) return null;

        setting.Write(target, newValue);
        var actual = setting.Read(target);
        if (actual != newValue)
        {
            // Best effort to leave things as they were before reporting the failure.
            TryWrite(setting, target, previous);
            throw new InvalidOperationException(
                $"O Windows não aceitou a alteração de {settingKey}: pedimos \"{newValue}\" e ficou \"{actual}\".");
        }

        long id;
        try
        {
            id = journal.Record(new NewChange(findingId, settingKey, setting.Scope, userSid, target, previous, newValue));
        }
        catch
        {
            // A change that is not in the journal could not be undone later, so it must not stay.
            TryWrite(setting, target, previous);
            throw;
        }
        return journal.Get(id);
    }

    public UndoResult Undo(long entryId)
    {
        var entry = journal.Get(entryId) ?? throw new InvalidOperationException($"A alteração {entryId} não existe.");
        if (entry.Status != JournalStatus.Applied)
            return new UndoResult(entryId, entry.Status, "Esta alteração já tinha sido desfeita.");

        var newest = journal.NewestActive(entry.SettingKey, entry.Target, entry.UserSid);
        if (newest is not null && newest.Id != entry.Id)
            return new UndoResult(entryId, JournalStatus.Applied,
                $"Desfaça antes a alteração {newest.Id}, que mudou a mesma configuração depois desta.");

        var setting = Setting(entry.SettingKey);
        string current;
        try
        {
            current = setting.Read(entry.Target);
        }
        catch (Exception ex)
        {
            return Close(entry, JournalStatus.UndoFailed, $"Não deu para ler a configuração: {ex.Message}");
        }

        if (current != entry.NewValue)
            return Close(entry, JournalStatus.Superseded,
                $"A configuração foi mudada depois (agora está \"{current}\"), então o Zenith não mexeu nela.");

        try
        {
            setting.Write(entry.Target, entry.PreviousValue);
            var restored = setting.Read(entry.Target);
            return restored == entry.PreviousValue
                ? Close(entry, JournalStatus.Undone, "Valor anterior restaurado.")
                : Close(entry, JournalStatus.UndoFailed, $"Pedimos \"{entry.PreviousValue}\" e ficou \"{restored}\".");
        }
        catch (Exception ex)
        {
            return Close(entry, JournalStatus.UndoFailed, ex.Message);
        }
    }

    /// <summary>Undoes every active change this engine owns, newest first, so each setting returns to its original value.</summary>
    public IReadOnlyList<UndoResult> UndoAll(Func<JournalEntry, bool>? filter = null) =>
        journal.List(activeOnly: true)
            .Where(e => _settings.ContainsKey(e.SettingKey) && (filter?.Invoke(e) ?? true))
            .Select(e => Undo(e.Id))
            .ToList();

    public bool Owns(string settingKey) => _settings.ContainsKey(settingKey);

    private ISetting Setting(string key) =>
        _settings.TryGetValue(key, out var s) ? s : throw new InvalidOperationException($"Configuração desconhecida: {key}.");

    private UndoResult Close(JournalEntry entry, JournalStatus status, string message)
    {
        journal.Close(entry.Id, status, message);
        return new UndoResult(entry.Id, status, message);
    }

    private static void TryWrite(ISetting setting, string target, string value)
    {
        try
        {
            setting.Write(target, value);
        }
        catch (Exception)
        {
            // The caller is already reporting a failure; this was only a cleanup attempt.
        }
    }
}
