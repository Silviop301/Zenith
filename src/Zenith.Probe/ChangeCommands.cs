using System.Diagnostics;
using Zenith.Core;
using Zenith.Core.Agent;
using Zenith.Core.Changes;
using Zenith.Core.Findings;

namespace Zenith.Probe;

/// <summary>
/// Apply and undo, as the user-session agent: user settings are changed here, machine settings by the
/// service, and every change is recorded in the service's journal.
/// </summary>
internal static class ChangeCommands
{
    private static readonly TimeSpan HdrTrial = TimeSpan.FromSeconds(15);

    public static int Ping()
    {
        using var agent = SessionAgent.Connect();
        var ping = agent.Ping();
        Console.WriteLine($"Serviço do Zenith rodando (versão {ping.Version}).");
        Console.WriteLine($"Diário: {ping.JournalPath}");
        if (ping.CallerSid != SessionAgent.UserSid)
        {
            Console.WriteLine("O serviço não reconheceu este usuário, então não vai aceitar alterações.");
            Console.WriteLine($"Este usuário: {SessionAgent.UserSid}. O serviço viu: {ping.CallerSid ?? "ninguém"}.");
            return 1;
        }
        Console.WriteLine("O serviço reconheceu este usuário.");
        return 0;
    }

    public static int Apply(string[] args)
    {
        var findingId = args.FirstOrDefault() switch
        {
            "taxa" => FindingIds.RefreshRate,
            "hdr" => FindingIds.Hdr,
            "energia" => FindingIds.PowerPlan,
            "placa" => FindingIds.GameOnIntegratedGpu,
            _ => throw new InvalidOperationException("Diga qual achado aplicar: taxa, hdr, energia ou placa."),
        };

        using var agent = SessionAgent.Connect();
        if (findingId == FindingIds.Hdr) return ApplyHdrWithTrial(agent);

        var applied = agent.Apply(findingId, MachineSnapshot.Capture());
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
        using var agent = SessionAgent.Connect();
        var entries = agent.Journal();
        if (entries.Count == 0) Console.WriteLine("O diário está vazio: o Zenith ainda não alterou nada.");
        foreach (var entry in entries) Print(entry);
        return 0;
    }

    public static int Sensors()
    {
        using var agent = SessionAgent.Connect();
        var samples = agent.LatestSensors();
        if (samples.Count == 0)
        {
            Console.WriteLine("Ainda não há leituras. O serviço lê os sensores logo ao iniciar e depois a cada 5 minutos.");
            return 0;
        }
        foreach (var s in samples)
            Console.WriteLine($"{s.At:dd/MM HH:mm}  {s.Sensor,-30} {s.Value,8:0.#} {Unit(s.Sensor),-3} {s.Target}");
        return 0;
    }

    private static string Unit(string sensor) => sensor switch
    {
        _ when sensor.EndsWith(".temperature") => "°C",
        _ when sensor.EndsWith(".usage") || sensor is "disk.wear" or "battery.health" => "%",
        "disk.power-on-hours" => "h",
        _ => "",
    };

    public static int Undo(string[] args)
    {
        if (args.Length == 0 || !long.TryParse(args[0], out var id))
            throw new InvalidOperationException("Diga o número da alteração (veja com: zenith-probe diario).");

        using var agent = SessionAgent.Connect();
        var result = agent.Undo(id);
        Console.WriteLine($"#{result.EntryId}: {result.Message}");
        return result.Status == JournalStatus.Undone ? 0 : 1;
    }

    public static int UndoAll()
    {
        using var agent = SessionAgent.Connect();
        var results = agent.UndoAll();
        if (results.Count == 0) Console.WriteLine("Não há alterações ativas para desfazer.");
        foreach (var r in results) Console.WriteLine($"#{r.EntryId}: {r.Message}");
        return results.All(r => r.Status == JournalStatus.Undone) ? 0 : 1;
    }

    /// <summary>
    /// Achado 2: turns HDR on and keeps it only if the user confirms within 15 seconds, so a black or
    /// washed-out screen fixes itself.
    /// </summary>
    private static int ApplyHdrWithTrial(SessionAgent agent)
    {
        var applied = agent.Apply(FindingIds.Hdr, MachineSnapshot.Capture());
        if (applied.Count == 0)
        {
            Console.WriteLine("Nada a mudar: nenhum monitor com HDR desligado.");
            return 0;
        }

        Console.WriteLine($"HDR ligado. Se a imagem ficou boa, pressione S nos próximos {HdrTrial.TotalSeconds:0} segundos.");
        Console.WriteLine("Se não fizer nada, o HDR volta a ficar desligado sozinho.");

        if (WaitForYes(HdrTrial))
        {
            Console.WriteLine("Mantido.");
            foreach (var entry in applied) Print(entry);
            return 0;
        }

        Console.WriteLine("Sem confirmação: o HDR voltou a ficar desligado.");
        var results = agent.Revert(applied);
        foreach (var r in results) Console.WriteLine($"#{r.EntryId}: {r.Message}");
        return results.All(r => r.Status == JournalStatus.Undone) ? 0 : 1;
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
