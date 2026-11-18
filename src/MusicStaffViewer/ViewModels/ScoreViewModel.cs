using System.Collections.ObjectModel;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Com.Example.MusicStaffViewer.Core.Services;
using Com.Example.MusicStaffViewer.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage.Streams;

namespace Com.Example.MusicStaffViewer.ViewModels;

public sealed partial class ScoreViewModel(
    ScoreMetadataReader metadata,
    UserSettingsService settingsService,
    Func<AlphaTabScoreSession> sessionFactory,
    ILogger<ScoreViewModel> logger) : ObservableObject, IAsyncDisposable
{
    private readonly ResourceLoader _strings = new();
    private readonly LinkedList<ScorePartialViewModel> _cache = new();
    private readonly HashSet<string> _renderedIds = [];
    private AlphaTabScoreSession? _session;
    private AlphaTabScoreSession? _highResSession;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _highResLifetime;
    private CancellationTokenSource? _highResDebounce;
    private AppSettings _settings = new();
    private int _generation;
    private int _totalSystems;
    private int _currentSystem;
    private int _exporting;
    private int _disposed;
    private string? _currentPath;
    private string? _loadingPath;
    private bool _highResLoading;
    private bool _highResUnsupported;
    private double _highResScale;
    private double _highResRequestedScale;
    private bool _darkPaper;
    private bool _isLoading;
    private bool _hasScore;
    private bool _isFitWidth;
    private bool _isExporting;
    private bool _isCurrentFavorite;
    private double _exportProgress;
    private string? _errorMessage;
    private string _scoreTitle = string.Empty;
    private string _composer = string.Empty;
    private string _status = string.Empty;
    private double _zoom = 1.0;
    private double _contentWidth = AlphaTabScoreSession.LogicalWidth;
    private double _layoutWidth = AlphaTabScoreSession.LogicalWidth;
    private double[] _systemBottoms = [];
    private int _measuresPerSystem = ScoreDisplayOptions.DefaultMeasuresPerSystem;
    private int _masterBarCount;
    private int _pendingResumeSystem;
    private double _pendingResumeOffset;
    private double _lastVerticalOffset;
    private List<int> _selectedTrackIndices = [];
    private ScorePartialViewModel? _header;

    public event Action<bool>? ScoreLoaded;
    public event Action<bool>? ScoreLoading;
    public event Action? ScoreReloading;
    public event Action? FavoritesChanging;
    public event Action? FavoritesChanged;
    public ObservableCollection<ScoreDocument> RecentScores { get; } = [];
    public ObservableCollection<ScoreDocument> FavoriteScores { get; } = [];
    public ObservableCollection<ScorePartialViewModel> Partials { get; } = [];
    public ObservableCollection<ScoreTrackViewModel> Tracks { get; } = [];
    public ObservableCollection<ScoreSectionViewModel> Sections { get; } = [];

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool HasScore { get => _hasScore; private set => SetProperty(ref _hasScore, value); }
    public bool IsFitWidth { get => _isFitWidth; private set => SetProperty(ref _isFitWidth, value); }
    public bool IsExporting { get => _isExporting; private set => SetProperty(ref _isExporting, value); }
    public bool IsCurrentFavorite
    {
        get => _isCurrentFavorite;
        private set => SetProperty(ref _isCurrentFavorite, value);
    }
    public double ExportProgress { get => _exportProgress; private set => SetProperty(ref _exportProgress, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string ScoreTitle { get => _scoreTitle; private set => SetProperty(ref _scoreTitle, value); }
    public string Composer { get => _composer; private set => SetProperty(ref _composer, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (SetProperty(ref _zoom, value))
            {
                OnPropertyChanged(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => $"{Zoom * 100:0}%";
    public double ContentWidth { get => _contentWidth; private set => SetProperty(ref _contentWidth, value); }
    public int MeasuresPerSystem { get => _measuresPerSystem; private set => SetProperty(ref _measuresPerSystem, value); }
    public int CurrentSystem
    {
        get => _currentSystem;
        private set
        {
            if (SetProperty(ref _currentSystem, value))
            {
                OnPropertyChanged(nameof(CurrentSystemText));
                OnPropertyChanged(nameof(CurrentMeasureText));
            }
        }
    }

    public string CurrentSystemText => _totalSystems > 0
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
            _strings.GetString("SystemPosition"), CurrentSystem, _totalSystems)
        : string.Empty;

    public int MasterBarCount => _masterBarCount;

    public string? CurrentPath => _currentPath;

    public bool HasHeader => _header is not null;
    public ImageSource? HeaderImageSource => _header?.ImageSource;
    public double HeaderDisplayWidth => _header?.DisplayWidth ?? 0;
    public double HeaderDisplayHeight => _header?.DisplayHeight ?? 0;
    public Thickness HeaderDisplayMargin => _header?.DisplayMargin ?? default;
    public bool IsHeaderLoading => _header?.IsLoading ?? false;

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(HasHeader));
        OnPropertyChanged(nameof(HeaderImageSource));
        OnPropertyChanged(nameof(HeaderDisplayWidth));
        OnPropertyChanged(nameof(HeaderDisplayHeight));
        OnPropertyChanged(nameof(HeaderDisplayMargin));
        OnPropertyChanged(nameof(IsHeaderLoading));
    }

    public string CurrentMeasureText
    {
        get
        {
            if (!HasScore || Partials.Count == 0)
            {
                return string.Empty;
            }

            ScorePartialViewModel partial = Partials[Math.Clamp(CurrentSystem - 1, 0, Partials.Count - 1)];
            if (partial.HasMasterBarRange)
            {
                return string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _strings.GetString("CurrentMeasureRange"),
                    partial.FirstMasterBarIndex + 1, partial.LastMasterBarIndex + 1);
            }

            int estimate = (partial.Index * MeasuresPerSystem) + 1;
            estimate = Math.Clamp(estimate, 1, Math.Max(1, _masterBarCount));
            return string.Format(System.Globalization.CultureInfo.CurrentCulture,
                _strings.GetString("CurrentMeasureApprox"), estimate);
        }
    }

    public int CurrentMeasure
    {
        get
        {
            if (!HasScore || Partials.Count == 0)
            {
                return 1;
            }

            ScorePartialViewModel partial = Partials[Math.Clamp(CurrentSystem - 1, 0, Partials.Count - 1)];
            int measure = partial.HasMasterBarRange
                ? partial.FirstMasterBarIndex + 1
                : (partial.Index * MeasuresPerSystem) + 1;
            return Math.Clamp(measure, 1, Math.Max(1, _masterBarCount));
        }
    }

    public AppSettings Settings => _settings;

    private double BaseRasterScale => ScoreDisplayOptions.GetBaseRasterScale(_settings.ScoreQuality);

    private double MaximumRasterScale => ScoreDisplayOptions.GetMaximumRasterScale(_settings.ScoreQuality);

    public async Task InitializeAsync(string? startupPath = null)
    {
        try
        {
            _settings = await settingsService.LoadAsync();
            MeasuresPerSystem = _settings.DefaultMeasuresPerSystem;
            string? initial = startupPath ?? _settings.LastScorePath;
            if (initial is not null && File.Exists(initial))
            {
                await OpenAsync(initial);
            }
            else
            {
                await RefreshRecentAsync();
                await RefreshFavoritesAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not initialize the score viewer");
            ErrorMessage = _strings.GetString("OpenError") + exception.Message;
        }
    }

    private async Task RefreshRecentAsync()
    {
        string? current = _currentPath;
        ScoreDocument[] recent = await Task.Run(() => _settings.RecentFiles.Where(File.Exists)
            .Select(path =>
            {
                ScoreDocument document = metadata.Read(path);
                document.IsCurrent = string.Equals(path, current, StringComparison.OrdinalIgnoreCase);
                return document;
            }).ToArray());
        bool unchanged = RecentScores.Count == recent.Length;
        for (int index = 0; unchanged && index < recent.Length; index++)
        {
            unchanged = string.Equals(RecentScores[index].Path, recent[index].Path,
                StringComparison.OrdinalIgnoreCase) &&
                RecentScores[index].IsCurrent == recent[index].IsCurrent;
        }

        if (unchanged)
        {
            return;
        }

        RecentScores.Clear();
        foreach (ScoreDocument score in recent)
        {
            RecentScores.Add(score);
        }
    }

    private async Task RefreshFavoritesAsync()
    {
        string? current = _currentPath;
        ScoreDocument[] favorites = await Task.Run(() => _settings.FavoriteFiles
            .Where(File.Exists)
            .Select(path =>
            {
                ScoreDocument document = metadata.Read(path);
                document.IsCurrent = string.Equals(path, current, StringComparison.OrdinalIgnoreCase);
                return document;
            })
            .OrderBy(document => document.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(document => document.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
        bool sameStructure = FavoriteScores.Count == favorites.Length;
        for (int index = 0; sameStructure && index < favorites.Length; index++)
        {
            sameStructure = string.Equals(FavoriteScores[index].Path, favorites[index].Path,
                StringComparison.OrdinalIgnoreCase) &&
                string.Equals(FavoriteScores[index].Title, favorites[index].Title, StringComparison.Ordinal) &&
                string.Equals(FavoriteScores[index].Composer, favorites[index].Composer, StringComparison.Ordinal);
        }

        FavoritesChanging?.Invoke();
        try
        {
            if (sameStructure)
            {
                for (int index = 0; index < favorites.Length; index++)
                {
                    if (FavoriteScores[index].IsCurrent != favorites[index].IsCurrent)
                    {
                        FavoriteScores[index] = favorites[index];
                    }
                }
            }
            else
            {
                FavoriteScores.Clear();
                foreach (ScoreDocument score in favorites)
                {
                    FavoriteScores.Add(score);
                }
            }
        }
        finally
        {
            FavoritesChanged?.Invoke();
        }

        UpdateCurrentFavorite();
    }

    private bool IsFavorite(string path) =>
        _settings.FavoriteFiles.Contains(path, StringComparer.OrdinalIgnoreCase);

    private void UpdateCurrentFavorite() =>
        IsCurrentFavorite = _currentPath is not null && IsFavorite(_currentPath);

    public Task OpenAsync(string path)
    {
        MeasuresPerSystem = _settings.DefaultMeasuresPerSystem;
        return LoadScoreAsync(path, preserveZoom: false);
    }

    public Task OpenFromExternalAsync(string path)
    {
        if (string.Equals(path, _loadingPath, StringComparison.OrdinalIgnoreCase) ||
            (HasScore && string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        return OpenAsync(path);
    }

    private async Task LoadScoreAsync(string path, bool preserveZoom)
    {
        if (!preserveZoom)
        {
            await SaveCurrentViewStateAsync();
        }

        ScoreViewState? savedView = preserveZoom ? null : UserSettingsService.GetView(_settings, path);
        bool fitWidth = !preserveZoom &&
            (savedView is not null ? savedView.FitWidth : _settings.InitialZoomMode == InitialZoomMode.FitWidth);
        double requestedZoom = preserveZoom
            ? Zoom
            : savedView?.Zoom ?? (fitWidth ? 1.0 : _settings.DefaultZoom);
        if (!preserveZoom)
        {
            _selectedTrackIndices = savedView?.VisibleTrackIndices
                .Where(index => index >= 0).Distinct().OrderBy(index => index).ToList() ?? [];
        }

        ScoreLoading?.Invoke(preserveZoom);
        int generation = ++_generation;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        CancellationToken token = _lifetime.Token;
        await DisposeHighResAsync();
        await DisposeSessionAsync(_session);
        _session = null;

        bool darkPaper = _settings.SheetStyle == SheetStyle.Dark ||
            (_settings.SheetStyle == SheetStyle.FollowDisplay &&
            (_settings.DisplayMode == DisplayMode.Dark ||
             (_settings.DisplayMode == DisplayMode.System && App.IsSystemDark)));
        _darkPaper = darkPaper;
        _highResUnsupported = false;
        _loadingPath = path;
        _currentPath = null;
        _cache.Clear();
        _renderedIds.Clear();
        Partials.Clear();
        _header = null;
        NotifyHeaderChanged();
        _systemBottoms = [];
        ErrorMessage = null;
        ScoreTitle = string.Empty;
        Composer = string.Empty;
        _layoutWidth = AlphaTabScoreSession.LogicalWidth;
        HasScore = false;
        IsFitWidth = false;
        CurrentSystem = 0;
        _totalSystems = 0;
        IsLoading = true;
        Status = _strings.GetString("LayingOut");
        try
        {
            AlphaTabScoreSession session = sessionFactory();
            _session = session;
            ScoreLayoutResult layout;
            try
            {
                layout = await session.LoadAsync(path, darkPaper, MeasuresPerSystem,
                    _settings.NoteSpacing, BaseRasterScale, _selectedTrackIndices, token);
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "requested")
            {
                _selectedTrackIndices = [];
                layout = await session.LoadAsync(path, darkPaper, MeasuresPerSystem,
                    _settings.NoteSpacing, BaseRasterScale, null, token);
            }

            if (generation != _generation)
            {
                return;
            }

            _currentPath = path;
            ScoreTitle = layout.Title;
            Composer = layout.Composer;
            int resumeSystem = savedView is not null && savedView.TopMeasure > 0
                ? FindSystemInLayout(layout, savedView.TopMeasure, MeasuresPerSystem)
                : savedView?.TopSystem ?? 0;
            ApplyLayout(layout, requestedZoom, resumeSystem,
                savedView?.OffsetWithinSystem ?? 0);
            UserSettingsService.Remember(_settings, path);
            await RefreshRecentAsync();
            await RefreshFavoritesAsync();
            await settingsService.SaveAsync(_settings);
            ScoreLoaded?.Invoke(fitWidth);
            logger.LogInformation("Laid out {Title}: {SystemCount} systems", layout.Title, layout.Partials.Count);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                _currentPath = null;
                ScoreTitle = string.Empty;
                Composer = string.Empty;
                _layoutWidth = AlphaTabScoreSession.LogicalWidth;
                _header = null;
                NotifyHeaderChanged();
                _systemBottoms = [];
                _totalSystems = 0;
                CurrentSystem = 0;
                try
                {
                    await RefreshRecentAsync();
                    await RefreshFavoritesAsync();
                }
                catch (Exception refreshException)
                {
                    logger.LogWarning(refreshException, "Could not refresh recent scores after a failed open");
                }

                logger.LogError(exception, "Could not open score {Path}", path);
                ErrorMessage = _strings.GetString("OpenError") + exception.Message;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _loadingPath = null;
                IsLoading = false;
            }
        }
    }

    private void ApplyLayout(ScoreLayoutResult layout, double requestedZoom, int resumeSystem,
        double resumeOffset)
    {
        _layoutWidth = layout.Width;
        _masterBarCount = layout.MasterBarCount;
        _systemBottoms = new double[layout.Partials.Count];
        ScorePartialLayout? header = layout.Header;
        _header = header is null ? null : new ScorePartialViewModel(header, -1) { IsVisible = true };
        NotifyHeaderChanged();
        double contentTop = header?.Y ?? layout.Partials[0].Y;
        double previousBottom = header is null ? contentTop : header.Y + header.Height;
        Partials.Clear();
        for (int index = 0; index < layout.Partials.Count; index++)
        {
            ScorePartialLayout partial = layout.Partials[index];
            double topGap = Math.Max(0, partial.Y - previousBottom);
            var partialViewModel = new ScorePartialViewModel(partial, index, topGap);
            partialViewModel.SetMasterBarRange(partial.FirstMasterBarIndex, partial.LastMasterBarIndex);
            Partials.Add(partialViewModel);
            _systemBottoms[index] = partial.Y + partial.Height - contentTop;
            previousBottom = partial.Y + partial.Height;
        }

        RebuildTracks(layout);
        RebuildSections(layout);
        HasScore = true;
        IsFitWidth = false;
        SetZoom(requestedZoom);
        _ = RequestHeaderAsync();
        _totalSystems = layout.Partials.Count;
        CurrentSystem = 1;
        UpdateSystemStatus();
        _pendingResumeSystem = resumeSystem > 0
            ? Math.Clamp(resumeSystem, 1, layout.Partials.Count)
            : 0;
        _pendingResumeOffset = Math.Max(0, resumeOffset);
        _lastVerticalOffset = _pendingResumeSystem > 0
            ? GetSystemOffset(_pendingResumeSystem) + (_pendingResumeOffset * Zoom)
            : 0;
        OnPropertyChanged(nameof(MasterBarCount));
        OnPropertyChanged(nameof(CurrentMeasureText));
        OnPropertyChanged(nameof(CurrentMeasure));
    }

    private void RebuildTracks(ScoreLayoutResult layout)
    {
        var selected = new HashSet<int>(layout.SelectedTrackIndices);
        Tracks.Clear();
        foreach (ScoreTrackInfo info in layout.Tracks)
        {
            string name = !string.IsNullOrWhiteSpace(info.Name)
                ? info.Name
                : !string.IsNullOrWhiteSpace(info.ShortName)
                    ? info.ShortName
                    : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                        _strings.GetString("TrackFallbackName"), info.Index + 1);
            string display = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                _strings.GetString("TrackDisplayName"), info.Index + 1, name);
            Tracks.Add(new ScoreTrackViewModel(info.Index, display, info.IsPercussion)
            {
                IsSelected = selected.Contains(info.Index)
            });
        }

        _selectedTrackIndices = layout.SelectedTrackIndices
            .Where(index => index >= 0).Distinct().OrderBy(index => index).ToList();
    }

    private void RebuildSections(ScoreLayoutResult layout)
    {
        Sections.Clear();
        foreach (ScoreSectionInfo info in layout.Sections)
        {
            string label = !string.IsNullOrWhiteSpace(info.Marker) ? info.Marker : info.Text;
            string display = string.IsNullOrWhiteSpace(label)
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _strings.GetString("SectionMeasureOnly"), info.MasterBarIndex + 1)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _strings.GetString("SectionDisplayName"), info.MasterBarIndex + 1, label);
            Sections.Add(new ScoreSectionViewModel(info.MasterBarIndex, display));
        }
    }

    private async Task<bool> RelayoutAsync(int? anchorMeasure = null)
    {
        AlphaTabScoreSession? session = _session;
        if (session is null || _currentPath is null || Partials.Count == 0)
        {
            return false;
        }

        int generation = _generation;
        CancellationToken token = _lifetime?.Token ?? CancellationToken.None;
        int measure = Math.Clamp(anchorMeasure ?? CurrentMeasure, 1, Math.Max(1, _masterBarCount));
        double zoom = Zoom;
        bool fitWidth = IsFitWidth;
        double offsetWithinSystem = Math.Max(0,
            (_lastVerticalOffset - GetSystemOffset(CurrentSystem)) / Math.Max(0.0001, Zoom));
        IReadOnlyList<int> tracks = [.. _selectedTrackIndices];

        await DisposeHighResAsync();
        Status = _strings.GetString("Relayouting");
        try
        {
            ScoreLayoutResult layout = await session.RelayoutAsync(_darkPaper, MeasuresPerSystem,
                _settings.NoteSpacing, BaseRasterScale, tracks, token);
            if (generation != _generation)
            {
                return false;
            }

            int resumeSystem = FindSystemInLayout(layout, measure, MeasuresPerSystem);
            ApplyLayout(layout, zoom, resumeSystem, offsetWithinSystem);
            IsFitWidth = fitWidth;
            ScoreLoaded?.Invoke(fitWidth);
            await SaveCurrentViewStateAsync();
            logger.LogInformation("Relayout finished: {SystemCount} systems, tracks {Tracks}",
                layout.Partials.Count, string.Join(',', layout.SelectedTrackIndices));
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                logger.LogError(exception, "Could not relayout score {Path}", _currentPath);
                ErrorMessage = _strings.GetString("RelayoutError") + exception.Message;
            }

            return false;
        }
    }

    public async Task SetTrackVisibilityAsync(IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        List<int> normalized = indices.Where(index => index >= 0)
            .Distinct().OrderBy(index => index).ToList();
        if (normalized.Count == 0 || normalized.SequenceEqual(_selectedTrackIndices))
        {
            return;
        }

        List<int> previous = [.. _selectedTrackIndices];
        _selectedTrackIndices = normalized;
        logger.LogInformation("Track visibility changed: {Tracks}", string.Join(',', normalized));
        if (!await RelayoutAsync())
        {
            _selectedTrackIndices = previous;
            SyncTrackSelection();
        }
    }

    public Task ShowAllTracksAsync() =>
        SetTrackVisibilityAsync(Tracks.Select(track => track.Index).ToList());

    private void SyncTrackSelection()
    {
        var selected = new HashSet<int>(_selectedTrackIndices);
        foreach (ScoreTrackViewModel track in Tracks)
        {
            track.IsSelected = selected.Contains(track.Index);
        }
    }

    public double GetSystemOffset(int systemIndex)
    {
        if (!HasScore || Partials.Count == 0)
        {
            return 0;
        }

        int index = Math.Clamp(systemIndex, 1, Partials.Count) - 1;
        if (index == 0)
        {
            return 0;
        }

        return (_systemBottoms[index - 1] * Zoom) + ScoreDisplayOptions.EdgeMargin;
    }

    public int ResolveSystemForMeasure(int measure)
    {
        if (!HasScore || Partials.Count == 0)
        {
            return 1;
        }

        int target = Math.Clamp(measure, 1, Math.Max(1, _masterBarCount)) - 1;
        for (int index = 0; index < Partials.Count; index++)
        {
            ScorePartialViewModel partial = Partials[index];
            if (partial.HasMasterBarRange && target >= partial.FirstMasterBarIndex &&
                target <= partial.LastMasterBarIndex)
            {
                return index + 1;
            }
        }

        return Math.Clamp((target / Math.Max(1, MeasuresPerSystem)) + 1, 1, Partials.Count);
    }

    private static int FindSystemInLayout(ScoreLayoutResult layout, int measure, int measuresPerSystem)
    {
        int target = Math.Clamp(measure, 1, Math.Max(1, layout.MasterBarCount)) - 1;
        for (int index = 0; index < layout.Partials.Count; index++)
        {
            ScorePartialLayout partial = layout.Partials[index];
            if (partial.FirstMasterBarIndex >= 0 && target >= partial.FirstMasterBarIndex &&
                target <= partial.LastMasterBarIndex)
            {
                return index + 1;
            }
        }

        return Math.Clamp((target / Math.Max(1, measuresPerSystem)) + 1, 1, layout.Partials.Count);
    }

    public bool TryConsumeResumeTarget(out int systemIndex, out double offsetWithinSystem)
    {
        systemIndex = _pendingResumeSystem;
        offsetWithinSystem = _pendingResumeOffset;
        _pendingResumeSystem = 0;
        _pendingResumeOffset = 0;
        if (systemIndex <= 0 || Partials.Count == 0)
        {
            return false;
        }

        systemIndex = Math.Clamp(systemIndex, 1, Partials.Count);
        return true;
    }

    public async Task SaveViewStateAsync(double verticalOffset)
    {
        _lastVerticalOffset = verticalOffset;
        await SaveCurrentViewStateAsync();
    }

    public async Task SaveCurrentViewStateAsync()
    {
        if (_currentPath is null || !HasScore || Partials.Count == 0)
        {
            return;
        }

        try
        {
            UpdateCurrentSystem(_lastVerticalOffset);
            int systemIndex = Math.Clamp(CurrentSystem, 1, Partials.Count);
            double top = GetSystemOffset(systemIndex);
            UserSettingsService.RememberView(_settings, new ScoreViewState
            {
                Path = _currentPath,
                Zoom = Zoom,
                FitWidth = IsFitWidth,
                TopSystem = systemIndex,
                TopMeasure = CurrentMeasure,
                OffsetWithinSystem = Math.Max(0,
                    (_lastVerticalOffset - top) / Math.Max(0.0001, Zoom)),
                VisibleTrackIndices = [.. _selectedTrackIndices]
            });
            await settingsService.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not save the view state for {Path}", _currentPath);
        }
    }

    public async Task ReloadAsync()
    {
        if (_currentPath is not null)
        {
            ScoreReloading?.Invoke();
            await LoadScoreAsync(_currentPath, preserveZoom: true);
        }
    }

    public async Task CloseScoreAsync()
    {
        await SaveCurrentViewStateAsync();
        _generation++;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        await DisposeHighResAsync();
        await DisposeSessionAsync(_session);
        _session = null;

        _cache.Clear();
        _renderedIds.Clear();
        Partials.Clear();
        Tracks.Clear();
        Sections.Clear();
        _header = null;
        NotifyHeaderChanged();
        _systemBottoms = [];
        _currentPath = null;
        _loadingPath = null;
        _darkPaper = false;
        _highResUnsupported = false;
        _masterBarCount = 0;
        _selectedTrackIndices = [];
        _pendingResumeSystem = 0;
        _pendingResumeOffset = 0;
        _lastVerticalOffset = 0;
        ScoreTitle = string.Empty;
        Composer = string.Empty;
        _layoutWidth = AlphaTabScoreSession.LogicalWidth;
        ErrorMessage = null;
        HasScore = false;
        IsLoading = false;
        IsFitWidth = false;
        _totalSystems = 0;
        CurrentSystem = 0;
        Status = string.Empty;
        _settings.LastScorePath = null;
        try
        {
            await RefreshRecentAsync();
            await RefreshFavoritesAsync();
            await settingsService.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save settings after closing the score");
        }

        ScoreLoaded?.Invoke(false);
    }

    public async Task ToggleFavoriteAsync()
    {
        if (_currentPath is null)
        {
            return;
        }

        if (IsFavorite(_currentPath))
        {
            UserSettingsService.Unfavorite(_settings, _currentPath);
        }
        else
        {
            UserSettingsService.Favorite(_settings, _currentPath);
        }

        await RefreshFavoritesAsync();
        await settingsService.SaveAsync(_settings);
        logger.LogInformation("Toggled favorite for {Path}: {IsFavorite}", _currentPath, IsCurrentFavorite);
    }

    public async Task RemoveFavoriteAsync(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);
        UserSettingsService.Unfavorite(_settings, score.Path);
        await RefreshFavoritesAsync();
        await settingsService.SaveAsync(_settings);
        logger.LogInformation("Removed {Path} from favorites", score.Path);
    }

    public async Task SetMeasuresPerSystemAsync(int value)
    {
        int measures = Math.Clamp(value, ScoreDisplayOptions.MinimumMeasuresPerSystem,
            ScoreDisplayOptions.MaximumMeasuresPerSystem);
        if (measures == MeasuresPerSystem)
        {
            return;
        }

        int previousMeasures = MeasuresPerSystem;
        int anchorMeasure = CurrentMeasure;
        MeasuresPerSystem = measures;
        if (!await RelayoutAsync(anchorMeasure))
        {
            MeasuresPerSystem = previousMeasures;
        }
    }

    private PdfExportOptions CreatePdfExportOptions() => _settings.PdfPageSize switch
    {
        PdfPageSize.A3 => new PdfExportOptions
        {
            PageWidth = PdfExportOptions.A3Width,
            PageHeight = PdfExportOptions.A3Height
        },
        PdfPageSize.Letter => new PdfExportOptions
        {
            PageWidth = PdfExportOptions.LetterWidth,
            PageHeight = PdfExportOptions.LetterHeight
        },
        _ => new PdfExportOptions()
    };

    private static async Task<PdfSystem> RenderPdfSystemAsync(AlphaTabScoreSession session, int index,
        double width, double height, CancellationToken cancellationToken)
    {
        RenderedPartial rendered = await session.RenderPartialAsync(index, cancellationToken);
        return new PdfSystem(rendered.Png, width, height);
    }

    public async Task ExportPdfAsync(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Interlocked.Exchange(ref _exporting, 1) != 0)
        {
            throw new InvalidOperationException("A PDF export is already running.");
        }

        AlphaTabScoreSession? exportSession = null;
        try
        {
            ErrorMessage = null;
            AlphaTabScoreSession baseSession = _session ?? throw new InvalidOperationException("No score is loaded.");
            if (Partials.Count == 0)
            {
                throw new InvalidOperationException("No score is loaded.");
            }

            int generation = _generation;
            CancellationToken token = _lifetime?.Token ?? CancellationToken.None;
            double pdfScale = ScoreDisplayOptions.GetPdfRasterScale(_settings.PdfQuality);
            AlphaTabScoreSession session;
            if (_highResSession is not null && _highResScale >= pdfScale - 0.01)
            {
                session = _highResSession;
            }
            else if (BaseRasterScale >= pdfScale - 0.01)
            {
                session = baseSession;
            }
            else
            {
                exportSession = await CreatePdfSessionAsync(pdfScale, generation, token);
                session = exportSession ?? baseSession;
            }

            IsExporting = true;
            ExportProgress = 0;
            PdfSystem? header = null;
            ScorePartialViewModel? headerModel = _header;
            if (headerModel is not null)
            {
                RenderedPartial renderedHeader = await session.RenderHeaderPartialAsync(token);
                if (generation != _generation)
                {
                    throw new OperationCanceledException();
                }

                header = new PdfSystem(renderedHeader.Png, headerModel.BaseWidth, headerModel.BaseHeight);
            }

            (int Index, double Width, double Height)[] snapshot = Partials
                .Select(partial => (partial.Index, partial.BaseWidth, partial.BaseHeight))
                .ToArray();
            double maxWidth = Math.Max(snapshot.Max(partial => partial.Width), header?.Width ?? 0);
            PdfExportOptions options = CreatePdfExportOptions();
            var progress = new Progress<int>(completed =>
            {
                if (generation != _generation)
                {
                    return;
                }

                ExportProgress = (double)completed / snapshot.Length;
                Status = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _strings.GetString("ExportPdfProgress"), completed, snapshot.Length);
            });

            string title = ScoreTitle;
            string composer = _composer;
            await Task.Run(() => PdfExporter.WriteSystemsAsync(output, title, composer, snapshot.Length,
                maxWidth, (index, cancellationToken) =>
                {
                    if (generation != _generation)
                    {
                        throw new OperationCanceledException();
                    }

                    (int systemIndex, double width, double height) = snapshot[index];
                    return RenderPdfSystemAsync(session, systemIndex, width, height, cancellationToken);
                }, header, options, progress, token), token);
        }
        finally
        {
            IsExporting = false;
            ExportProgress = 0;
            Interlocked.Exchange(ref _exporting, 0);
            await DisposeSessionAsync(exportSession);
            UpdateSystemStatus();
        }
    }

    private async Task<AlphaTabScoreSession?> CreatePdfSessionAsync(double rasterScale, int generation,
        CancellationToken cancellationToken)
    {
        if (_currentPath is null)
        {
            return null;
        }

        AlphaTabScoreSession candidate = sessionFactory();
        try
        {
            ScoreLayoutResult layout = await candidate.LoadAsync(_currentPath, _darkPaper, MeasuresPerSystem,
                _settings.NoteSpacing, rasterScale, _selectedTrackIndices, cancellationToken);
            if (generation != _generation || layout.Partials.Count != Partials.Count)
            {
                await DisposeSessionAsync(candidate);
                return null;
            }

            return candidate;
        }
        catch
        {
            await DisposeSessionAsync(candidate);
            throw;
        }
    }

    private void UpdateSystemStatus()
    {
        Status = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            _strings.GetString("SystemCount"), _totalSystems);
        OnPropertyChanged(nameof(CurrentSystemText));
    }

    public void ReportOpenError(Exception exception)
    {
        logger.LogError(exception, "Could not open a score with the file picker or drag and drop");
        ErrorMessage = _strings.GetString("OpenError") + exception.Message;
    }

    public void ReportUnsupportedFile(string fileName) =>
        ErrorMessage = _strings.GetString("UnsupportedFileError") + fileName;

    public void ReportExportError(Exception exception)
    {
        logger.LogError(exception, "Could not export the score as PDF");
        ErrorMessage = _strings.GetString("ExportPdfError") + exception.Message;
    }

    public void ReportExportOpenError(Exception exception)
    {
        logger.LogError(exception, "Could not open the exported PDF");
        ErrorMessage = _strings.GetString("ExportPdfOpenError") + exception.Message;
    }

    public void ReportFavoriteError(Exception exception)
    {
        logger.LogError(exception, "Could not update the favorite score list");
        ErrorMessage = _strings.GetString("FavoriteSaveError") + exception.Message;
    }

    public void ClearError() => ErrorMessage = null;

    public void FitWidth(double viewportWidth)
    {
        if (viewportWidth <= 0 || !HasScore)
        {
            return;
        }

        double target = ScoreDisplayOptions.ClampZoom(
            (viewportWidth - (ScoreDisplayOptions.EdgeMargin * 2)) / _layoutWidth);
        if (Math.Abs(target - Zoom) >= 0.0005)
        {
            SetZoom(target);
        }

        IsFitWidth = true;
    }

    public void UpdateCurrentSystem(double verticalOffset)
    {
        _lastVerticalOffset = verticalOffset;
        if (!HasScore || Partials.Count == 0)
        {
            CurrentSystem = 0;
            return;
        }

        CurrentSystem = FindSystemFromOffset(verticalOffset);
    }

    private int FindSystemFromOffset(double verticalOffset)
    {
        double offset = Math.Max(0, verticalOffset - ScoreDisplayOptions.EdgeMargin) / Zoom;
        int found = Array.BinarySearch(_systemBottoms, offset);
        int system = found >= 0 ? found + 2 : ~found + 1;
        return Math.Clamp(system, 1, Partials.Count);
    }

    public void ZoomBy(double delta) => SetZoom(Zoom + delta);

    [RelayCommand]
    private void ZoomIn() => SetZoom(ScoreDisplayOptions.NextZoom(Zoom, 1));

    [RelayCommand]
    private void ZoomOut() => SetZoom(ScoreDisplayOptions.NextZoom(Zoom, -1));

    [RelayCommand]
    private void ResetZoom() => SetZoom(1.0);

    private void SetZoom(double zoom)
    {
        double previousZoom = Zoom;
        IsFitWidth = false;
        Zoom = ScoreDisplayOptions.ClampZoom(zoom);
        ContentWidth = _layoutWidth * Zoom;
        foreach (ScorePartialViewModel partial in Partials)
        {
            partial.SetZoom(Zoom);
        }

        _header?.SetZoom(Zoom);
        NotifyHeaderChanged();
        if (HasScore)
        {
            _ = ScheduleHighResAsync(GetRequiredRasterScale(Zoom));
        }

        if (HasScore && Math.Abs(Zoom - previousZoom) >= 0.2)
        {
            RefreshDecodedImages();
        }
    }

    private double GetRequiredRasterScale(double zoom)
    {
        double rasterizationScale = App.MainWindow?.RootElement.XamlRoot?.RasterizationScale ?? 1.0;
        double target = zoom * rasterizationScale * 1.25;
        double baseScale = BaseRasterScale;
        if (target <= baseScale)
        {
            return baseScale;
        }

        return Math.Clamp(Math.Ceiling(target * 2) / 2, baseScale, MaximumRasterScale);
    }

    private double GetDesiredSourceScale()
    {
        if (_highResSession is not null && _highResScale > BaseRasterScale + 0.01 &&
            GetRequiredRasterScale(Zoom) <= _highResScale + 0.01)
        {
            return _highResScale;
        }

        return BaseRasterScale;
    }

    private void RefreshDecodedImages()
    {
        foreach (ScorePartialViewModel partial in Partials)
        {
            if (!partial.IsVisible || partial.ImageSource is null || partial.IsLoading ||
                partial.IsDecoding || double.IsNaN(partial.DecodedZoom) ||
                Math.Abs(partial.DecodedZoom - Zoom) < 0.2)
            {
                continue;
            }

            _ = RefreshDecodedImageAsync(partial);
        }

        if (_header is { ImageSource: not null, IsLoading: false, IsDecoding: false } header &&
            !double.IsNaN(header.DecodedZoom) && Math.Abs(header.DecodedZoom - Zoom) >= 0.2)
        {
            _ = RefreshDecodedImageAsync(header);
        }
    }

    private async Task RefreshDecodedImageAsync(ScorePartialViewModel partial)
    {
        byte[]? bytes = partial.PngBytes;
        if (bytes is null || !partial.IsVisible)
        {
            return;
        }

        partial.IsDecoding = true;
        int generation = _generation;
        double decodeZoom = Zoom;
        try
        {
            BitmapImage image = await CreateImageAsync(partial, bytes, partial.SourceRasterScale);
            if (generation == _generation && partial.IsVisible)
            {
                partial.ImageSource = image;
                partial.DecodedZoom = decodeZoom;
                if (ReferenceEquals(partial, _header))
                {
                    NotifyHeaderChanged();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not redecode system {SystemId}", partial.Id);
        }
        finally
        {
            partial.IsDecoding = false;
        }
    }

    public async Task RequestPartialAsync(ScorePartialViewModel partial)
    {
        partial.IsVisible = true;
        if (partial.ImageSource is not null || partial.IsLoading || _session is null)
        {
            return;
        }

        AlphaTabScoreSession renderSession = _highResSession is not null &&
            GetDesiredSourceScale() > BaseRasterScale + 0.01
            ? _highResSession
            : _session;
        double sourceScale = ReferenceEquals(renderSession, _highResSession)
            ? _highResScale
            : BaseRasterScale;
        partial.IsLoading = true;
        int generation = _generation;
        try
        {
            byte[] bytes;
            if (partial.PngBytes is not null && partial.SourceRasterScale >= sourceScale - 0.01)
            {
                bytes = partial.PngBytes;
            }
            else
            {
                RenderedPartial rendered = await renderSession.RenderPartialAsync(partial.Index,
                    _lifetime?.Token ?? CancellationToken.None);
                if (generation != _generation)
                {
                    return;
                }

                bytes = rendered.Png;
                partial.PngBytes = bytes;
                partial.SourceRasterScale = sourceScale;
                partial.SetMasterBarRange(rendered.FirstMasterBarIndex, rendered.LastMasterBarIndex);
                if (partial.Index == CurrentSystem - 1)
                {
                    OnPropertyChanged(nameof(CurrentMeasureText));
                }

                TouchCache(partial);
            }

            if (!partial.IsVisible)
            {
                return;
            }

            double decodeZoom = Zoom;
            BitmapImage image = await CreateImageAsync(partial, bytes, partial.SourceRasterScale);
            if (generation == _generation && partial.IsVisible)
            {
                partial.ImageSource = image;
                partial.DecodedZoom = decodeZoom;
                MarkRendered(partial.Id);
                logger.LogInformation("Rendered score system {SystemId}", partial.Id);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not render system {SystemId}", partial.Id);
            if (generation == _generation)
            {
                ErrorMessage = _strings.GetString("RenderError") + exception.Message;
            }
        }
        finally
        {
            partial.IsLoading = false;
        }

        if (generation == _generation && partial.IsVisible && _highResSession is not null &&
            partial.SourceRasterScale < _highResScale - 0.01)
        {
            _ = UpgradePartialAsync(partial, generation);
        }
    }

    private async Task RequestHeaderAsync()
    {
        ScorePartialViewModel? header = _header;
        if (header is null || header.ImageSource is not null || header.IsLoading || _session is null)
        {
            return;
        }

        AlphaTabScoreSession renderSession = _highResSession is not null &&
            GetDesiredSourceScale() > BaseRasterScale + 0.01
            ? _highResSession
            : _session;
        double sourceScale = ReferenceEquals(renderSession, _highResSession)
            ? _highResScale
            : BaseRasterScale;
        header.IsLoading = true;
        NotifyHeaderChanged();
        int generation = _generation;
        try
        {
            byte[] bytes;
            if (header.PngBytes is not null && header.SourceRasterScale >= sourceScale - 0.01)
            {
                bytes = header.PngBytes;
            }
            else
            {
                RenderedPartial rendered = await renderSession.RenderHeaderPartialAsync(
                    _lifetime?.Token ?? CancellationToken.None);
                if (generation != _generation || !ReferenceEquals(header, _header))
                {
                    return;
                }

                bytes = rendered.Png;
                header.PngBytes = bytes;
                header.SourceRasterScale = sourceScale;
            }

            double decodeZoom = Zoom;
            BitmapImage image = await CreateImageAsync(header, bytes, header.SourceRasterScale);
            if (generation == _generation && ReferenceEquals(header, _header))
            {
                header.ImageSource = image;
                header.DecodedZoom = decodeZoom;
                logger.LogInformation("Rendered score header {HeaderId}", header.Id);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not render the score header");
        }
        finally
        {
            header.IsLoading = false;
            if (ReferenceEquals(header, _header))
            {
                NotifyHeaderChanged();
            }
        }
    }

    private static async Task<BitmapImage> CreateImageAsync(ScorePartialViewModel partial, byte[] bytes,
        double sourceRasterScale)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var image = new BitmapImage { DecodePixelType = DecodePixelType.Physical };
        int naturalWidth = Math.Max(1,
            (int)Math.Ceiling(partial.BaseWidth * sourceRasterScale));
        double rasterizationScale = App.MainWindow?.RootElement.XamlRoot?.RasterizationScale ?? 1.0;
        int decodeWidth = Math.Clamp(
            (int)Math.Ceiling(partial.DisplayWidth * rasterizationScale * 1.25),
            1, naturalWidth);
        if (decodeWidth < naturalWidth)
        {
            image.DecodePixelWidth = decodeWidth;
        }

        await image.SetSourceAsync(stream);
        return image;
    }

    public async Task PrefetchPartialAsync(int index)
    {
        if (index < 0 || index >= Partials.Count || _session is null)
        {
            return;
        }

        ScorePartialViewModel partial = Partials[index];
        if (partial.PngBytes is not null || partial.IsLoading)
        {
            return;
        }

        AlphaTabScoreSession renderSession = _highResSession is not null &&
            GetDesiredSourceScale() > BaseRasterScale + 0.01
            ? _highResSession
            : _session;
        double sourceScale = ReferenceEquals(renderSession, _highResSession)
            ? _highResScale
            : BaseRasterScale;
        int generation = _generation;
        partial.IsLoading = true;
        try
        {
            RenderedPartial rendered = await renderSession.RenderPartialAsync(partial.Index,
                _lifetime?.Token ?? CancellationToken.None);
            if (generation == _generation)
            {
                partial.PngBytes = rendered.Png;
                partial.SourceRasterScale = sourceScale;
                partial.SetMasterBarRange(rendered.FirstMasterBarIndex, rendered.LastMasterBarIndex);
                if (partial.Index == CurrentSystem - 1)
                {
                    OnPropertyChanged(nameof(CurrentMeasureText));
                }

                TouchCache(partial);
                MarkRendered(partial.Id);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not prefetch system {SystemId}", partial.Id);
        }
        finally
        {
            partial.IsLoading = false;
        }

        if (generation == _generation && partial.IsVisible && partial.PngBytes is not null)
        {
            await RequestPartialAsync(partial);
        }
    }

    private async Task ScheduleHighResAsync(double scale)
    {
        _highResDebounce?.Cancel();
        _highResDebounce?.Dispose();
        var debounce = new CancellationTokenSource();
        _highResDebounce = debounce;
        try
        {
            await Task.Delay(150, debounce.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!debounce.IsCancellationRequested)
        {
            await EnsureHighResAsync(scale);
        }
    }

    private async Task EnsureHighResAsync(double scale)
    {
        if (Volatile.Read(ref _exporting) != 0)
        {
            return;
        }

        double target = Math.Clamp(scale, BaseRasterScale, MaximumRasterScale);
        if (target <= BaseRasterScale + 0.01 || _highResUnsupported || _session is null ||
            _currentPath is null || !HasScore)
        {
            return;
        }

        if (_highResSession is not null && _highResScale >= target - 0.01)
        {
            return;
        }

        if (_highResLoading && _highResRequestedScale >= target - 0.01)
        {
            return;
        }

        int generation = _generation;
        string path = _currentPath;
        bool darkPaper = _darkPaper;
        int measures = MeasuresPerSystem;
        NoteSpacing noteSpacing = _settings.NoteSpacing;
        IReadOnlyList<int> tracks = [.. _selectedTrackIndices];
        _highResLoading = true;
        _highResRequestedScale = target;
        _highResLifetime?.Cancel();
        _highResLifetime?.Dispose();
        var lifetime = new CancellationTokenSource();
        _highResLifetime = lifetime;
        AlphaTabScoreSession? candidate = null;
        try
        {
            candidate = sessionFactory();
            ScoreLayoutResult layout = await candidate.LoadAsync(path, darkPaper, measures,
                noteSpacing, target, tracks, lifetime.Token);
            if (generation != _generation)
            {
                return;
            }

            if (!LayoutsMatch(layout.Partials))
            {
                _highResUnsupported = true;
                logger.LogWarning("High-resolution layout differs from the base layout; using base images");
                return;
            }

            AlphaTabScoreSession? previous = _highResSession;
            _highResSession = candidate;
            candidate = null;
            _highResScale = target;
            await DisposeSessionAsync(previous);

            await RefreshVisibleHighResAsync(generation);
            await UpgradeHeaderAsync(generation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not prepare high-resolution score images");
        }
        finally
        {
            await DisposeSessionAsync(candidate);

            if (ReferenceEquals(_highResLifetime, lifetime))
            {
                lifetime.Dispose();
                _highResLifetime = null;
                _highResLoading = false;
            }
        }
    }

    private bool LayoutsMatch(IReadOnlyList<ScorePartialLayout> layout)
    {
        if (layout.Count != Partials.Count)
        {
            return false;
        }

        for (int index = 0; index < layout.Count; index++)
        {
            ScorePartialLayout highRes = layout[index];
            ScorePartialViewModel basePartial = Partials[index];
            if (Math.Abs(highRes.X - basePartial.BaseX) > 1.0 ||
                Math.Abs(highRes.Y - basePartial.BaseY) > 1.0 ||
                Math.Abs(highRes.Width - basePartial.BaseWidth) > 1.0 ||
                Math.Abs(highRes.Height - basePartial.BaseHeight) > 1.0)
            {
                return false;
            }
        }

        return true;
    }

    private async Task RefreshVisibleHighResAsync(int generation)
    {
        Task[] tasks = Partials
            .Where(partial => partial.IsVisible && partial.ImageSource is not null)
            .Select(partial => UpgradePartialAsync(partial, generation))
            .ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task UpgradeHeaderAsync(int generation)
    {
        ScorePartialViewModel? header = _header;
        AlphaTabScoreSession? session = _highResSession;
        if (header is null || session is null || generation != _generation ||
            header.ImageSource is null || header.IsLoading || header.IsDecoding ||
            header.SourceRasterScale >= _highResScale - 0.01)
        {
            return;
        }

        header.IsDecoding = true;
        double sourceScale = _highResScale;
        try
        {
            RenderedPartial rendered = await session.RenderHeaderPartialAsync(
                _lifetime?.Token ?? CancellationToken.None);
            if (generation != _generation || !ReferenceEquals(_header, header) ||
                !ReferenceEquals(_highResSession, session))
            {
                return;
            }

            header.PngBytes = rendered.Png;
            header.SourceRasterScale = sourceScale;
            BitmapImage image = await CreateImageAsync(header, rendered.Png, sourceScale);
            if (generation == _generation && ReferenceEquals(_header, header))
            {
                header.ImageSource = image;
                header.DecodedZoom = Zoom;
                NotifyHeaderChanged();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not render the high-resolution score header");
        }
        finally
        {
            header.IsDecoding = false;
        }
    }

    private async Task UpgradePartialAsync(ScorePartialViewModel partial, int generation)
    {
        AlphaTabScoreSession? session = _highResSession;
        if (session is null || generation != _generation || partial.IsLoading || partial.IsDecoding)
        {
            return;
        }

        double sourceScale = _highResScale;
        if (partial.SourceRasterScale >= sourceScale - 0.01 && partial.ImageSource is not null)
        {
            return;
        }

        partial.IsDecoding = true;
        try
        {
            RenderedPartial rendered = await session.RenderPartialAsync(partial.Index,
                _lifetime?.Token ?? CancellationToken.None);
            if (generation != _generation || !partial.IsVisible ||
                !ReferenceEquals(_highResSession, session))
            {
                return;
            }

            partial.PngBytes = rendered.Png;
            partial.SourceRasterScale = sourceScale;
            partial.SetMasterBarRange(rendered.FirstMasterBarIndex, rendered.LastMasterBarIndex);
            if (partial.Index == CurrentSystem - 1)
            {
                OnPropertyChanged(nameof(CurrentMeasureText));
            }

            TouchCache(partial);
            BitmapImage image = await CreateImageAsync(partial, rendered.Png, sourceScale);
            if (generation == _generation && partial.IsVisible)
            {
                partial.ImageSource = image;
                partial.DecodedZoom = Zoom;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not render high-resolution system {SystemId}", partial.Id);
        }
        finally
        {
            partial.IsDecoding = false;
        }
    }

    private async Task DisposeHighResAsync()
    {
        _highResDebounce?.Cancel();
        _highResDebounce?.Dispose();
        _highResDebounce = null;
        _highResLifetime?.Cancel();
        _highResLifetime?.Dispose();
        _highResLifetime = null;
        _highResLoading = false;
        _highResRequestedScale = 0;
        _highResScale = 0;
        AlphaTabScoreSession? session = _highResSession;
        _highResSession = null;
        await DisposeSessionAsync(session);
    }

    private async Task DisposeSessionAsync(AlphaTabScoreSession? session)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not dispose a render session");
        }
    }

    private void TouchCache(ScorePartialViewModel partial)
    {
        LinkedListNode<ScorePartialViewModel>? existing = _cache.Find(partial);
        if (existing is not null)
        {
            _cache.Remove(existing);
        }

        _cache.AddFirst(partial);
        while (_cache.Count > 16)
        {
            ScorePartialViewModel old = _cache.Last!.Value;
            _cache.RemoveLast();
            if (!old.IsVisible)
            {
                old.PngBytes = null;
            }
        }
    }

    private void MarkRendered(string id)
    {
        if (_renderedIds.Add(id))
        {
            Status = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                _strings.GetString("RenderedCount"), _renderedIds.Count, _totalSystems);
        }
    }

    public void ReleasePartial(ScorePartialViewModel partial)
    {
        partial.IsVisible = false;
        partial.ImageSource = null;
        if (!_cache.Contains(partial))
        {
            partial.PngBytes = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await SaveCurrentViewStateAsync();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        await DisposeHighResAsync();
        await DisposeSessionAsync(_session);
    }
}
