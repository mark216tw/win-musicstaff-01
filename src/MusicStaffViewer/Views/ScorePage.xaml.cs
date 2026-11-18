using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Com.Example.MusicStaffViewer.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using WinRT.Interop;

namespace Com.Example.MusicStaffViewer.Views;

public sealed partial class ScorePage : UserControl
{
    private const double ExpandedSidebarWidth = 280;
    private const double CollapsedSidebarWidth = 44;
    private const string CollapseGlyph = "\uE76B";
    private const string ExpandGlyph = "\uE76C";
    private const string OutlineStarGlyph = "\uE734";
    private const string FilledStarGlyph = "\uE735";
    private bool _initialized;
    private bool _exporting;
    private bool _updatingMeasures;
    private bool _openingFavorite;
    private bool _suppressFavoriteSelection;
    private bool _fitWidthPending;
    private bool _restoreFitWidth;
    private int _scrollRestoreVersion;
    private (double X, double Y)? _pendingScrollRestore;
    private bool _isPanning;
    private bool _sidebarVisible = true;
    private StorageFile? _lastExportedPdf;
    private uint _panPointerId;
    private double _panStartX;
    private double _panStartY;
    private double _panStartOffset;
    private double _panStartVerticalOffset;
    private int _highestPrepared = -1;
    private readonly DispatcherQueueTimer _fitWidthTimer;
    private readonly DispatcherQueueTimer _viewStateTimer;
    private bool _updatingTracks;
    private bool _navigationDialogOpen;
    private static readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader SharedStrings = new();
    public ScoreViewModel ViewModel { get; }
    public IReadOnlyList<int> MeasureCounts { get; } = Enumerable.Range(
        ScoreDisplayOptions.MinimumMeasuresPerSystem,
        ScoreDisplayOptions.MaximumMeasuresPerSystem - ScoreDisplayOptions.MinimumMeasuresPerSystem + 1).ToArray();

