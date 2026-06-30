using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPhotosSyncer.App.Localization;
using GPhotosSyncer.App.Models;
using GPhotosSyncer.App.Services;
using GPhotosSyncer.Core;

namespace GPhotosSyncer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    // Properties that change WHAT a plan would do; editing any of them invalidates a computed plan
    // so the user can never execute a plan that no longer matches the UI.
    static readonly HashSet<string> PlanInputs = new()
    {
        nameof(Destination), nameof(SkipJson), nameof(ExcludeTrash),
        nameof(ComparisonIndex), nameof(DeletionIndex),
        nameof(AllPartsPresent), nameof(DeleteDestOnly)
    };

    readonly ProfileService _profiles = new();
    CancellationTokenSource? _cts;
    SyncPlan? _plan;

    public ObservableCollection<SourceItem> Sources { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    /// <summary>Exposed so XAML can bind localized strings via {Binding Loc[Key]}.</summary>
    public Localizer Loc => Localizer.Instance;

    /// <summary>Supplied by the View — the folder picker needs the window HWND.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    private string _destination = "";

    [ObservableProperty] private bool _skipJson = true;
    [ObservableProperty] private bool _excludeTrash = true;
    [ObservableProperty] private int _comparisonIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMirror))]
    [NotifyPropertyChangedFor(nameof(ShowOrphans))]
    private int _deletionIndex;

    [ObservableProperty] private bool _allPartsPresent;
    [ObservableProperty] private bool _deleteDestOnly;
    [ObservableProperty] private double _parallelCopies = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [NotifyPropertyChangedFor(nameof(CanSync))]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSync))]
    private bool _hasPlan;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _progressActive;

    [ObservableProperty] private string _summaryCopy = "";
    [ObservableProperty] private string _summaryUnchanged = "";
    [ObservableProperty] private string _summaryJson = "";
    [ObservableProperty] private string _summaryZoneMedia = "";
    [ObservableProperty] private string _summaryProtected = "";
    [ObservableProperty] private string _summaryConflicts = "";

    public bool IsMirror => DeletionIndex == 3;
    public bool ShowOrphans => DeletionIndex != 0;
    public bool CanAnalyze => Sources.Count > 0 && !string.IsNullOrWhiteSpace(Destination) && !IsBusy;
    public bool CanSync => HasPlan && !IsBusy;
    /// <summary>True when no operation is running — drives the lock of the whole input area.</summary>
    public bool NotBusy => !IsBusy;

    int ParallelValue => (int)Math.Clamp(double.IsNaN(ParallelCopies) ? 2 : ParallelCopies, 1, 16);

    public MainViewModel()
    {
        Sources.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanAnalyze));
            InvalidatePlan();
        };
        LoadProfile();
        StatusText = L("Status_Ready");
        Localizer.Instance.PropertyChanged += OnLocaleChanged;
    }

    void OnLocaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Localizer.Language)) return;
        SaveProfile();
        if (!IsBusy && !HasPlan) StatusText = L("Status_Ready");
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && PlanInputs.Contains(e.PropertyName))
            InvalidatePlan();
    }

    /// <summary>Discards a stale plan so Sync can't run settings the user has since changed.</summary>
    void InvalidatePlan()
    {
        if (_plan is null && !HasPlan) return;
        _plan = null;
        HasPlan = false;
        StatusText = L("Status_OptionsChanged");
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (PickFolder is null) return;
        var path = await PickFolder();
        if (!string.IsNullOrWhiteSpace(path)) AddFromPaths(new[] { path });
    }

    /// <summary>Adds one or more picked/dropped folders. If a folder contains several Takeout
    /// parts (e.g. the Downloads folder with all the takeout-… subfolders), they are all added.</summary>
    public void AddFromPaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var p in paths) added += AddOneOrParts(p);
        AddLog(added > 0
            ? Localizer.Instance.Format("Log_SourcesAdded", added)
            : L("Log_NoTakeout"));
    }

    int AddOneOrParts(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;

        var found = new List<string>();
        if (TakeoutLocator.FindPhotoBase(path) is not null) found.Add(path);
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(path))
                if (TakeoutLocator.FindPhotoBase(sub) is not null) found.Add(sub);
        }
        catch { /* unreadable folder */ }

        if (found.Count == 0) found.Add(path); // trust an explicit pick even with an unusual layout

        int n = 0;
        foreach (var f in found)
        {
            if (Sources.Any(s => string.Equals(s.InputPath, f, StringComparison.OrdinalIgnoreCase))) continue;
            Sources.Add(new SourceItem(f));
            n++;
        }
        return n;
    }

    [RelayCommand]
    private void RemoveSource(SourceItem? item)
    {
        if (item is not null) Sources.Remove(item);
    }

    [RelayCommand]
    private async Task PickDestAsync()
    {
        if (PickFolder is null) return;
        var path = await PickFolder();
        if (!string.IsNullOrWhiteSpace(path)) Destination = path;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze) return;
        IsBusy = true;
        HasPlan = false;
        _plan = null;
        StatusText = L("Status_Analyzing");
        ResetCts();

        var opts = BuildOptions();
        var bases = Sources.Select(s => s.ResolvedBase).ToList();
        var dest = Destination;
        var token = _cts!.Token;
        var log = new Progress<string>(AddLog);

        try
        {
            var plan = await Task.Run(() => new SyncPlanner(opts).Build(bases, dest, log, token), token);
            _plan = plan;
            UpdateSummary(plan);
            HasPlan = true;
            StatusText = Localizer.Instance.Format("Status_AnalysisReady", plan.CopyCount, Fmt(plan.CopyBytes));
            AddLog(StatusText);
        }
        catch (OperationCanceledException) { StatusText = L("Status_AnalysisCancelled"); AddLog(StatusText); }
        catch (Exception ex) { StatusText = L("Status_AnalysisError"); AddLog("ERROR: " + ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (_plan is null || IsBusy) return;
        IsBusy = true;
        ProgressActive = true;
        ProgressValue = 0;
        StatusText = L("Status_Syncing");
        ResetCts();

        var opts = BuildOptions();
        var dest = Destination;
        var plan = _plan;
        var token = _cts!.Token;
        var progress = new Progress<SyncProgress>(UpdateProgress);

        try
        {
            var result = await new SyncExecutor(opts).ExecuteAsync(plan, dest, progress, token);
            AddLog(Localizer.Instance.Format("Log_SyncResult",
                result.Copied, Fmt(result.CopiedBytes), result.Quarantined, Fmt(result.QuarantinedBytes), result.Errors.Count));
            foreach (var e in result.Errors.Take(10)) AddLog("  ! " + e);
            StatusText = result.Errors.Count == 0
                ? L("Status_SyncDone")
                : Localizer.Instance.Format("Status_SyncDoneErrors", result.Errors.Count);
            HasPlan = false;
            _plan = null; // consumed — re-analyze for a fresh run
        }
        catch (OperationCanceledException) { StatusText = L("Status_SyncCancelled"); AddLog(StatusText); }
        catch (Exception ex) { StatusText = L("Status_SyncError"); AddLog("ERROR: " + ex.Message); }
        finally { IsBusy = false; ProgressActive = false; SaveProfile(); }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    void ResetCts()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    SyncOptions BuildOptions() => new()
    {
        Comparison = ComparisonIndex switch
        {
            1 => ComparisonMode.NameSizeDate,
            2 => ComparisonMode.Hash,
            _ => ComparisonMode.NameSize
        },
        Deletion = DeletionIndex switch
        {
            1 => DeletionMode.Report,
            2 => DeletionMode.Quarantine,
            3 => DeletionMode.Mirror,
            _ => DeletionMode.AddOnly
        },
        AllPartsPresent = AllPartsPresent,
        DeleteDestOnlyFolders = DeleteDestOnly,
        MaxParallelCopies = ParallelValue,
        SkipExtensions = SkipJson
            ? new HashSet<string>(SyncOptions.DefaultSkipExtensions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        ExcludedTopFolders = ExcludeTrash
            ? new HashSet<string>(SyncOptions.DefaultExcludedTopFolders, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };

    void UpdateSummary(SyncPlan plan)
    {
        SummaryCopy = $"{plan.CopyCount}  ({Fmt(plan.CopyBytes)})";
        SummaryUnchanged = $"{plan.UnchangedCount}  ({Fmt(plan.UnchangedBytes)})";
        SummaryConflicts = plan.Conflicts.Count.ToString();

        var b = plan.CategorizeOrphans();
        SummaryJson = $"{b.ZoneJson.Count}  ({Fmt(b.ZoneJsonBytes)})";
        SummaryZoneMedia = $"{b.ZoneMedia.Count}  ({Fmt(b.ZoneMediaBytes)})";
        SummaryProtected = $"{b.DestOnly.Count}  ({Fmt(b.DestOnlyBytes)})";
    }

    void UpdateProgress(SyncProgress p)
    {
        ProgressValue = p.BytesTotal == 0 ? 100 : 100.0 * p.BytesDone / p.BytesTotal;
        double etaSecs = p.BytesPerSecond > 1 ? (p.BytesTotal - p.BytesDone) / p.BytesPerSecond : 0;
        var eta = TimeSpan.FromSeconds(Math.Max(0, etaSecs));
        string etaText = eta.TotalDays >= 1 ? $"{(int)eta.TotalDays}d {eta:hh\\:mm\\:ss}" : eta.ToString(@"hh\:mm\:ss");
        StatusText = $"{p.FilesDone}/{p.FilesTotal} · {Fmt(p.BytesDone)}/{Fmt(p.BytesTotal)} · {Fmt((long)p.BytesPerSecond)}/s · ETA {etaText}";
    }

    void AddLog(string message)
    {
        Log.Add(message);
        if (Log.Count > 500) Log.RemoveAt(0);
    }

    void LoadProfile()
    {
        var p = _profiles.Load();
        if (p is null) return;
        Destination = p.Destination;
        SkipJson = p.SkipJson;
        ExcludeTrash = p.ExcludeTrash;
        ComparisonIndex = Math.Clamp(p.ComparisonIndex, 0, 2);
        DeletionIndex = Math.Clamp(p.DeletionIndex, 0, 3);
        ParallelCopies = p.Parallel < 1 ? 2 : Math.Min(p.Parallel, 16);
        if (!string.IsNullOrWhiteSpace(p.Language)) Localizer.Instance.Language = p.Language;
        foreach (var s in p.Sources) Sources.Add(new SourceItem(s));
    }

    public void SaveProfile()
    {
        _profiles.Save(new SyncProfile
        {
            Sources = Sources.Select(s => s.InputPath).ToList(),
            Destination = Destination,
            SkipJson = SkipJson,
            ExcludeTrash = ExcludeTrash,
            ComparisonIndex = ComparisonIndex,
            DeletionIndex = DeletionIndex,
            Parallel = ParallelValue,
            Language = Localizer.Instance.Language
        });
    }

    static string L(string key) => Localizer.Instance[key];
    static string Fmt(long bytes) => ByteFormat.Humanize(bytes);
}
