using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.Core.Changes;

namespace Zenith.Core.Ipc;

public sealed class ServiceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Talks to the Zenith Windows service. One connection, one request at a time.</summary>
public sealed class ServiceClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private long _nextId;

    private ServiceClient(NamedPipeClientStream pipe) => _pipe = pipe;

    public static ServiceClient Connect(TimeSpan? timeout = null)
    {
        // The service identifies us from our process token; it never needs to act as us.
        var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Identification);
        try
        {
            pipe.Connect((int)(timeout ?? TimeSpan.FromSeconds(3)).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            throw new ServiceUnavailableException("O serviço do Zenith não está rodando. Instale ou inicie o serviço e tente de novo.", ex);
        }
        return new ServiceClient(pipe);
    }

    public T Call<T>(string command, object? args = null)
    {
        var node = Call(command, args);
        return node is null ? default! : node.Deserialize<T>(Protocol.Json)!;
    }

    public JsonNode? Call(string command, object? args = null)
    {
        var request = new Request(++_nextId, command, args is null ? null : JsonSerializer.SerializeToNode(args, Protocol.Json)!.AsObject());
        Protocol.WriteAsync(_pipe, request).GetAwaiter().GetResult();
        var response = Protocol.ReadAsync<Response>(_pipe).GetAwaiter().GetResult()
            ?? throw new ServiceUnavailableException("O serviço do Zenith fechou a conexão.");
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Erro desconhecido no serviço.");
        return response.Result;
    }

    public void Dispose() => _pipe.Dispose();
}

/// <summary>The service's journal, seen from the user-session agent. The service stamps the caller's identity on user entries.</summary>
public sealed class RemoteJournal(ServiceClient client) : IChangeJournal
{
    public long Record(NewChange change)
    {
        if (change.Scope != ChangeScope.User) throw new InvalidOperationException("O agente só registra alterações do usuário.");
        return client.Call<long>(Protocol.Commands.UserRecord,
            new UserRecordArgs(change.FindingId, change.SettingKey, change.Target, change.PreviousValue, change.NewValue));
    }

    public void Close(long id, JournalStatus status, string? note = null) =>
        client.Call(Protocol.Commands.UserClose, new UserCloseArgs(id, Enum.Parse<JournalStatusArg>(status.ToString()), note));

    public JournalEntry? Get(long id) => client.Call<JournalEntry?>(Protocol.Commands.JournalGet, new IdArgs(id));

    public IReadOnlyList<JournalEntry> List(bool activeOnly = false) =>
        client.Call<List<JournalEntry>>(Protocol.Commands.JournalList, new ListArgs(activeOnly));

    public JournalEntry? NewestActive(string settingKey, string target, string? userSid) =>
        client.Call<JournalEntry?>(Protocol.Commands.JournalNewestActive, new NewestActiveArgs(settingKey, target, userSid));
}
