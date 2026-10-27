using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Zenith.App.Backend;
using Zenith.Core.Bestiary;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;

namespace Zenith.App.ViewModels;

public enum BestiaryState { NotRead, Reading, Ready, Failed }

/// <summary>
/// The Bestiário page: everyone who lives in the PC, filters, search and the fiche of the selected one.
/// Reading needs no service; "Impedir de iniciar" goes through <see cref="MainViewModel"/>, which owns changes.
/// </summary>
public sealed class BestiaryViewModel : ObservableObject
{
    private readonly IBackend _backend;
    private readonly Func<InhabitantViewModel, Task> _stopStartup;
    private readonly Func<bool> _canChange;

    private BestiaryState _state = BestiaryState.NotRead;
    private string? _error;
    private BestiaryOverview? _overview;
    private IReadOnlyList<InhabitantCard> _cards = [];
    private BestiaryFilter _filter = BestiaryFilter.All;
    private string _search = "";
    private InhabitantViewModel? _selected;

    public BestiaryViewModel(IBackend backend, Func<InhabitantViewModel, Task> stopStartup, Func<bool> canChange)
    {
        _backend = backend;
        _stopStartup = stopStartup;
        _canChange = canChange;
        StopStartupCommand = new RelayCommand(() => _ = Selected is { } s ? _stopStartup(s) : Task.CompletedTask,
            () => Selected?.Card.CanStopStartup == true && _canChange());
        OpenLocationCommand = new RelayCommand(OpenLocation, () => Selected is not null);
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(), () => State != BestiaryState.Reading);
    }

    public BestiaryState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(IsReading));
            Raise(nameof(IsReady));
            Raise(nameof(IsFailed));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsReading => State is BestiaryState.Reading or BestiaryState.NotRead;
    public bool IsReady => State == BestiaryState.Ready;
    public bool IsFailed => State == BestiaryState.Failed;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public BestiaryOverview? Overview
    {
        get => _overview;
        private set
        {
            Set(ref _overview, value);
            Raise(nameof(AttentionBadge));
        }
    }

    /// <summary>The sidebar badge: programs that deserve a look (Atenção or Suspeito).</summary>
    public string? AttentionBadge => Overview is { } o && o.Counts[BestiaryFilter.Attention] > 0
        ? o.Counts[BestiaryFilter.Attention].ToString(System.Globalization.CultureInfo.CurrentCulture)
        : null;

    public ObservableCollection<FilterChipViewModel> Filters { get; } = [];
    public ObservableCollection<InhabitantViewModel> Visible { get; } = [];
    public bool IsFilterEmpty => IsReady && Visible.Count == 0;

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value ?? "")) Refill(); }
    }

    public InhabitantViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (_selected is not null) _selected.IsSelected = false;
            Set(ref _selected, value);
            if (value is not null) value.IsSelected = true;
            Raise(nameof(HasSelection));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasSelection => Selected is not null;

    public ICommand StopStartupCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand RefreshCommand { get; }

    /// <summary>Reads once, the first time the page opens.</summary>
    public Task EnsureLoadedAsync() => State == BestiaryState.NotRead ? LoadAsync() : Task.CompletedTask;

    public async Task LoadAsync(bool showReading = true)
    {
        if (showReading) State = BestiaryState.Reading;
        try
        {
            var scan = await Task.Run(_backend.Bestiary);
            _cards = BestiaryPresenter.Cards(scan);
            Overview = BestiaryPresenter.Overview(_cards, scan.HiddenProcesses);
            Error = null;
            State = BestiaryState.Ready;
            BuildFilters();
            Refill(keepSelection: true);
        }
        catch (Exception ex)
        {
            Error = string.Format(Strings.BestiaryFailed, ex.Message);
            State = BestiaryState.Failed;
        }
    }

    /// <summary>Selects one inhabitant by file name, for screenshots.</summary>
    internal void Select(string fileName) =>
        Selected = Visible.FirstOrDefault(v => v.Card.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) ?? Selected;

    private void BuildFilters()
    {
        Filters.Clear();
        foreach (var (filter, format) in new[]
                 {
                     (BestiaryFilter.All, Strings.BestiaryFilterAll),
                     (BestiaryFilter.Startup, Strings.BestiaryFilterStartup),
                     (BestiaryFilter.Heavy, Strings.BestiaryFilterHeavy),
                     (BestiaryFilter.Unknown, Strings.BestiaryFilterUnknown),
                     (BestiaryFilter.Attention, Strings.BestiaryFilterAttention),
                 })
        {
            var count = Overview!.Counts[filter];
            if (count == 0 && filter != BestiaryFilter.All && filter != _filter) continue;
            Filters.Add(new FilterChipViewModel(filter, string.Format(format, count), filter == _filter, SetFilter));
        }
    }

    private void SetFilter(BestiaryFilter filter)
    {
        _filter = filter;
        foreach (var chip in Filters) chip.IsSelected = chip.Filter == filter;
        Refill();
    }

    private void Refill(bool keepSelection = false)
    {
        var selectedPath = keepSelection ? Selected?.Card.Path : null;
        Visible.Clear();
        var index = 0;
        foreach (var card in _cards.Where(c => c.Matches(_filter) && c.Matches(Search)))
            Visible.Add(new InhabitantViewModel(card, index++, v => Selected = v));
        Selected = Visible.FirstOrDefault(v => v.Card.Path == selectedPath) ?? Visible.FirstOrDefault();
        Raise(nameof(IsFilterEmpty));
    }

    private void OpenLocation()
    {
        if (Selected is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Selected.Card.Path}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Explorer missing or the file gone: nothing useful to say beyond the path already on the fiche.
        }
    }
}

public sealed class FilterChipViewModel : ObservableObject
{
    private bool _isSelected;

    public FilterChipViewModel(BestiaryFilter filter, string label, bool isSelected, Action<BestiaryFilter> select)
    {
        Filter = filter;
        Label = label;
        _isSelected = isSelected;
        SelectCommand = new RelayCommand(() => select(filter));
    }

    public BestiaryFilter Filter { get; }
    public string Label { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ICommand SelectCommand { get; }
}

public sealed class InhabitantViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isBusy;

    public InhabitantViewModel(InhabitantCard card, int index, Action<InhabitantViewModel> select)
    {
        Card = card;
        Index = index;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public InhabitantCard Card { get; }
    public int Index { get; }
    public string Initials => Card.Initials;
    public string Name => Card.Name;
    public string FileName => Card.FileName;
    public string Category => Card.Category;
    public ThreatLevel Level => Card.Level;
    public string LevelLabel => Card.LevelLabel;
    public string Cost => Card.Cost;
    public string? ImpostorChip => Card.ImpostorChip;
    public string Who => Card.Who;
    public string ReasonsTitle => Card.ReasonsTitle;
    public IReadOnlyList<string> Reasons => Card.Reasons;
    public bool IsWorrying => Card.Level == ThreatLevel.Suspicious;
    /// <summary>An alert mark only where there is something to worry about; plain evidence gets a check.</summary>
    public string ReasonIcon => Card.Level >= ThreatLevel.Attention ? "alert" : "check";
    public IReadOnlyList<FicheStat> Stats => Card.Stats;
    public string? StartupAdvice => Card.StartupAdvice;
    public bool CanStopStartup => Card.CanStopStartup;
    public string Path => Card.Path;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public ICommand SelectCommand { get; }
}
