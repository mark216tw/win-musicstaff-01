namespace Com.Example.MusicStaffViewer.Core.Models;

public sealed class ScoreViewState
{
    public string Path { get; set; } = string.Empty;
    public double Zoom { get; set; } = 1.0;
    public bool FitWidth { get; set; }
    public int TopSystem { get; set; }
    public int TopMeasure { get; set; }
    public double OffsetWithinSystem { get; set; }
    public List<int> VisibleTrackIndices { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
