using Zenith.Core.Agent;
using Zenith.Core.Bestiary;
using Zenith.Core.Presentation;

namespace Zenith.Probe;

/// <summary>The Bestiário from the command line: who lives in the PC, and "impedir de iniciar" through the journal.</summary>
internal static class BestiaryCommand
{
    /// <summary>Reads only; works without the service.</summary>
    public static int List()
    {
        var scan = BestiaryScanner.Scan();
        var cards = BestiaryPresenter.Cards(scan);
        var overview = BestiaryPresenter.Overview(cards, scan.HiddenProcesses);
        Console.WriteLine($"{cards.Count} habitantes, {overview.CatalogedLabel.ToLowerInvariant()}.");
        if (overview.HiddenNote is { } hidden) Console.WriteLine(hidden);
        Console.WriteLine();

        foreach (var sighting in scan.Sightings.OrderByDescending(s => s.Verdict.Level).ThenBy(s => s.Inhabitant.FileName))
        {
            var (inhabitant, verdict) = sighting;
            var card = BestiaryPresenter.Card(sighting);
            Console.WriteLine($"{card.LevelLabel,-20} {card.Name}  ({inhabitant.FileName}, {card.Cost})");
            Console.WriteLine($"{"",-20} {inhabitant.ExecutablePath}");
            foreach (var reason in card.Reasons) Console.WriteLine($"{"",-20} · {reason}");
            foreach (var entry in inhabitant.Startup)
                Console.WriteLine($"{"",-20} inicialização: {entry.Target} ({(entry.Enabled ? "ligada" : "desligada")})");
        }
        return 0;
    }

    /// <summary>"impedir <alvo>": e.g. zenith-probe impedir "user-run|Discord". The targets are listed by "bestiario".</summary>
    public static int StopStartup(string[] args)
    {
        if (args.Length == 0)
            throw new InvalidOperationException("Diga o que impedir, como aparece em \"zenith-probe bestiario\" (ex.: user-run|Discord).");

        using var agent = SessionAgent.Connect();
        var entry = agent.DisableStartup(args[0]);
        Console.WriteLine(entry is null
            ? "Já não iniciava com o Windows. Nada mudou."
            : $"#{entry.Id}  {entry.Target}: não inicia mais com o Windows. Para desfazer: zenith-probe desfazer {entry.Id}");
        return 0;
    }
}
