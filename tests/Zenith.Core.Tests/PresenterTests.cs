using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Presentation;

namespace Zenith.Core.Tests;

public class FindingPresenterTests
{
    private static FindingResult Result(string id, FindingStatus status, FindingChange? change = null, Dictionary<string, object?>? evidence = null) =>
        new(id, $"Título {id}", status, $"Resumo {id}", $"Fonte {id}", evidence ?? [], Change: change);

    [Fact]
    public void Refresh_rate_is_a_one_click_fix_with_the_values()
    {
        var card = FindingPresenter.Card(Result(FindingIds.RefreshRate, FindingStatus.Detected, new("60 Hz", "165 Hz")))!;
        Assert.Equal(FindingGroup.ApplyNow, card.Group);
        Assert.Equal(FindingAction.Apply, card.Action);
        Assert.Equal("Seu monitor aceita 165 Hz, mas está em 60 Hz", card.Headline);
        Assert.Equal("60 Hz", card.Change!.From);
    }

    [Fact]
    public void Hdr_gets_the_trial()
    {
        var card = FindingPresenter.Card(Result(FindingIds.Hdr, FindingStatus.Detected, new("Desligado", "Ligado")))!;
        Assert.Equal(FindingAction.Trial, card.Action);
        Assert.Equal("Testar", card.ActionLabel);
    }

    [Fact]
    public void Power_saver_plan_explains_the_switch_to_balanced()
    {
        var card = FindingPresenter.Card(Result(FindingIds.PowerPlan, FindingStatus.Detected,
            new("Economia de energia", "Equilibrado"), new() { ["schemeKind"] = "power-saver" }))!;
        Assert.Equal(FindingGroup.ApplyNow, card.Group);
        Assert.Contains("Equilibrado", card.Explanation);
    }

    [Fact]
    public void Processor_cap_explains_the_cap()
    {
        var card = FindingPresenter.Card(Result(FindingIds.PowerPlan, FindingStatus.Detected,
            new("70%", "100%"), new() { ["schemeKind"] = "balanced" }))!;
        Assert.Contains("70%", card.Explanation);
    }

    [Fact]
    public void Power_mode_alone_is_a_guide_not_a_fix()
    {
        // PowerPlanFix does not change the power mode overlay, so there must be no "Ativar" button.
        var card = FindingPresenter.Card(Result(FindingIds.PowerPlan, FindingStatus.Detected))!;
        Assert.Equal(FindingGroup.NeedsYou, card.Group);
        Assert.Equal(GuideKind.PowerMode, card.Guide);
    }

    [Theory]
    [InlineData(FindingIds.MonitorOnMotherboard, GuideKind.Cable)]
    [InlineData(FindingIds.MemorySpeed, GuideKind.Bios)]
    public void Cable_and_memory_are_guides(string id, GuideKind guide)
    {
        var card = FindingPresenter.Card(Result(id, FindingStatus.Detected, new("a", "b")))!;
        Assert.Equal(FindingGroup.NeedsYou, card.Group);
        Assert.Equal(FindingAction.Guide, card.Action);
        Assert.Equal(guide, card.Guide);
    }

    [Fact]
    public void Not_applicable_findings_are_hidden() =>
        Assert.Null(FindingPresenter.Card(Result(FindingIds.GameOnIntegratedGpu, FindingStatus.NotApplicable)));

    [Theory]
    [InlineData(FindingStatus.NotDetected, FindingGroup.AllGood)]
    [InlineData(FindingStatus.Inconclusive, FindingGroup.CouldNotCheck)]
    [InlineData(FindingStatus.Error, FindingGroup.CouldNotCheck)]
    public void Quiet_cards_keep_the_reader_summary_and_source(FindingStatus status, FindingGroup group)
    {
        var card = FindingPresenter.Card(Result(FindingIds.MemorySpeed, status))!;
        Assert.Equal(group, card.Group);
        Assert.Equal(FindingAction.None, card.Action);
        Assert.Equal($"Resumo {FindingIds.MemorySpeed}", card.Explanation);
        Assert.Equal($"Fonte {FindingIds.MemorySpeed}", card.Source);
    }

    [Fact]
    public void Every_card_shows_where_it_came_from()
    {
        var cards = FindingPresenter.Cards(
        [
            Result(FindingIds.RefreshRate, FindingStatus.Detected, new("60 Hz", "144 Hz")),
            Result(FindingIds.MemorySpeed, FindingStatus.Detected, new("2133 MHz", "3200 MHz")),
        ]);
        Assert.All(cards, c => Assert.False(string.IsNullOrWhiteSpace(c.Source)));
        Assert.All(cards, c => Assert.False(string.IsNullOrWhiteSpace(c.Detail)));
    }

