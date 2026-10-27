using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.Core.Changes;
using Zenith.Core.Settings;

namespace Zenith.Core.Ipc;

/// <summary>
/// The service side of the protocol. Kept free of pipe and Windows details so it can be tested anywhere:
/// the caller's identity and the system fixes are passed in.
/// </summary>
public sealed class CommandHandler(
    IChangeJournal journal,
    ChangeEngine systemEngine,
    Func<IReadOnlyList<JournalEntry>> powerFix,
    string journalPath)
{
    /// <summary>User-scope settings the agent may record. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> UserSettingKeys = new HashSet<string>
    {
        RefreshRateSetting.SettingKey,
        HdrSetting.SettingKey,
        GpuPreferenceSetting.SettingKey,
    };

    public Response Handle(Request request, string? callerSid)
    {
        try
        {
            return new Response(request.Id, true, Dispatch(request, callerSid));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException or UnauthorizedAccessException)
        {
            return new Response(request.Id, false, Error: ex.Message);
        }
    }

    private JsonNode? Dispatch(Request request, string? callerSid) => request.Command switch
    {
        Protocol.Commands.Ping => Node(new PingResult(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?", journalPath, callerSid)),
        Protocol.Commands.JournalList => Node(journal.List(Args<ListArgs>(request)?.ActiveOnly ?? false)),
        Protocol.Commands.JournalGet => Node(journal.Get(Required<IdArgs>(request).Id)),
        Protocol.Commands.JournalNewestActive => NewestActive(Required<NewestActiveArgs>(request)),
        Protocol.Commands.PowerFix => Node(powerFix()),
        Protocol.Commands.SystemUndo => Node(SystemUndo(Required<IdArgs>(request).Id)),
        Protocol.Commands.SystemUndoAll => Node(systemEngine.UndoAll(e => e.Scope == ChangeScope.System)),
        Protocol.Commands.UserRecord => Node(UserRecord(Required<UserRecordArgs>(request), RequireSid(callerSid))),
        Protocol.Commands.UserClose => UserClose(Required<UserCloseArgs>(request), RequireSid(callerSid)),
        _ => throw new ArgumentException($"Comando desconhecido: {request.Command}."),
    };

    private JsonNode? NewestActive(NewestActiveArgs args) =>
        Node(journal.NewestActive(args.SettingKey, args.Target, args.UserSid));

    private UndoResult SystemUndo(long id)
    {
        var entry = journal.Get(id) ?? throw new InvalidOperationException($"A alteração {id} não existe.");
        if (entry.Scope != ChangeScope.System)
            throw new InvalidOperationException("Esta alteração é do usuário e é desfeita pelo app, não pelo serviço.");
        return systemEngine.Undo(id);
    }

    private long UserRecord(UserRecordArgs args, string callerSid)
    {
        if (!UserSettingKeys.Contains(args.SettingKey))
            throw new ArgumentException($"Configuração de usuário desconhecida: {args.SettingKey}.");
        // The identity comes from the pipe, never from the message.
        return journal.Record(new NewChange(args.FindingId, args.SettingKey, ChangeScope.User, callerSid, args.Target,
            args.PreviousValue, args.NewValue));
    }

    private JsonNode? UserClose(UserCloseArgs args, string callerSid)
    {
        var entry = journal.Get(args.Id) ?? throw new InvalidOperationException($"A alteração {args.Id} não existe.");
        if (entry.Scope != ChangeScope.User || entry.UserSid != callerSid)
            throw new UnauthorizedAccessException("Só quem fez a alteração pode marcá-la como desfeita.");
        journal.Close(args.Id, Enum.Parse<JournalStatus>(args.Status.ToString()), args.Note);
        return null;
    }

    private static string RequireSid(string? sid) =>
        sid ?? throw new UnauthorizedAccessException("Não deu para identificar quem está chamando o serviço.");

    private static T? Args<T>(Request request) where T : class => request.Args?.Deserialize<T>(Protocol.Json);

    private static T Required<T>(Request request) where T : class =>
        Args<T>(request) ?? throw new ArgumentException($"O comando {request.Command} precisa de argumentos.");

    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, Protocol.Json);
}
