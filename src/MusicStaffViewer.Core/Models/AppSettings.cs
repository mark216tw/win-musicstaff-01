namespace Com.Example.MusicStaffViewer.Core.Models;

public sealed class AppSettings
{
    public DisplayMode DisplayMode { get; set; } = DisplayMode.System;
    public AccentTheme AccentTheme { get; set; } = AccentTheme.ClassicBlue;
    public PdfPageSize PdfPageSize { get; set; } = PdfPageSize.A4;
    public InitialZoomMode InitialZoomMode { get; set; } = InitialZoomMode.FitWidth;
    public double DefaultZoom { get; set; } = 1.0;
    public int DefaultMeasuresPerSystem { get; set; } = ScoreDisplayOptions.DefaultMeasuresPerSystem;
    public NoteSpacing NoteSpacing { get; set; } = NoteSpacing.Compact;
    public QualityLevel ScoreQuality { get; set; } = QualityLevel.High;
    public QualityLevel PdfQuality { get; set; } = QualityLevel.High;
    public WindowBounds? Window { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public List<string> FavoriteFiles { get; set; } = [];
    public List<ScoreViewState> ScoreViewStates { get; set; } = [];
    public string? LastScorePath { get; set; }
}
