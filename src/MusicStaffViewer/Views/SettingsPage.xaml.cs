using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.Services;
using Com.Example.MusicStaffViewer.Services;
using Com.Example.MusicStaffViewer.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.Example.MusicStaffViewer.Views;

public sealed partial class SettingsPage : UserControl
{
    private readonly ScoreViewModel _scores;
    private readonly ThemeService _theme;
    private readonly UserSettingsService _settings;
    private readonly ILogger<SettingsPage> _logger;
    private readonly ResourceLoader _strings = new();
    private bool _initializing;

    public string DisplayModeName => _strings.GetString("DisplayModeAutomationName");
    public string AccentThemeName => _strings.GetString("AccentThemeAutomationName");
    public string PdfPageName => _strings.GetString("PdfPageAutomationName");
    public string InitialZoomName => _strings.GetString("InitialZoomAutomationName");
    public string DefaultZoomName => _strings.GetString("DefaultZoomAutomationName");
    public string DefaultMeasuresName => _strings.GetString("DefaultMeasuresAutomationName");
    public string NoteSpacingName => _strings.GetString("NoteSpacingAutomationName");
    public string ScoreQualityName => _strings.GetString("ScoreQualityAutomationName");
    public string PdfQualityName => _strings.GetString("PdfQualityAutomationName");
    public IReadOnlyList<int> MeasureCounts { get; } = Enumerable.Range(
        ScoreDisplayOptions.MinimumMeasuresPerSystem,
        ScoreDisplayOptions.MaximumMeasuresPerSystem - ScoreDisplayOptions.MinimumMeasuresPerSystem + 1).ToArray();

    public SettingsPage(ScoreViewModel scores, ThemeService theme, UserSettingsService settings,
        ILogger<SettingsPage> logger)
    {
        _scores = scores;
        _theme = theme;
        _settings = settings;
        _logger = logger;
        InitializeComponent();
        AutomationProperties.SetName(ClearRecentButton, _strings.GetString("ClearRecentAutomationName"));
    }

    public void BeginEdit()
    {
        _initializing = true;
        DisplayCombo.SelectedIndex = (int)_scores.Settings.DisplayMode;
        AccentCombo.SelectedIndex = (int)_scores.Settings.AccentTheme;
        PdfPageCombo.SelectedIndex = (int)_scores.Settings.PdfPageSize;
        InitialZoomCombo.SelectedIndex = (int)_scores.Settings.InitialZoomMode;
        MeasureCombo.SelectedItem = _scores.Settings.DefaultMeasuresPerSystem;
        NoteSpacingCombo.SelectedIndex = (int)_scores.Settings.NoteSpacing;
        ScoreQualityCombo.SelectedIndex = (int)_scores.Settings.ScoreQuality;
        PdfQualityCombo.SelectedIndex = (int)_scores.Settings.PdfQuality;
        ZoomSlider.Value = _scores.Settings.DefaultZoom;
        UpdateZoomState();
        SettingsError.IsOpen = false;
        _initializing = false;
    }

    public async Task<bool> ApplyAsync()
    {
        if (DisplayCombo.SelectedIndex < 0 || AccentCombo.SelectedIndex < 0 ||
            PdfPageCombo.SelectedIndex < 0 ||
            InitialZoomCombo.SelectedIndex < 0 || MeasureCombo.SelectedItem is not int measures ||
            NoteSpacingCombo.SelectedIndex < 0 || ScoreQualityCombo.SelectedIndex < 0 ||
            PdfQualityCombo.SelectedIndex < 0)
        {
            return false;
        }

        AppSettings settings = _scores.Settings;
        DisplayMode previousDisplayMode = settings.DisplayMode;
        AccentTheme previousAccentTheme = settings.AccentTheme;
        PdfPageSize previousPdfPageSize = settings.PdfPageSize;
        InitialZoomMode previousInitialZoomMode = settings.InitialZoomMode;
        double previousDefaultZoom = settings.DefaultZoom;
        int previousMeasures = settings.DefaultMeasuresPerSystem;
        NoteSpacing previousNoteSpacing = settings.NoteSpacing;
        QualityLevel previousScoreQuality = settings.ScoreQuality;
        QualityLevel previousPdfQuality = settings.PdfQuality;

        settings.DisplayMode = (DisplayMode)DisplayCombo.SelectedIndex;
        settings.AccentTheme = (AccentTheme)AccentCombo.SelectedIndex;
        settings.PdfPageSize = (PdfPageSize)PdfPageCombo.SelectedIndex;
        settings.InitialZoomMode = (InitialZoomMode)InitialZoomCombo.SelectedIndex;
        settings.DefaultZoom = ZoomSlider.Value;
        settings.DefaultMeasuresPerSystem = measures;
        settings.NoteSpacing = (NoteSpacing)NoteSpacingCombo.SelectedIndex;
        settings.ScoreQuality = (QualityLevel)ScoreQualityCombo.SelectedIndex;
        settings.PdfQuality = (QualityLevel)PdfQualityCombo.SelectedIndex;

        if (!await SaveAsync())
        {
            settings.DisplayMode = previousDisplayMode;
            settings.AccentTheme = previousAccentTheme;
            settings.PdfPageSize = previousPdfPageSize;
            settings.InitialZoomMode = previousInitialZoomMode;
            settings.DefaultZoom = previousDefaultZoom;
            settings.DefaultMeasuresPerSystem = previousMeasures;
            settings.NoteSpacing = previousNoteSpacing;
            settings.ScoreQuality = previousScoreQuality;
            settings.PdfQuality = previousPdfQuality;
            return false;
        }

        bool displayChanged = settings.DisplayMode != previousDisplayMode;
        bool noteSpacingChanged = settings.NoteSpacing != previousNoteSpacing;
        bool scoreQualityChanged = settings.ScoreQuality != previousScoreQuality;
        _theme.Apply(settings, App.MainWindow!.RootElement);
        if (displayChanged || noteSpacingChanged || scoreQualityChanged)
        {
            await _scores.ReloadAsync();
        }

        return true;
    }

    private void OnInitialZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
        {
            UpdateZoomState();
        }
    }

    private void OnDefaultZoomChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => UpdateZoomLabel();

    private void UpdateZoomState()
    {
        CustomZoomPanel.Visibility = InitialZoomCombo.SelectedIndex == (int)InitialZoomMode.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel() => ZoomValue.Text = $"{ZoomSlider.Value:P0}";

    private async void OnConfirmClearRecent(object sender, RoutedEventArgs e)
    {
        List<string> previousFiles = [.. _scores.Settings.RecentFiles];
        var previousScores = _scores.RecentScores.ToArray();
        _scores.Settings.RecentFiles.Clear();
        _scores.RecentScores.Clear();
        if (!await SaveAsync())
        {
            _scores.Settings.RecentFiles.AddRange(previousFiles);
            foreach (var score in previousScores)
            {
                _scores.RecentScores.Add(score);
            }
        }

        if (ClearRecentButton.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    private void OnCancelClearRecent(object sender, RoutedEventArgs e)
    {
        if (ClearRecentButton.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            await _settings.SaveAsync(_scores.Settings);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save preferences");
            SettingsError.Message = _strings.GetString("SettingsSaveError") + exception.Message;
            SettingsError.IsOpen = true;
            return false;
        }
    }
}
