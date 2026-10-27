using System.Globalization;
using Zenith.Core.Findings;
using Zenith.Core.Resources;

namespace Zenith.Core.Presentation;

public enum FindingGroup
{
    /// <summary>Detected, and Zenith can fix it in one click.</summary>
    ApplyNow,
    /// <summary>Detected, and the user has to do it (cable, BIOS, a Windows setting), with a guide.</summary>
    NeedsYou,
    /// <summary>Checked, nothing to fix.</summary>
    AllGood,
    /// <summary>The check ran without a clear answer, or failed.</summary>
    CouldNotCheck,
}

public enum FindingAction { None, Apply, Trial, Guide }

public enum GuideKind { None, Cable, Bios, PowerMode }

/// <summary>One finding as the Resgate screen shows it. Every card keeps the reader's summary and source, so it can show where it came from.</summary>
public sealed record FindingCard(
    string FindingId,
    FindingGroup Group,
    string Icon,
    string Headline,
    string Explanation,
    FindingChange? Change,
    string? Chip,
    FindingAction Action,
    string? ActionLabel,
    GuideKind Guide,
    string Detail,
    string Source);

/// <summary>Header and summary of the Resgate screen for one scan.</summary>
public sealed record ResgateOverview(
    string Eyebrow,
    string Headline,
    string Lead,
    int Found,
    int ApplyNow,
    int NeedsYou,
    string? ApplyAllLabel,
    string ApplyNowValue,
    string NeedsYouTitle,
    string NeedsYouDetail);

/// <summary>Turns reader results into the cards and texts of the Resgate screen. Findings that do not apply to this PC are left out.</summary>
public static class FindingPresenter
{
    public static IReadOnlyList<FindingCard> Cards(IEnumerable<FindingResult> results) =>
        results.Select(Card).OfType<FindingCard>().ToList();

    public static FindingCard? Card(FindingResult r) => r.Status switch
    {
        FindingStatus.NotApplicable => null,
        FindingStatus.Detected => Detected(r),
        FindingStatus.NotDetected => Quiet(r, FindingGroup.AllGood),
        _ => Quiet(r, FindingGroup.CouldNotCheck),
    };

    public static ResgateOverview Overview(IReadOnlyList<FindingCard> cards, DateTimeOffset scannedAt, DateTimeOffset now)
    {
        var applyNow = cards.Count(c => c.Group == FindingGroup.ApplyNow);
        var needsYou = cards.Count(c => c.Group == FindingGroup.NeedsYou);
        var unsure = cards.Count(c => c.Group == FindingGroup.CouldNotCheck);
        var found = applyNow + needsYou;

        var when = scannedAt.Date == now.Date ? F(Strings.TimeToday, scannedAt) : F(Strings.TimeOtherDay, scannedAt);
        var lead = new List<string>();
        if (found > 0)
        {
            lead.Add(Plural(found, Strings.LeadFound_One, Strings.LeadFound_Other));
            if (applyNow > 0) lead.Add(Plural(applyNow, Strings.LeadApplyNow_One, Strings.LeadApplyNow_Other));
        }
        else
        {
            lead.Add(Strings.LeadNone);
        }
        if (unsure > 0) lead.Add(Plural(unsure, Strings.LeadCouldNotCheck_One, Strings.LeadCouldNotCheck_Other));

        return new ResgateOverview(
            F(Strings.ScanDoneEyebrow, when),
            found > 0 ? Strings.HeadlineFound : Strings.HeadlineNone,
            string.Join(" ", lead),
            found, applyNow, needsYou,
            applyNow > 0 ? Plural(applyNow, Strings.ButtonApplyAll_One, Strings.ButtonApplyAll_Other) : null,
            F(Strings.SummaryApplyNowValue, applyNow, found),
            needsYou == 0 ? Strings.SummaryNeedsYou_None : Plural(needsYou, Strings.SummaryNeedsYou_One, Strings.SummaryNeedsYou_Other),
            needsYou == 0 ? Strings.SummaryNeedsYouNoneDetail : Strings.SummaryNeedsYouDetail);
    }

