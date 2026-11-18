using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Com.Example.MusicStaffViewer.Views;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.Services;
using Com.Example.MusicStaffViewer.ViewModels;
using Com.Example.MusicStaffViewer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.Example.MusicStaffViewer;

public sealed partial class MainWindow : Window
{
    private readonly ScorePage _scorePage;
    private readonly SettingsPage _settingsPage;
    private readonly AboutPage _aboutPage;
    private readonly ScoreViewModel _scores;
    private readonly UserSettingsService _settings;
    private readonly ILogger<MainWindow> _logger;
    private readonly ThemeService _theme;
    private readonly ResourceLoader _strings = new();
    private readonly UISettings _systemSettings = App.SystemUISettings;
    private readonly DispatcherQueueTimer _systemThemeTimer;
    private WindowBounds _normalBounds = new(100, 100, 1200, 800);
    private bool _allowClose;
    private bool _dialogOpen;
    private bool _fullScreen;
    private bool _wasMaximized;
    private bool _lastSystemDark = App.IsSystemDark;
    private WindowBounds _restoreBounds = new(100, 100, 1200, 800);
    public FrameworkElement RootElement => Root;

    public MainWindow(ScorePage scorePage, SettingsPage settingsPage, AboutPage aboutPage,
        ScoreViewModel scores, UserSettingsService settings, ThemeService theme, ILogger<MainWindow> logger)
    {
        _scorePage = scorePage;
        _settingsPage = settingsPage;
        _aboutPage = aboutPage;
        _scores = scores;
        _settings = settings;
        _logger = logger;
        _theme = theme;
        InitializeComponent();
        _systemThemeTimer = DispatcherQueue.CreateTimer();
        _systemThemeTimer.Interval = TimeSpan.FromMilliseconds(300);
        _systemThemeTimer.IsRepeating = false;
        _systemThemeTimer.Tick += OnSystemThemeTimerTick;
        Title = _strings.GetString("AppTitle");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new SizeInt32(1200, 800));
        _normalBounds = new WindowBounds(AppWindow.Position.X, AppWindow.Position.Y,
            AppWindow.Size.Width, AppWindow.Size.Height);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 600;
        }

        AppWindow.Changed += OnWindowChanged;
        AppWindow.Closing += OnClosing;
        _systemSettings.ColorValuesChanged += OnSystemColorsChanged;
        Closed += (_, _) => _systemSettings.ColorValuesChanged -= OnSystemColorsChanged;
        PageHost.Content = _scorePage;
        RebuildRecentMenu();
        UpdateCommandStates();
        _scores.RecentScores.CollectionChanged += OnRecentScoresChanged;
        _scores.PropertyChanged += OnScorePropertyChanged;

        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        var fullScreenAccelerator = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F11
        };
        fullScreenAccelerator.Invoked += OnFullScreenShortcut;
        Root.KeyboardAccelerators.Add(fullScreenAccelerator);
    }

    public void RestoreWindow(WindowBounds? saved)
    {
        if (saved is null)
        {
            return;
        }

        DisplayArea display = DisplayArea.GetFromPoint(new PointInt32(saved.X, saved.Y), DisplayAreaFallback.Nearest);
        RectInt32 work = display.WorkArea;
        WindowBounds bounds = WindowStateService.Clamp(saved, new WorkArea(work.X, work.Y, work.Width, work.Height));
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        _normalBounds = bounds with { IsMaximized = false };
        if (saved.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void OnRecentScoresChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        DispatcherQueue.TryEnqueue(RebuildRecentMenu);

    private void OnScorePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ScoreViewModel.HasScore)
            or nameof(ScoreViewModel.IsLoading)
            or nameof(ScoreViewModel.IsExporting))
        {
            UpdateCommandStates();
        }
    }

    private void UpdateCommandStates()
    {
        bool ready = _scores.HasScore && !_scores.IsLoading;
        ExportPdfMenuItem.IsEnabled = ready && !_scores.IsExporting;
        ReloadMenuItem.IsEnabled = ready && !_scores.IsExporting;
        CloseScoreMenuItem.IsEnabled = ready && !_scores.IsExporting;
    }

    private void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (_scores.RecentScores.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem
            {
                Text = _strings.GetString("NoRecentMenu"),
                IsEnabled = false
            });
            return;
        }

        foreach (ScoreDocument score in _scores.RecentScores)
        {
            var item = new MenuFlyoutItem
            {
                Text = score.Title,
                Tag = score.Path
            };
            ToolTipService.SetToolTip(item, score.Path);
            item.Click += OnRecentScoreClicked;
            RecentMenu.Items.Add(item);
        }
    }

    private async Task GuardAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{Message}", message);
        }
    }

    private async void OnRecentScoreClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string path })
        {
            await GuardAsync(() => _scores.OpenAsync(path), "Could not open the recent score");
        }
    }

    private async void OnOpenScore(object sender, RoutedEventArgs e) =>
        await GuardAsync(_scorePage.OpenFromPickerAsync, "Could not open the score picker");

    private async void OnReloadScore(object sender, RoutedEventArgs e) =>
        await GuardAsync(_scores.ReloadAsync, "Could not reload the score");

    private async void OnCloseScore(object sender, RoutedEventArgs e) =>
        await GuardAsync(_scores.CloseScoreAsync, "Could not close the score");

    private async void OnExportPdf(object sender, RoutedEventArgs e) =>
        await GuardAsync(_scorePage.ExportPdfAsync, "Could not export the score as PDF");

    private async void OnPreferences(object sender, RoutedEventArgs e) =>
        await GuardAsync(ShowPreferencesAsync, "Could not show preferences");

    private void OnToggleFullScreen(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void OnFullScreenShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SetFullScreen(!_fullScreen);
    }

    private void SetFullScreen(bool enabled)
    {
        if (enabled == _fullScreen || _dialogOpen)
        {
            return;
        }

        _fullScreen = enabled;
        if (enabled)
        {
            _wasMaximized = AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Maximized
            };
            _restoreBounds = _normalBounds;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_wasMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            else
            {
                AppWindow.MoveAndResize(new RectInt32(_restoreBounds.X, _restoreBounds.Y,
                    _restoreBounds.Width, _restoreBounds.Height));
                _normalBounds = _restoreBounds with { IsMaximized = false };
            }
        }

        MenuBarBorder.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _scorePage.SetFocusMode(enabled);
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_fullScreen && !_dialogOpen && args.Key == Windows.System.VirtualKey.Escape)
        {
            args.Handled = true;
            SetFullScreen(false);
            return;
        }

        CoreVirtualKeyStates control = InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control);
        bool isControlDown = (control & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (isControlDown && !_dialogOpen && !_scorePage.IsNavigationDialogOpen && _scores.HasScore)
        {
            const int gKey = 71;
            if ((int)args.Key == gKey || (int)args.OriginalKey == gKey)
            {
                args.Handled = true;
                await GuardAsync(_scorePage.ShowGoToMeasureAsync, "Could not show the go-to-measure dialog");
                return;
            }

            if (args.Key == Windows.System.VirtualKey.PageDown ||
                args.OriginalKey == Windows.System.VirtualKey.PageDown)
            {
                args.Handled = true;
                _scorePage.GoToNextSystem();
                return;
            }

            if (args.Key == Windows.System.VirtualKey.PageUp ||
                args.OriginalKey == Windows.System.VirtualKey.PageUp)
            {
                args.Handled = true;
                _scorePage.GoToPreviousSystem();
                return;
            }
        }

        const int commaKey = 188;
        if ((int)args.Key != commaKey && (int)args.OriginalKey != commaKey)
        {
            return;
        }

        if (!isControlDown)
        {
            return;
        }

        args.Handled = true;
        await GuardAsync(ShowPreferencesAsync, "Could not show preferences");
    }

    private async void OnAbout(object sender, RoutedEventArgs e) =>
        await GuardAsync(ShowAboutAsync, "Could not show the about dialog");

    private async Task ShowPreferencesAsync()
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        ContentDialog? dialog = null;
        try
        {
            _settingsPage.BeginEdit();
            dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = _strings.GetString("PreferencesDialogTitle"),
                Content = _settingsPage,
                PrimaryButtonText = _strings.GetString("ApplyDialog"),
                CloseButtonText = _strings.GetString("CancelDialog"),
                DefaultButton = ContentDialogButton.Primary,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    args.Cancel = !await _settingsPage.ApplyAsync();
                }
                catch (Exception exception)
                {
                    args.Cancel = true;
                    _logger.LogError(exception, "Could not apply preferences");
                }
                finally
                {
                    deferral.Complete();
                }
            };
            await dialog.ShowAsync();
        }
        finally
        {
            if (dialog is not null)
            {
                dialog.Content = null;
            }

            _dialogOpen = false;
        }
    }

    private async Task ShowAboutAsync()
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        ContentDialog? dialog = null;
        try
        {
            dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Content = _aboutPage,
                CloseButtonText = _strings.GetString("CloseDialog"),
                DefaultButton = ContentDialogButton.Close,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            await dialog.ShowAsync();
        }
        finally
        {
            if (dialog is not null)
            {
                dialog.Content = null;
            }

            _dialogOpen = false;
        }
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _normalBounds = new WindowBounds(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        }
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        Root.DispatcherQueue.TryEnqueue(() =>
        {
            if (_scores.Settings.DisplayMode != DisplayMode.System)
            {
                return;
            }

            _systemThemeTimer.Stop();
            _systemThemeTimer.Start();
        });
    }

    private async void OnSystemThemeTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_scores.Settings.DisplayMode != DisplayMode.System)
        {
            return;
        }

        bool dark = App.IsSystemDark;
        if (dark == _lastSystemDark)
        {
            return;
        }

        try
        {
            _theme.Apply(_scores.Settings, Root);
            _lastSystemDark = dark;
            await _scores.ReloadAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not apply system color change");
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        try
        {
            await _scores.SaveCurrentViewStateAsync();
            bool maximized = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            _scores.Settings.Window = _normalBounds with { IsMaximized = maximized };
            await _settings.SaveAsync(_scores.Settings);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save window state");
        }

        _allowClose = true;
        Close();
    }
}
