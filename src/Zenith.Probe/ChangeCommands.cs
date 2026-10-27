using System.Diagnostics;
using System.Security.Principal;
using Zenith.Core;
using Zenith.Core.Changes;
using Zenith.Core.Fixes;
using Zenith.Core.Ipc;
using Zenith.Core.Settings;

namespace Zenith.Probe;

/// <summary>
/// Apply and undo, as the user-session agent: user settings are changed here, machine settings by the
/// service, and every change is recorded in the service's journal.
/// </summary>
internal static class ChangeCommands
{
    private static readonly TimeSpan HdrTrial = TimeSpan.FromSeconds(15);

    private static string UserSid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Não deu para identificar o usuário atual.");

    public static int Ping()
    {
        using var client = ServiceClient.Connect();
        var ping = client.Call<PingResult>(Protocol.Commands.Ping);
        Console.WriteLine($"Serviço do Zenith rodando (versão {ping.Version}).");
        Console.WriteLine($"Diário: {ping.JournalPath}");
        if (ping.CallerSid != UserSid)
        {
            Console.WriteLine("O serviço não reconheceu este usuário, então não vai aceitar alterações.");
            Console.WriteLine($"Este usuário: {UserSid}. O serviço viu: {ping.CallerSid ?? "ninguém"}.");
            return 1;
        }
        Console.WriteLine("O serviço reconheceu este usuário.");
        return 0;
    }

    public static int Apply(string[] args)
    {
        using var client = ServiceClient.Connect();
        var applied = args.FirstOrDefault() switch
        {
            "energia" => client.Call<List<JournalEntry>>(Protocol.Commands.PowerFix),
            "taxa" => UserFixes.RefreshRate(AgentEngine(client), MachineSnapshot.Capture(), UserSid),
            "placa" => UserFixes.GpuPreference(AgentEngine(client), MachineSnapshot.Capture(), UserSid),
            "hdr" => ApplyHdrWithTrial(AgentEngine(client)),
            _ => throw new InvalidOperationException("Diga qual achado aplicar: taxa, hdr, energia ou placa."),
        };

        if (applied.Count == 0)
        {
            Console.WriteLine("Nada a mudar: este achado não aparece neste PC agora.");
            return 0;
        }
        foreach (var entry in applied) Print(entry);
        return 0;
    }

    public static int Journal()
    {
        using var client = ServiceClient.Connect();
        var entries = new RemoteJournal(client).List();
        if (entries.Count == 0) Console.WriteLine("O diário está vazio: o Zenith ainda não alterou nada.");
        foreach (var entry in entries) Print(entry);
        return 0;
    }

    public static int Undo(string[] args)
    {
        if (args.Length == 0 || !long.TryParse(args[0], out var id))
            throw new InvalidOperationException("Diga o número da alteração (veja com: zenith-probe diario).");

        using var client = ServiceClient.Connect();
        var entry = new RemoteJournal(client).Get(id) ?? throw new InvalidOperationException($"A alteração {id} não existe.");
        var result = entry.Scope == ChangeScope.System
            ? client.Call<UndoResult>(Protocol.Commands.SystemUndo, new IdArgs(id))
            : AgentEngine(client).Undo(id);
        Console.WriteLine($"#{result.EntryId}: {result.Message}");
        return result.Status == JournalStatus.Undone ? 0 : 1;
    }

    public static int UndoAll()
    {
        using var client = ServiceClient.Connect();
        var sid = UserSid;
        // User changes first, newest first; then the service undoes the machine-wide ones.
        var results = AgentEngine(client).UndoAll(e => e.Scope == ChangeScope.User && e.UserSid == sid)
            .Concat(client.Call<List<UndoResult>>(Protocol.Commands.SystemUndoAll))
            .ToList();

        if (results.Count == 0) Console.WriteLine("Não há alterações ativas para desfazer.");
        foreach (var r in results) Console.WriteLine($"#{r.EntryId}: {r.Message}");
        return results.All(r => r.Status == JournalStatus.Undone) ? 0 : 1;
    }

    /// <summary>
    /// Achado 2: turns HDR on and keeps it only if the user confirms within 15 seconds, so a black or
    /// washed-out screen fixes itself.
    /// </summary>
    private static IReadOnlyList<JournalEntry> ApplyHdrWithTrial(ChangeEngine engine)
    {
        var applied = UserFixes.Hdr(engine, MachineSnapshot.Capture(), UserSid);
        if (applied.Count == 0) return applied;

        Console.WriteLine($"HDR ligado. Se a imagem ficou boa, pressione S nos próximos {HdrTrial.TotalSeconds:0} segundos.");
        Console.WriteLine("Se não fizer nada, o HDR volta a ficar desligado sozinho.");

        if (WaitForYes(HdrTrial))
        {
            Console.WriteLine("Mantido.");
            return applied;
        }

        foreach (var entry in applied)
            Console.WriteLine($"#{entry.Id}: {engine.Undo(entry.Id).Message}");
        return [];
    }

    private static bool WaitForYes(TimeSpan timeout)
    {
        if (Console.IsInputRedirected) return false;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.S) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    private static ChangeEngine AgentEngine(ServiceClient client) =>
        new(new RemoteJournal(client), [new RefreshRateSetting(), new HdrSetting(), new GpuPreferenceSetting()]);

    private static void Print(JournalEntry e) =>
        Console.WriteLine($"#{e.Id} [{StatusLabel(e.Status)}] {e.AppliedAt:dd/MM HH:mm} {e.SettingKey}: {e.PreviousValue} → {e.NewValue}" +
                          (e.Note is null ? "" : $" ({e.Note})"));

    private static string StatusLabel(JournalStatus status) => status switch
    {
        JournalStatus.Applied => "ativa",
        JournalStatus.Undone => "desfeita",
        JournalStatus.Superseded => "mudada depois",
        JournalStatus.UndoFailed => "falhou ao desfazer",
        _ => status.ToString(),
    };
}