    [Fact]
    public void Overview_counts_and_plurals()
    {
        var cards = FindingPresenter.Cards(
        [
            Result(FindingIds.RefreshRate, FindingStatus.Detected, new("60 Hz", "144 Hz")),
            Result(FindingIds.Hdr, FindingStatus.Detected, new("Desligado", "Ligado")),
            Result(FindingIds.PowerPlan, FindingStatus.Detected, new("70%", "100%")),
            Result(FindingIds.MonitorOnMotherboard, FindingStatus.Detected, new("Placa-mãe", "Placa de vídeo")),
            Result(FindingIds.MemorySpeed, FindingStatus.Detected, new("2133 MHz", "3200 MHz")),
            Result(FindingIds.GameOnIntegratedGpu, FindingStatus.NotApplicable),
        ]);
        var now = new DateTimeOffset(2026, 10, 27, 14, 32, 0, TimeSpan.Zero);
        var o = FindingPresenter.Overview(cards, now, now);

        Assert.Equal("VARREDURA CONCLUÍDA · HOJE, 14:32", o.Eyebrow);
        Assert.Equal(5, o.Found);
        Assert.Equal("3 de 5", o.ApplyNowValue);
        Assert.Equal("2 precisam de você", o.NeedsYouTitle);
        Assert.Equal("Resgatar os 3 seguros", o.ApplyAllLabel);
        Assert.Equal("Encontramos 5 recursos do seu hardware desligados ou limitados. Dá para ativar 3 agora, todos com desfazer.", o.Lead);
    }

    [Fact]
    public void Overview_with_nothing_found_has_no_apply_button()
    {
        var cards = FindingPresenter.Cards(
        [
            Result(FindingIds.RefreshRate, FindingStatus.NotDetected),
            Result(FindingIds.MemorySpeed, FindingStatus.Inconclusive),
        ]);
        var scanned = new DateTimeOffset(2026, 10, 26, 9, 5, 0, TimeSpan.Zero);
        var o = FindingPresenter.Overview(cards, scanned, scanned.AddDays(1));

        Assert.Null(o.ApplyAllLabel);
        Assert.Equal("Seu PC está usando o que tem.", o.Headline);
        Assert.StartsWith("VARREDURA CONCLUÍDA · 26/10", o.Eyebrow);
        Assert.Contains("Em 1 ponto não deu para ter certeza", o.Lead);
    }

    [Fact]
    public void No_vague_speed_promises_in_any_text()
    {
        // Regra 6: nenhum texto do tipo "deixe seu PC mais rápido".
        var set = Resources.Strings.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)!;
        foreach (System.Collections.DictionaryEntry entry in set)
        {
            var text = ((string)entry.Value!).ToLowerInvariant();
            Assert.DoesNotContain("mais rápido", text);
            Assert.DoesNotContain("acelera", text);
            Assert.DoesNotContain("turbin", text);
        }
    }
}

public class JournalPresenterTests
{
    private const string Me = "S-1-5-21-1";

    private static JournalEntry Entry(long id, string key, string prev, string next, ChangeScope scope = ChangeScope.User,
        string? sid = Me, string target = "t", JournalStatus status = JournalStatus.Applied) =>
        new(id, "f", key, scope, scope == ChangeScope.User ? sid : null, target, prev, next,
            new DateTimeOffset(2026, 10, 27, 14, 32, 0, TimeSpan.Zero), status, null, null);

    [Fact]
    public void Values_in_plain_words()
    {
        var lines = JournalPresenter.Lines(
        [
            Entry(1, "display.refresh-rate", "59", "165"),
            Entry(2, "display.hdr", "off", "on"),
            Entry(3, "power.active-scheme", "a1841308-3541-4fab-bc81-f71556f20b4a", "381b4222-f694-41f0-9685-ff5bb260df2e", ChangeScope.System),
            Entry(4, "gpu.app-preference", "unset", "2", target: @"C:\Jogos\jogo.exe"),
        ], Me);

        Assert.Equal([4L, 3, 2, 1], lines.Select(l => l.Id));
        var byId = lines.ToDictionary(l => l.Id);
        Assert.Equal(("59 Hz", "165 Hz"), (byId[1].From, byId[1].To));
        Assert.Equal(("Desligado", "Ligado"), (byId[2].From, byId[2].To));
        Assert.Equal(("Economia de energia", "Equilibrado"), (byId[3].From, byId[3].To));
        Assert.Equal("Placa de vídeo do jogo.exe", byId[4].What);
        Assert.Equal(("Padrão do Windows", "Alto desempenho"), (byId[4].From, byId[4].To));
    }

    [Fact]
    public void Other_users_changes_stay_out()
    {
        var lines = JournalPresenter.Lines(
        [
            Entry(1, "display.refresh-rate", "60", "144", sid: "S-1-5-21-2"),
            Entry(2, "power.active-scheme", "a", "b", ChangeScope.System),
        ], Me);
        Assert.Equal([2L], lines.Select(l => l.Id));
    }

    [Fact]
    public void Only_active_changes_can_be_undone()
    {
        var lines = JournalPresenter.Lines(
        [
            Entry(1, "display.hdr", "off", "on", status: JournalStatus.Undone),
            Entry(2, "display.hdr", "off", "on"),
        ], Me);
        Assert.False(lines.Single(l => l.Id == 1).CanUndo);
        Assert.Equal("Desfeita", lines.Single(l => l.Id == 1).StatusLabel);
        Assert.True(lines.Single(l => l.Id == 2).CanUndo);
    }
}