    private static FindingCard Detected(FindingResult r)
    {
        var c = r.Change;
        return r.Id switch
        {
            FindingIds.RefreshRate when c is not null => Fix(r, "monitor", F(Strings.RefreshRate_Headline, c.From, c.To), Strings.RefreshRate_Explanation),
            FindingIds.Hdr => Fix(r, "hdr", Strings.Hdr_Headline, Strings.Hdr_Explanation) with
            {
                Chip = Strings.ChipTrial, Action = FindingAction.Trial, ActionLabel = Strings.ActionTrial,
            },
            FindingIds.PowerPlan when c is null =>
                // Only the power mode overlay is limiting: that one stays the user's choice.
                Guide(r, "power", Strings.PowerMode_Headline, Strings.PowerMode_Explanation, Strings.ChipWindowsSetting, Strings.ActionGuide, GuideKind.PowerMode),
            FindingIds.PowerPlan => Fix(r, "power", Strings.PowerPlan_Headline,
                r.Evidence.TryGetValue("schemeKind", out var kind) && kind as string == "power-saver"
                    ? Strings.PowerPlan_ExplanationScheme
                    : F(Strings.PowerPlan_ExplanationCap, c.From)),
            FindingIds.GameOnIntegratedGpu => Fix(r, "gpu", Strings.Gpu_Headline, Strings.Gpu_Explanation),
            FindingIds.MonitorOnMotherboard =>
                Guide(r, "cable", Strings.Cable_Headline, Strings.Cable_Explanation, Strings.ChipCable, Strings.ActionCable, GuideKind.Cable),
            FindingIds.MemorySpeed =>
                Guide(r, "memory", Strings.Memory_Headline, Strings.Memory_Explanation, Strings.ChipBios, Strings.ActionBios, GuideKind.Bios),
            // A finding this screen does not know yet: show what the reader said, without a button.
            _ => Quiet(r, FindingGroup.NeedsYou),
        };
    }

    private static FindingCard Fix(FindingResult r, string icon, string headline, string explanation) =>
        new(r.Id, FindingGroup.ApplyNow, icon, headline, explanation, r.Change, Strings.ChipOneClick,
            FindingAction.Apply, Strings.ActionApply, GuideKind.None, r.Summary, r.Source);

    private static FindingCard Guide(FindingResult r, string icon, string headline, string explanation, string chip, string action, GuideKind guide) =>
        new(r.Id, FindingGroup.NeedsYou, icon, headline, explanation, r.Change, chip, FindingAction.Guide, action, guide, r.Summary, r.Source);

    // Already fine: the reader's summary says it ("Seu monitor já está na taxa máxima"), the problem title would read
    // like an alarm next to a check mark. No clear answer: the title says what was checked, the summary why it is unclear.
    private static FindingCard Quiet(FindingResult r, FindingGroup group) => group == FindingGroup.AllGood
        ? new(r.Id, group, IconOf(r.Id), r.Summary, "", null, null, FindingAction.None, null, GuideKind.None, r.Summary, r.Source)
        : new(r.Id, group, IconOf(r.Id), r.Title, r.Summary, null, null, FindingAction.None, null, GuideKind.None, r.Summary, r.Source);

    private static string IconOf(string findingId) => findingId switch
    {
        FindingIds.RefreshRate => "monitor",
        FindingIds.Hdr => "hdr",
        FindingIds.PowerPlan => "power",
        FindingIds.GameOnIntegratedGpu => "gpu",
        FindingIds.MonitorOnMotherboard => "cable",
        FindingIds.MemorySpeed => "memory",
        _ => "info",
    };

    public static string Plural(int n, string one, string other) => n == 1 ? one : F(other, n);

    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
