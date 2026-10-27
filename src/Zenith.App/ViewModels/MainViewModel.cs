using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Zenith.App.Backend;
using Zenith.Core.Changes;
using Zenith.Core.Findings;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;

namespace Zenith.App.ViewModels;

public enum AppPage { Findings, Journal }

public enum ScanState { Scanning, Ready, Failed }

/// <summary>The whole app: the Resgate findings, the journal (Resgatados), the HDR test and the guides.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IBackend _backend;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private AppPage _page = AppPage.Findings;
    private ServiceState _service = ServiceState.Checking;
    private ScanState _scanState = ScanState.Scanning;
    private ResgateOverview? _overview;
    private string? _scanError;
    private bool _isBusy;
    private int _activeChanges;
    private HdrTrialViewModel? _hdrTrial;
    private GuideViewModel? _guide;
    private string? _toast;
    private IReadOnlyList<TrayAlert> _alerts = [];

    public MainViewModel(IBackend backend)
    {
        _backend = backend;
        ShowFindingsCommand = new RelayCommand(() => Page = AppPage.Findings);
        ShowJournalCommand = new RelayCommand(() => Page = AppPage.Journal);
        RescanCommand = new RelayCommand(() => _ = RescanAsync(showScanning: true), () => !IsBusy && ScanState != ScanState.Scanning);
        ApplyAllCommand = new RelayCommand(() => _ = ApplyAllAsync(), () => CanChange && ScanState == ScanState.Ready);
        UndoAllCommand = new RelayCommand(() => _ = UndoAllAsync(), () => CanChange && ActiveChanges > 0);
        DismissToastCommand = new RelayCommand(() => Toast = null);
        _toastTimer.Tick += (_, _) => Toast = null;
    }

    // Shell

    public AppPage Page
    {
        get => _page;
        set { if (Set(ref _page, value)) { Raise(nameof(IsFindingsPage)); Raise(nameof(IsJournalPage)); } }
    }

    public bool IsFindingsPage => Page == AppPage.Findings;
    public bool IsJournalPage => Page == AppPage.Journal;

    public ServiceState Service
    {
        get => _service;
        private set
        {
            if (!Set(ref _service, value)) return;
            Raise(nameof(ServiceTitle));
            Raise(nameof(ServiceDetail));
            Raise(nameof(IsServiceReady));
            Raise(nameof(ShowServiceBanner));
            Raise(nameof(Tray));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Changes need the service; with it off the buttons stay disabled instead of failing after the click. Guides always open.</summary>
    private bool CanChange => IsServiceReady && !IsBusy;

    /// <summary>The service was checked and cannot take changes: findings still show, buttons explain why nothing happens.</summary>
    public bool ShowServiceBanner => Service is ServiceState.NotRunning or ServiceState.WrongUser;

    public bool IsServiceReady => Service == ServiceState.Ready;

    public string ServiceTitle => Service switch
    {
        ServiceState.Ready => Strings.ServiceOn,
        ServiceState.NotRunning => Strings.ServiceOff,
        ServiceState.WrongUser => Strings.ServiceWrongUser,
        _ => Strings.ServiceChecking,
    };

    public string? ServiceDetail => Service switch
    {
        ServiceState.Ready => Strings.ServiceOnDetail,
        ServiceState.NotRunning => Strings.ServiceOffDetail,
        ServiceState.WrongUser => Strings.ServiceWrongUserDetail,
        _ => null,
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { Raise(nameof(Tray)); CommandManager.InvalidateRequerySuggested(); } }
    }

    public string? Toast
    {
        get => _toast;
        set
        {
            if (!Set(ref _toast, value)) return;
            _toastTimer.Stop();
            if (value is not null) _toastTimer.Start();
        }
    }

    public ICommand ShowFindingsCommand { get; }
    public ICommand ShowJournalCommand { get; }
    public ICommand DismissToastCommand { get; }

    // Findings

    public ScanState ScanState
    {
        get => _scanState;
        private set
        {
            if (!Set(ref _scanState, value)) return;
            Raise(nameof(IsScanning));
            Raise(nameof(IsScanReady));
            Raise(nameof(IsScanFailed));
            Raise(nameof(Tray));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsScanning => ScanState == ScanState.Scanning;
    public bool IsScanReady => ScanState == ScanState.Ready;
    public bool IsScanFailed => ScanState == ScanState.Failed;

    public ResgateOverview? Overview
    {
        get => _overview;
        private set
        {
            Set(ref _overview, value);
            Raise(nameof(HasFindings));
            Raise(nameof(FindingsBadge));
            Raise(nameof(RingFraction));
            Raise(nameof(Tray));
        }
    }

    public bool HasFindings => Overview?.Found > 0;
    public string? FindingsBadge => Overview?.Found > 0 ? Overview.Found.ToString(CultureInfo.CurrentCulture) : null;
    public double RingFraction => Overview is { Found: > 0 } o ? (double)o.ApplyNow / o.Found : 0;

    public string? ScanError { get => _scanError; private set => Set(ref _scanError, value); }

    public ObservableCollection<FindingCardViewModel> ApplyNow { get; } = [];
    public ObservableCollection<FindingCardViewModel> NeedsYou { get; } = [];
    public ObservableCollection<FindingCardViewModel> AllGood { get; } = [];
    public ObservableCollection<FindingCardViewModel> CouldNotCheck { get; } = [];

    public ICommand RescanCommand { get; }
    public ICommand ApplyAllCommand { get; }

    // Journal

    public ObservableCollection<JournalLineViewModel> JournalLines { get; } = [];

    public int ActiveChanges
    {
        get => _activeChanges;
        private set { if (Set(ref _activeChanges, value)) { Raise(nameof(JournalBadge)); CommandManager.InvalidateRequerySuggested(); } }
    }

    public string? JournalBadge => ActiveChanges > 0 ? ActiveChanges.ToString(CultureInfo.CurrentCulture) : null;
    public bool IsJournalEmpty => JournalLines.Count == 0;

    public ICommand UndoAllCommand { get; }

    // Tray

    /// <summary>What the tray icon shows now.</summary>
    public TrayState Tray => TrayPresenter.State(
        Service == ServiceState.Checking ? null : Service == ServiceState.Ready, IsBusy, IsScanning, Overview, Alerts.Count);

    /// <summary>Critical alerts from the service's sensors (a disk with read errors).</summary>
    public IReadOnlyList<TrayAlert> Alerts
    {
        get => _alerts;
        private set { _alerts = value; Raise(nameof(Tray)); }
    }

    public async Task RefreshAlertsAsync()
    {
        if (Service != ServiceState.Ready) return;
        try
        {
            Alerts = TrayPresenter.Alerts(await Task.Run(_backend.LatestSensors));
        }
        catch (Exception)
        {
            // The service went away; the next check shows it.
        }
    }

    /// <summary>A background check while the app sits in the tray: the service, the findings and the alerts, without the scanning screen.</summary>
    public async Task BackgroundCheckAsync()
    {
        if (IsBusy) return;
        Service = await Task.Run(_backend.CheckService);
        await RescanAsync(showScanning: false);
        await RefreshJournalAsync();
        await RefreshAlertsAsync();
    }

    // Overlays

    public HdrTrialViewModel? HdrTrial
    {
        get => _hdrTrial;
        private set { if (Set(ref _hdrTrial, value)) Raise(nameof(HasOverlay)); }
    }

    public GuideViewModel? Guide
    {
        get => _guide;
        private set { if (Set(ref _guide, value)) Raise(nameof(HasOverlay)); }
    }

    public bool HasOverlay => HdrTrial is not null || Guide is not null;

    // Flow

    public async Task StartAsync()
    {
        var service = Task.Run(_backend.CheckService);
        await RescanAsync(showScanning: true);
        Service = await service;
        await RefreshJournalAsync();
        await RefreshAlertsAsync();
    }

    public async Task RescanAsync(bool showScanning)
    {
        if (showScanning) ScanState = ScanState.Scanning;
        try
        {
            var outcome = await Task.Run(_backend.Scan);
            var cards = FindingPresenter.Cards(outcome.Findings);
            Overview = FindingPresenter.Overview(cards, outcome.ScannedAt, DateTimeOffset.Now);

            var index = 0;
            Fill(ApplyNow, cards, FindingGroup.ApplyNow, ref index);
            Fill(NeedsYou, cards, FindingGroup.NeedsYou, ref index);
            Fill(AllGood, cards, FindingGroup.AllGood, ref index);
            Fill(CouldNotCheck, cards, FindingGroup.CouldNotCheck, ref index);
            ScanError = null;
            ScanState = ScanState.Ready;
        }
        catch (Exception ex)
        {
            ScanError = ex.Message;
            ScanState = ScanState.Failed;
        }
    }

    public void OpenGuide(GuideKind kind) => Guide = GuideViewModel.For(kind, () => Guide = null);

    /// <summary>Starts the HDR test without the backend, for screenshots.</summary>
    internal HdrTrialViewModel ShowHdrTrialPreview() => HdrTrial = new HdrTrialViewModel(startTimer: false);

    /// <summary>Applies findings without the busy and toast flow, for screenshots.</summary>
    internal async Task ApplyForScreenshotAsync(IEnumerable<string> findingIds)
    {
        foreach (var id in findingIds) await Task.Run(() => _backend.Apply(id));
        await AfterChangeAsync();
    }

    internal void CloseOverlays()
    {
        HdrTrial = null;
        Guide = null;
    }

    private void Fill(ObservableCollection<FindingCardViewModel> target, IReadOnlyList<FindingCard> cards, FindingGroup group, ref int index)
    {
        target.Clear();
        foreach (var card in cards.Where(c => c.Group == group))
            target.Add(new FindingCardViewModel(card, index++, c => _ = ActAsync(c), c => c.Card.Action == FindingAction.Guide || CanChange));
    }

    private async Task ActAsync(FindingCardViewModel card)
    {
        if (card.Card.Action == FindingAction.Guide)
        {
            OpenGuide(card.Card.Guide);
            return;
        }
        if (IsBusy || !await EnsureServiceAsync()) return;

        IsBusy = true;
        card.IsBusy = true;
        try
        {
            if (card.Card.Action == FindingAction.Trial)
            {
                await RunHdrTrialAsync();
            }
            else
            {
                var applied = await Task.Run(() => _backend.Apply(card.Card.FindingId));
                Toast = applied.Count == 0 ? Strings.MsgNothingToChange : AppliedMessage(applied.Count);
            }
        }
        catch (Exception ex)
        {
            Toast = string.Format(Strings.MsgError, ex.Message);
        }
        finally
        {
            card.IsBusy = false;
            IsBusy = false;
        }
        await AfterChangeAsync();
    }

    /// <summary>Applies every one-click fix, then runs the HDR test last, since it needs the user's eyes.</summary>
    private async Task ApplyAllAsync()
    {
        if (IsBusy || !await EnsureServiceAsync()) return;

        IsBusy = true;
        var count = 0;
        var errors = new List<string>();
        try
        {
            foreach (var card in ApplyNow.Where(c => c.Card.Action == FindingAction.Apply).ToList())
            {
                card.IsBusy = true;
                try
                {
                    count += (await Task.Run(() => _backend.Apply(card.Card.FindingId))).Count;
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
                finally
                {
                    card.IsBusy = false;
                }
            }
            Toast = errors.Count > 0 ? string.Format(Strings.MsgError, string.Join(" ", errors))
                : count > 0 ? AppliedMessage(count)
                : Strings.MsgNothingToChange;

            if (ApplyNow.Any(c => c.Card.Action == FindingAction.Trial))
                await RunHdrTrialAsync();
        }
        finally
        {
            IsBusy = false;
        }
        await AfterChangeAsync();
    }

    private async Task RunHdrTrialAsync()
    {
        var applied = await Task.Run(() => _backend.Apply(FindingIds.Hdr));
        if (applied.Count == 0)
        {
            Toast = Strings.MsgNothingToChange;
            return;
        }

        var trial = HdrTrial = new HdrTrialViewModel();
        var keep = await trial.Result;
        HdrTrial = null;
        if (keep)
        {
            Toast = Strings.HdrTrialKept;
            return;
        }

        var results = await Task.Run(() => _backend.Revert(applied));
        var failed = results.Where(r => r.Status != JournalStatus.Undone).Select(r => r.Message).ToList();
        Toast = failed.Count == 0 ? Strings.HdrTrialReverted : string.Format(Strings.MsgError, string.Join(" ", failed));
    }

    private async Task UndoAsync(JournalLineViewModel line)
    {
        if (IsBusy || !await EnsureServiceAsync()) return;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _backend.Undo(line.Line.Id));
            Toast = result.Status == JournalStatus.Undone ? FindingPresenter.Plural(1, Strings.MsgUndone_One, Strings.MsgUndone_Other) : result.Message;
        }
        catch (Exception ex)
        {
            Toast = string.Format(Strings.MsgError, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await AfterChangeAsync();
    }

    private async Task UndoAllAsync()
    {
        if (IsBusy || !await EnsureServiceAsync()) return;
        IsBusy = true;
        try
        {
            var results = await Task.Run(_backend.UndoAll);
            var failed = results.Where(r => r.Status != JournalStatus.Undone).Select(r => $"#{r.EntryId}: {r.Message}").ToList();
            Toast = results.Count == 0 ? Strings.MsgNothingToUndo
                : failed.Count > 0 ? string.Format(Strings.MsgError, string.Join(" ", failed))
                : FindingPresenter.Plural(results.Count, Strings.MsgUndone_One, Strings.MsgUndone_Other);
        }
        catch (Exception ex)
        {
            Toast = string.Format(Strings.MsgError, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
        await AfterChangeAsync();
    }

    private async Task AfterChangeAsync()
    {
        await RefreshJournalAsync();
        await RescanAsync(showScanning: false);
    }

    private async Task RefreshJournalAsync()
    {
        if (Service != ServiceState.Ready) return;
        try
        {
            var entries = await Task.Run(_backend.Journal);
            var lines = JournalPresenter.Lines(entries, _backend.UserSid);
            JournalLines.Clear();
            for (var i = 0; i < lines.Count; i++) JournalLines.Add(new JournalLineViewModel(lines[i], i, l => _ = UndoAsync(l), () => CanChange));
            ActiveChanges = lines.Count(l => l.CanUndo);
        }
        catch (Exception)
        {
            Service = await Task.Run(_backend.CheckService);
        }
        Raise(nameof(IsJournalEmpty));
    }

    /// <summary>Checks the service again right before a change, since it may have stopped since the start.</summary>
    private async Task<bool> EnsureServiceAsync()
    {
        Service = await Task.Run(_backend.CheckService);
        if (Service == ServiceState.Ready) return true;
        Toast = Service == ServiceState.WrongUser ? Strings.ServiceWrongUserDetail : Strings.MsgServiceOff;
        return false;
    }

    private static string AppliedMessage(int count) => FindingPresenter.Plural(count, Strings.MsgApplied_One, Strings.MsgApplied_Other);
}