    public ScorePage(ScoreViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _fitWidthTimer = DispatcherQueue.CreateTimer();
        _fitWidthTimer.Interval = TimeSpan.FromMilliseconds(120);
        _fitWidthTimer.IsRepeating = false;
        _fitWidthTimer.Tick += OnFitWidthTimerTick;
        _viewStateTimer = DispatcherQueue.CreateTimer();
        _viewStateTimer.Interval = TimeSpan.FromSeconds(1);
        _viewStateTimer.IsRepeating = false;
        _viewStateTimer.Tick += OnViewStateTimerTick;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.FavoriteScores.CollectionChanged += OnFavoriteScoresChanged;
        ViewModel.FavoritesChanging += OnFavoritesChanging;
        ViewModel.FavoritesChanged += OnFavoritesChanged;
        ViewModel.Tracks.CollectionChanged += OnTracksChanged;
        ViewModel.Sections.CollectionChanged += OnSectionsChanged;
        ViewModel.ScoreLoading += OnScoreLoading;
        ViewModel.ScoreReloading += OnScoreReloading;
        ViewModel.ScoreLoaded += fitWidth =>
        {
            _highestPrepared = -1;
            _fitWidthPending = fitWidth || _restoreFitWidth;
            _restoreFitWidth = false;
            SyncMeasureSelection();
            if (_fitWidthPending)
            {
                DispatcherQueue.TryEnqueue(TryApplyPendingFitWidth);
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => _ = RestoreResumeTargetAsync());
            }

            if (_pendingScrollRestore is not null)
            {
                DispatcherQueue.TryEnqueue(RestorePendingScroll);
            }
        };
        AutomationProperties.SetName(ZoomOutButton, Strings("ZoomOutName"));
        AutomationProperties.SetName(ZoomInButton, Strings("ZoomInName"));
        AutomationProperties.SetName(FitButton, Strings("FitName"));
        AutomationProperties.SetName(PartsButton, Strings("PartsButtonName"));
        AutomationProperties.SetName(OutlineButton, Strings("OutlineButtonName"));
        AutomationProperties.SetName(MeasureCombo, Strings("MeasuresPerSystemAutomationName"));
        AutomationProperties.SetName(EmptyOpenButton, Strings("OpenName"));
        AutomationProperties.SetName(SidebarToggleButton, Strings("SidebarToggleName"));
        ToolTipService.SetToolTip(ZoomOutButton, $"{Strings("ZoomOutName")} (Ctrl+-)");
        ToolTipService.SetToolTip(ZoomInButton, $"{Strings("ZoomInName")} (Ctrl+=)");
        ToolTipService.SetToolTip(FitButton, Strings("FitName"));
        ToolTipService.SetToolTip(PartsButton, Strings("PartsButtonName"));
        ToolTipService.SetToolTip(OutlineButton, Strings("OutlineButtonName"));
        ToolTipService.SetToolTip(MeasureCombo, Strings("MeasuresPerSystemHint"));
        ToolTipService.SetToolTip(SidebarToggleButton, Strings("SidebarToggleName"));
        ExportOpenButton.Content = Strings("ExportPdfOpen");
        AutomationProperties.SetName(ExportOpenButton, Strings("ExportPdfOpenName"));
        ToolTipService.SetToolTip(ExportOpenButton, Strings("ExportPdfOpenName"));
        UpdateFavoriteToggleVisuals();
        ApplySidebarState();
        var plus = new KeyboardAccelerator
        {
            Key = (Windows.System.VirtualKey)187,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        plus.Invoked += OnZoomInShortcut;
        KeyboardAccelerators.Add(plus);
        var minus = new KeyboardAccelerator
        {
            Key = (Windows.System.VirtualKey)189,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        minus.Invoked += OnZoomOutShortcut;
        KeyboardAccelerators.Add(minus);
    }

    private static string Strings(string key) => SharedStrings.GetString(key);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        string? startupPath = App.StartupPath;
        App.StartupPath = null;
        await ViewModel.InitializeAsync(startupPath);
        SyncMeasureSelection();
        UpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The view remains in the navigation cache while settings are open.
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScoreViewModel.HasScore)
            or nameof(ScoreViewModel.IsLoading)
            or nameof(ScoreViewModel.IsExporting)
            or nameof(ScoreViewModel.IsCurrentFavorite)
            or nameof(ScoreViewModel.ErrorMessage))
        {
            UpdateState();
        }
    }

    private void OnFavoriteScoresChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateFavoriteEmptyHint();

    private void OnFavoritesChanging() => _suppressFavoriteSelection = true;

    private void OnFavoritesChanged() =>
        DispatcherQueue.TryEnqueue(() => _suppressFavoriteSelection = false);

    private void OnTracksChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateState();

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        LoadingState.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !ViewModel.IsLoading && !ViewModel.HasScore ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.IsOpen = ViewModel.ErrorMessage is not null;
        bool enabled = ViewModel.HasScore && !ViewModel.IsLoading;
        MeasureCombo.IsEnabled = enabled && !ViewModel.IsExporting;
        ZoomOutButton.IsEnabled = enabled;
        ZoomInButton.IsEnabled = enabled;
        FitButton.IsEnabled = enabled;
        PartsButton.IsEnabled = enabled;
        OutlineButton.IsEnabled = enabled && ViewModel.Sections.Count > 0;
        FavoriteToggle.IsEnabled = enabled;
        UpdateFavoriteToggleVisuals();
        UpdateFavoriteEmptyHint();
    }

    private void UpdateFavoriteToggleVisuals()
    {
        FavoriteToggleIcon.Glyph = ViewModel.IsCurrentFavorite ? FilledStarGlyph : OutlineStarGlyph;
        string name = Strings(ViewModel.IsCurrentFavorite ? "RemoveFavoriteTooltip" : "AddFavoriteTooltip");
        AutomationProperties.SetName(FavoriteToggle, name);
        ToolTipService.SetToolTip(FavoriteToggle, name);
    }

    private void UpdateFavoriteEmptyHint() =>
        FavoriteEmptyHint.Visibility = _sidebarVisible && ViewModel.FavoriteScores.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnErrorBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.ClearError();

    private async void OnFavoriteToggleClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasScore)
        {
            return;
        }

        try
        {
            await ViewModel.ToggleFavoriteAsync();
        }
        catch (Exception exception)
        {
            ViewModel.ReportFavoriteError(exception);
        }
        finally
        {
            UpdateFavoriteToggleVisuals();
        }
    }

    private async void OnFavoriteSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_openingFavorite || _suppressFavoriteSelection ||
            e.AddedItems.FirstOrDefault() is not ScoreDocument score ||
            string.Equals(score.Path, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool restoreKeyboardFocus =
            FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { FocusState: FocusState.Keyboard };
        _openingFavorite = true;
        try
        {
            await ViewModel.OpenAsync(score.Path);
        }
        finally
        {
            RestoreFavoriteSelection(score.Path, restoreKeyboardFocus);
            _openingFavorite = false;
        }
    }

    private void RestoreFavoriteSelection(string path, bool focusWithKeyboard)
    {
        ScoreDocument? score = ViewModel.FavoriteScores.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        if (score is null)
        {
            FavoriteList.SelectedItem = null;
            return;
        }

        FavoriteList.SelectedItem = score;
        FavoriteList.ScrollIntoView(score);
        if (!focusWithKeyboard)
        {
            return;
        }

        if (FavoriteList.ContainerFromItem(score) is ListViewItem container)
        {
            container.Focus(FocusState.Keyboard);
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (FavoriteList.ContainerFromItem(score) is ListViewItem realized)
            {
                realized.Focus(FocusState.Keyboard);
            }
        });
    }

    private void OnFavoriteRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScoreDocument score)
        {
            return;
        }

        var removeItem = new MenuFlyoutItem
        {
            Text = Strings("RemoveFavoriteText"),
            Tag = score
        };
        removeItem.Click += OnRemoveFavoriteClicked;
        var flyout = new MenuFlyout();
        flyout.Items.Add(removeItem);
        flyout.ShowAt(element, e.GetPosition(element));
        e.Handled = true;
    }

    private async void OnRemoveFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ScoreDocument score })
        {
            return;
        }

        try
        {
            await ViewModel.RemoveFavoriteAsync(score);
        }
        catch (Exception exception)
        {
            ViewModel.ReportFavoriteError(exception);
        }
    }

    private async void OnTrackClicked(object sender, RoutedEventArgs e)
    {
        if (_updatingTracks || !ViewModel.HasScore)
        {
            return;
        }

        _updatingTracks = true;
        try
        {
            await Task.Yield();
            if (sender is CheckBox { Tag: int trackIndex } checkbox)
            {
                ScoreTrackViewModel? track = ViewModel.Tracks
                    .FirstOrDefault(candidate => candidate.Index == trackIndex);
                if (track is not null)
                {
                    track.IsSelected = checkbox.IsChecked == true;
                }
            }

            List<int> selected = ViewModel.Tracks
                .Where(track => track.IsSelected).Select(track => track.Index).ToList();
            if (selected.Count == 0)
            {
                if (sender is CheckBox restoreCheckbox && restoreCheckbox.Tag is int restoreIndex)
                {
                    ScoreTrackViewModel? model = ViewModel.Tracks
                        .FirstOrDefault(candidate => candidate.Index == restoreIndex);
                    if (model is not null)
                    {
                        model.IsSelected = true;
                    }

                    restoreCheckbox.IsChecked = true;
                }

                return;
            }

            await ViewModel.SetTrackVisibilityAsync(selected);
        }
        finally
        {
            _updatingTracks = false;
        }
    }

    private async void OnShowAllPartsClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasScore)
        {
            return;
        }

        _updatingTracks = true;
        try
        {
            await ViewModel.ShowAllTracksAsync();
        }
        finally
        {
            _updatingTracks = false;
        }
    }

    private async void OnSectionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ScoreSectionViewModel section)
        {
            return;
        }

        SectionList.SelectedItem = null;
        OutlineButton.Flyout?.Hide();
        await NavigateToMeasureAsync(section.MasterBarIndex + 1);
    }

    private async void OnOpenClicked(object sender, RoutedEventArgs e) => await OpenFromPickerAsync();

    public async Task OpenFromPickerAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".musicxml");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".mxl");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await ViewModel.OpenAsync(file.Path);
            }
        }
        catch (Exception exception)
        {
            ViewModel.ReportOpenError(exception);
        }
    }

    public async Task ExportPdfAsync()
    {
        if (_exporting)
        {
            return;
        }

        if (!ViewModel.HasScore)
        {
            ViewModel.ReportExportError(new InvalidOperationException(Strings("ExportPdfNoScore")));
            return;
        }

        _exporting = true;
        ExportBar.IsOpen = false;
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SuggestFileName(ViewModel.ScoreTitle)
            };
            picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            string temporary = file.Path + ".tmp";
            try
            {
                using (Stream output = File.Create(temporary))
                {
                    await ViewModel.ExportPdfAsync(output);
                }

                File.Move(temporary, file.Path, overwrite: true);
                _lastExportedPdf = file;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            ExportBar.Message = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                Strings("ExportPdfSuccess"), file.Name);
            ExportBar.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ViewModel.ReportExportError(exception);
        }
        finally
        {
            _exporting = false;
        }
    }

    private async void OnExportOpenClicked(object sender, RoutedEventArgs e)
    {
        StorageFile? file = _lastExportedPdf;
        ExportBar.IsOpen = false;
        if (file is null || !File.Exists(file.Path))
        {
            ViewModel.ReportExportOpenError(
                new InvalidOperationException(Strings("ExportPdfOpenMissing")));
            return;
        }

        try
        {
            await Task.Run(() => Process.Start(new ProcessStartInfo(file.Path)
            {
                UseShellExecute = true
            }));
        }
        catch (Exception exception)
        {
            ViewModel.ReportExportOpenError(exception);
        }
    }

    private static string SuggestFileName(string title)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = string.Concat(title.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(name) ? "score" : name;
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e) =>
        ZoomCentered(() => ViewModel.ZoomOutCommand.Execute(null));
    private void OnZoomInClicked(object sender, RoutedEventArgs e) =>
        ZoomCentered(() => ViewModel.ZoomInCommand.Execute(null));
    private void OnFitWidthClicked(object sender, RoutedEventArgs e) =>
        ZoomCentered(() => ViewModel.FitWidth(GetAvailableViewportWidth()));

    private double GetAvailableViewportWidth()
    {
        double width = ScoreScrollViewer.ViewportWidth;
        return width > 0 ? width : ScoreScrollViewer.ActualWidth;
    }

    private void ZoomCentered(Action changeZoom)
    {
        if (!ViewModel.HasScore)
        {
            return;
        }

        double oldZoom = ViewModel.Zoom;
        double viewportWidth = ScoreScrollViewer.ViewportWidth;
        double viewportHeight = ScoreScrollViewer.ViewportHeight;
        double centerX = ScoreScrollViewer.HorizontalOffset + viewportWidth / 2;
        double centerY = ScoreScrollViewer.VerticalOffset + viewportHeight / 2;
        changeZoom();
        double ratio = ViewModel.Zoom / oldZoom;
        if (Math.Abs(ratio - 1) < 0.0005 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ScoreScrollViewer.UpdateLayout();
            double targetX = Math.Clamp(centerX * ratio - viewportWidth / 2,
                0, ScoreScrollViewer.ScrollableWidth);
            double targetY = Math.Clamp(centerY * ratio - viewportHeight / 2,
                0, ScoreScrollViewer.ScrollableHeight);
            ScoreScrollViewer.ChangeView(targetX, targetY, null, disableAnimation: true);
        });
    }

    private void OnScoreViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitWidthPending)
        {
            TryApplyPendingFitWidth();
            return;
        }

        if (ViewModel.IsFitWidth && ViewModel.HasScore && GetAvailableViewportWidth() > 0)
        {
            _fitWidthTimer.Stop();
            _fitWidthTimer.Start();
        }
    }

    private void OnFitWidthTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel.IsFitWidth && ViewModel.HasScore && GetAvailableViewportWidth() > 0)
        {
            ViewModel.FitWidth(GetAvailableViewportWidth());
        }
    }

    private void TryApplyPendingFitWidth()
    {
        if (!_fitWidthPending || !ViewModel.HasScore || GetAvailableViewportWidth() <= 0)
        {
            return;
        }

        ViewModel.FitWidth(GetAvailableViewportWidth());
        _fitWidthPending = false;
        _ = RestoreResumeTargetAsync();
    }

    private async void OnViewStateTimerTick(DispatcherQueueTimer sender, object args) =>
        await ViewModel.SaveViewStateAsync(ScoreScrollViewer.VerticalOffset);

    private void OnScoreLoading(bool preserveZoom)
    {
        if (preserveZoom)
        {
            return;
        }

        _viewStateTimer.Stop();
        _scrollRestoreVersion++;
        _pendingScrollRestore = null;
        _restoreFitWidth = false;
        _fitWidthPending = false;
    }

    private void OnScoreReloading()
    {
        _restoreFitWidth = ViewModel.IsFitWidth;
        _scrollRestoreVersion++;
        _pendingScrollRestore = ViewModel.HasScore
            ? (ScoreScrollViewer.HorizontalOffset, ScoreScrollViewer.VerticalOffset)
            : null;
    }

    private async void RestorePendingScroll()
    {
        if (_pendingScrollRestore is not (double x, double y))
        {
            return;
        }

        _pendingScrollRestore = null;
        if (!ViewModel.HasScore)
        {
            return;
        }

        int version = _scrollRestoreVersion;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (version != _scrollRestoreVersion)
            {
                return;
            }

            ScoreScrollViewer.UpdateLayout();
            double targetX = Math.Min(x, ScoreScrollViewer.ScrollableWidth);
            double targetY = Math.Min(y, ScoreScrollViewer.ScrollableHeight);
            ScoreScrollViewer.ChangeView(targetX, targetY, null, disableAnimation: true);
            if (Math.Abs(ScoreScrollViewer.VerticalOffset - targetY) < 1)
            {
                return;
            }

            await Task.Delay(120);
        }
    }

    private void OnScoreViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        ViewModel.UpdateCurrentSystem(ScoreScrollViewer.VerticalOffset);
        if (ViewModel.HasScore)
        {
            _viewStateTimer.Stop();
            _viewStateTimer.Start();
        }
    }

    private async Task RestoreResumeTargetAsync()
    {
        if (!ViewModel.TryConsumeResumeTarget(out int system, out double offsetWithinSystem))
        {
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!ViewModel.HasScore)
            {
                return;
            }

            ScoreScrollViewer.UpdateLayout();
            double target = ViewModel.GetSystemOffset(system) + (offsetWithinSystem * ViewModel.Zoom);
            target = Math.Clamp(target, 0, Math.Max(0, ScoreScrollViewer.ScrollableHeight));
            ScoreScrollViewer.ChangeView(null, target, null, disableAnimation: true);
            ViewModel.UpdateCurrentSystem(target);
            if (Math.Abs(ScoreScrollViewer.VerticalOffset - target) < 1)
            {
                return;
            }

            await Task.Delay(120);
        }
    }

    public bool IsNavigationDialogOpen => _navigationDialogOpen;

    private void ScrollToSystem(int systemIndex)
    {
        if (!ViewModel.HasScore)
        {
            return;
        }

        int target = Math.Clamp(systemIndex, 1, Math.Max(1, ViewModel.Partials.Count));
        ScoreScrollViewer.UpdateLayout();
        double offset = Math.Clamp(ViewModel.GetSystemOffset(target),
            0, Math.Max(0, ScoreScrollViewer.ScrollableHeight));
        ScoreScrollViewer.ChangeView(null, offset, null, disableAnimation: false);
        ViewModel.UpdateCurrentSystem(offset);
    }

    private async Task NavigateToMeasureAsync(int measure)
    {
        if (!ViewModel.HasScore)
        {
            return;
        }

        int system = ViewModel.ResolveSystemForMeasure(measure);
        ScrollToSystem(system);
        await Task.Delay(180);
        int resolved = ViewModel.ResolveSystemForMeasure(measure);
        if (resolved != system)
        {
            ScrollToSystem(resolved);
        }
    }

    public void GoToNextSystem() => ScrollToSystem(ViewModel.CurrentSystem + 1);

    public void GoToPreviousSystem() => ScrollToSystem(ViewModel.CurrentSystem - 1);

    public async Task ShowGoToMeasureAsync()
    {
        if (!ViewModel.HasScore || _navigationDialogOpen)
        {
            return;
        }

        _navigationDialogOpen = true;
        try
        {
            int maximum = Math.Max(1, ViewModel.MasterBarCount);
            var numberBox = new NumberBox
            {
                Header = Strings("GoToMeasureLabel"),
                Minimum = 1,
                Maximum = maximum,
                Value = Math.Clamp(ViewModel.CurrentMeasure, 1, maximum),
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(numberBox);
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    Strings("GoToMeasureHint"), maximum),
                TextWrapping = TextWrapping.Wrap
            });
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Strings("GoToMeasureDialogTitle"),
                Content = panel,
                PrimaryButtonText = Strings("GoToMeasureButton"),
                CloseButtonText = Strings("CancelDialog"),
                DefaultButton = ContentDialogButton.Primary
            };
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (double.IsNaN(numberBox.Value))
            {
                return;
            }

            await NavigateToMeasureAsync((int)Math.Round(numberBox.Value));
        }
        finally
        {
            _navigationDialogOpen = false;
        }
    }

    private void OnScorePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        CoreVirtualKeyStates control = InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control);
        if ((control & CoreVirtualKeyStates.Down) != CoreVirtualKeyStates.Down || !ViewModel.HasScore)
        {
            return;
        }

        int delta = e.GetCurrentPoint(ScoreScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        double current = ViewModel.Zoom;
        double target = ScoreDisplayOptions.NextZoom(current, delta > 0 ? 1 : -1);
        e.Handled = true;
        if (Math.Abs(target - current) < 0.0005)
        {
            return;
        }

        ZoomCentered(() => ViewModel.ZoomBy(target - current));
    }

    public void SetFocusMode(bool enabled)
    {
        ToolbarPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        StatusPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (enabled)
        {
            Sidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
        }
        else
        {
            Sidebar.Visibility = Visibility.Visible;
            ApplySidebarState();
        }
    }

    private void OnToggleSidebarClicked(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        ApplySidebarState();
    }

    private void ApplySidebarState()
    {
        SidebarColumn.Width = new GridLength(_sidebarVisible ? ExpandedSidebarWidth : CollapsedSidebarWidth);
        Sidebar.Padding = _sidebarVisible ? new Thickness(12) : new Thickness(6);
        SidebarHeader.ColumnSpacing = _sidebarVisible ? 8 : 0;
        SidebarTitle.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        FavoriteList.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarToggleIcon.Glyph = _sidebarVisible ? CollapseGlyph : ExpandGlyph;
        UpdateFavoriteEmptyHint();
    }

    public Task OpenPathAsync(string path) => ViewModel.OpenFromExternalAsync(path);

    private async void OnMeasuresPerSystemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMeasures || !ViewModel.HasScore || MeasureCombo.SelectedItem is not int measures)
        {
            return;
        }

        await ViewModel.SetMeasuresPerSystemAsync(measures);
        SyncMeasureSelection();
    }

    private void SyncMeasureSelection()
    {
        _updatingMeasures = true;
        MeasureCombo.SelectedItem = ViewModel.MeasuresPerSystem;
        _updatingMeasures = false;
    }

    private void OnScorePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(ScoreScrollViewer);
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            !point.Properties.IsLeftButtonPressed ||
            (ScoreScrollViewer.ScrollableWidth <= 0 && ScoreScrollViewer.ScrollableHeight <= 0))
        {
            return;
        }

        _isPanning = ScoreRepeater.CapturePointer(e.Pointer);
        if (!_isPanning)
        {
            return;
        }

        _panPointerId = e.Pointer.PointerId;
        _panStartX = point.Position.X;
        _panStartY = point.Position.Y;
        _panStartOffset = ScoreScrollViewer.HorizontalOffset;
        _panStartVerticalOffset = ScoreScrollViewer.VerticalOffset;
        e.Handled = true;
    }

    private void OnScorePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || e.Pointer.PointerId != _panPointerId)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(ScoreScrollViewer);
        double? targetX = ScoreScrollViewer.ScrollableWidth > 0
            ? Math.Clamp(_panStartOffset - (point.Position.X - _panStartX),
                0, ScoreScrollViewer.ScrollableWidth)
            : null;
        double? targetY = ScoreScrollViewer.ScrollableHeight > 0
            ? Math.Clamp(_panStartVerticalOffset - (point.Position.Y - _panStartY),
                0, ScoreScrollViewer.ScrollableHeight)
            : null;
        ScoreScrollViewer.ChangeView(targetX, targetY, null, disableAnimation: true);
        e.Handled = true;
    }

    private void OnScorePointerReleased(object sender, PointerRoutedEventArgs e) => EndPanning(e);

    private void OnScorePointerCanceled(object sender, PointerRoutedEventArgs e) => EndPanning(e);

    private void OnScorePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _panPointerId)
        {
            _isPanning = false;
        }
    }

    private void EndPanning(PointerRoutedEventArgs e)
    {
        if (!_isPanning || e.Pointer.PointerId != _panPointerId)
        {
            return;
        }

        _isPanning = false;
        ScoreRepeater.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private async void OnOpenShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenFromPickerAsync();
    }

    private void OnZoomInShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ZoomCentered(() => ViewModel.ZoomInCommand.Execute(null));
    }

    private void OnZoomOutShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ZoomCentered(() => ViewModel.ZoomOutCommand.Execute(null));
    }

    private void OnResetZoomShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ZoomCentered(() => ViewModel.ResetZoomCommand.Execute(null));
    }

    private async void OnReloadShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.ReloadAsync();
    }

    private async void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Index >= 0 && args.Index < ViewModel.Partials.Count && args.Element is FrameworkElement element)
        {
            ScorePartialViewModel partial = ViewModel.Partials[args.Index];
            element.Tag = partial;
            _highestPrepared = Math.Max(_highestPrepared, args.Index);
            await ViewModel.RequestPartialAsync(partial);
            if (args.Index == _highestPrepared)
            {
                await ViewModel.PrefetchPartialAsync(args.Index + 1);
            }
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is FrameworkElement { Tag: ScorePartialViewModel partial })
        {
            ViewModel.ReleasePartial(partial);
            ((FrameworkElement)args.Element).Tag = null;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool supported = e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation = supported ? DataPackageOperation.Copy : DataPackageOperation.None;
        DropOverlay.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            if (items.FirstOrDefault() is not StorageFile file)
            {
                return;
            }

            if (ScoreFileReader.SupportedExtensions.Contains(Path.GetExtension(file.Path),
                StringComparer.OrdinalIgnoreCase))
            {
                await ViewModel.OpenAsync(file.Path);
            }
            else
            {
                ViewModel.ReportUnsupportedFile(file.Name);
            }
        }
        catch (Exception exception)
        {
            ViewModel.ReportOpenError(exception);
        }
    }
}
