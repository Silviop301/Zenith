using System.Windows.Input;
using System.Windows.Threading;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;

namespace Zenith.App.ViewModels;

/// <summary>One finding card. The action goes back to <see cref="MainViewModel"/>, which owns the backend.</summary>
public sealed class FindingCardViewModel : ObservableObject
{
    private bool _isDetailOpen;
    private bool _isBusy;

    public FindingCardViewModel(FindingCard card, int index, Action<FindingCardViewModel> act)
    {
        Card = card;
        Index = index;
        ActCommand = new RelayCommand(() => act(this));
        ToggleDetailCommand = new RelayCommand(() => IsDetailOpen = !IsDetailOpen);
    }

    public FindingCard Card { get; }
    /// <summary>Position on the screen, for the staggered entrance.</summary>
    public int Index { get; }

    public string Icon => Card.Icon;
    public string Headline => Card.Headline;
    public string Explanation => Card.Explanation;
    public string? From => Card.Change?.From;
    public string? To => Card.Change?.To;
    public bool HasChange => Card.Change is not null;
    public string? Chip => Card.Chip;
    public string? ActionLabel => Card.ActionLabel;
    public bool HasAction => Card.Action != FindingAction.None;
    public bool IsFix => Card.Group == FindingGroup.ApplyNow;
    public bool IsAllGood => Card.Group == FindingGroup.AllGood;
    public string QuietIcon => IsAllGood ? "check" : "info";
    public string Detail => Card.Detail;
    public string SourceLabel => string.Format(Strings.SourceLabel, Card.Source);

    public bool IsDetailOpen { get => _isDetailOpen; set => Set(ref _isDetailOpen, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    public ICommand ActCommand { get; }
    public ICommand ToggleDetailCommand { get; }
}

public sealed class JournalLineViewModel : ObservableObject
{
    public JournalLineViewModel(JournalLine line, int index, Action<JournalLineViewModel> undo)
    {
        Line = line;
        Index = index;
        UndoCommand = new RelayCommand(() => undo(this));
    }

    public JournalLine Line { get; }
    public int Index { get; }
    public string Icon => Line.Icon;
    public string What => Line.What;
    public string From => Line.From;
    public string To => Line.To;
    public string When => Line.When;
    public string StatusLabel => Line.StatusLabel;
    public bool IsActive => Line.CanUndo;
    public string? Note => Line.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Line.Note);
    public ICommand UndoCommand { get; }
}

/// <summary>The 15-second HDR test: keep only if the user confirms; on timeout it reverts.</summary>
public sealed class HdrTrialViewModel : ObservableObject
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);

    private readonly TaskCompletionSource<bool> _result = new();
    private readonly DispatcherTimer _timer;
    private readonly DateTime _deadline;
    private int _secondsLeft;

    public HdrTrialViewModel(bool startTimer = true)
    {
        _deadline = DateTime.UtcNow + Duration;
        _secondsLeft = (int)Duration.TotalSeconds;
        KeepCommand = new RelayCommand(() => Finish(true));
        RevertCommand = new RelayCommand(() => Finish(false));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Tick();
        if (startTimer) _timer.Start();
    }

    public Task<bool> Result => _result.Task;
    public int SecondsLeft { get => _secondsLeft; private set { if (Set(ref _secondsLeft, value)) Raise(nameof(CountdownText)); } }
    public string CountdownText => string.Format(Strings.HdrTrialCountdown, SecondsLeft);
    /// <summary>1 at the start, 0 at the deadline; drives the countdown bar.</summary>
    public double Remaining => Math.Clamp((_deadline - DateTime.UtcNow) / Duration, 0, 1);

    public ICommand KeepCommand { get; }
    public ICommand RevertCommand { get; }

    private void Tick()
    {
        var left = _deadline - DateTime.UtcNow;
        SecondsLeft = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
        Raise(nameof(Remaining));
        if (left <= TimeSpan.Zero) Finish(false);
    }

    private void Finish(bool keep)
    {
        _timer.Stop();
        _result.TrySetResult(keep);
    }
}

public sealed class GuideViewModel(string title, string intro, IReadOnlyList<string> steps, string? safety, Action close) : ObservableObject
{
    public string Title { get; } = title;
    public string Intro { get; } = intro;
    public IReadOnlyList<GuideStep> Steps { get; } = steps.Select((s, i) => new GuideStep(i + 1, s)).ToList();
    public string? Safety { get; } = safety;
    public bool HasSafety => Safety is not null;
    public ICommand CloseCommand { get; } = new RelayCommand(close);

    public static GuideViewModel For(GuideKind kind, Action close) => kind switch
    {
        GuideKind.Cable => new(Strings.GuideCableTitle, Strings.GuideCableIntro,
            [Strings.GuideCableStep1, Strings.GuideCableStep2, Strings.GuideCableStep3, Strings.GuideCableStep4], null, close),
        GuideKind.Bios => new(Strings.GuideBiosTitle, Strings.GuideBiosIntro,
            [Strings.GuideBiosStep1, Strings.GuideBiosStep2, Strings.GuideBiosStep3, Strings.GuideBiosStep4], Strings.GuideBiosSafety, close),
        _ => new(Strings.GuidePowerTitle, Strings.GuidePowerIntro,
            [Strings.GuidePowerStep1, Strings.GuidePowerStep2, Strings.GuidePowerStep3, Strings.GuidePowerStep4], null, close),
    };
}

public sealed record GuideStep(int Number, string Text);
