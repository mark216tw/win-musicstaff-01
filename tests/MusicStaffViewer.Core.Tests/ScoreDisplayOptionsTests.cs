using Com.Example.MusicStaffViewer.Core.Models;
using Xunit;

namespace Com.Example.MusicStaffViewer.Core.Tests;

public sealed class ScoreDisplayOptionsTests
{
    [Theory]
    [InlineData(0.78, 1, 0.8)]
    [InlineData(0.80, 1, 0.85)]
    [InlineData(1.00, 1, 1.05)]
    [InlineData(0.78, -1, 0.75)]
    [InlineData(0.80, -1, 0.75)]
    [InlineData(1.00, -1, 0.95)]
    public void NextZoomAlignsToFivePercentSteps(double current, int direction, double expected) =>
        Assert.Equal(expected, ScoreDisplayOptions.NextZoom(current, direction));

    [Theory]
    [InlineData(0.1, 0.2)]
    [InlineData(0.2, 0.2)]
    [InlineData(0.5, 0.5)]
    [InlineData(4.0, 4.0)]
    [InlineData(9.0, 4.0)]
    public void ClampZoomKeepsZoomWithinRange(double zoom, double expected) =>
        Assert.Equal(expected, ScoreDisplayOptions.ClampZoom(zoom));

    [Fact]
    public void NextZoomStopsAtMaximum() =>
        Assert.Equal(ScoreDisplayOptions.MaximumZoom,
            ScoreDisplayOptions.NextZoom(ScoreDisplayOptions.MaximumZoom, 1));

    [Fact]
    public void NextZoomStopsAtMinimum() =>
        Assert.Equal(ScoreDisplayOptions.MinimumZoom,
            ScoreDisplayOptions.NextZoom(ScoreDisplayOptions.MinimumZoom, -1));

    [Theory]
    [InlineData(NoteSpacing.Compact, 1.2)]
    [InlineData(NoteSpacing.Normal, 1.414)]
    [InlineData(NoteSpacing.Loose, 1.618)]
    public void GetSpacingRatioMapsPresets(NoteSpacing spacing, double expected) =>
        Assert.Equal(expected, ScoreDisplayOptions.GetSpacingRatio(spacing), 3);

    [Theory]
    [InlineData(0.5, 1.2)]
    [InlineData(1.2, 1.2)]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.5, 2.0)]
    public void ClampSpacingRatioKeepsRatioWithinRange(double ratio, double expected) =>
        Assert.Equal(expected, ScoreDisplayOptions.ClampSpacingRatio(ratio), 6);

    [Theory]
    [InlineData(QualityLevel.Standard, 1.5, 2.5, 2.0)]
    [InlineData(QualityLevel.High, 2.0, 4.0, 3.0)]
    [InlineData(QualityLevel.Maximum, 3.0, 4.0, 4.0)]
    public void QualityLevelsMapToRasterScales(QualityLevel quality, double baseScale,
        double maximumScale, double pdfScale)
    {
        Assert.Equal(baseScale, ScoreDisplayOptions.GetBaseRasterScale(quality), 6);
        Assert.Equal(maximumScale, ScoreDisplayOptions.GetMaximumRasterScale(quality), 6);
        Assert.Equal(pdfScale, ScoreDisplayOptions.GetPdfRasterScale(quality), 6);
    }
}
