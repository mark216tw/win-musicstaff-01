namespace Com.Example.MusicStaffViewer.Core.Models;

public static class ScoreDisplayOptions
{
    public const int MinimumMeasuresPerSystem = 4;
    public const int MaximumMeasuresPerSystem = 10;
    public const int DefaultMeasuresPerSystem = 6;
    public const double MinimumZoom = 0.2;
    public const double MaximumZoom = 4.0;
    public const double ZoomStep = 0.05;
    public const double EdgeMargin = 16.0;
    public const double MinimumSpacingRatio = 1.2;
    public const double MaximumSpacingRatio = 2.0;
    public const double CompactSpacingRatio = MinimumSpacingRatio;
    public const double LooseSpacingRatio = 1.618;
    public const double StandardBaseRasterScale = 1.5;
    public const double HighBaseRasterScale = 2.0;
    public const double MaximumBaseRasterScale = 3.0;
    public const double StandardMaximumRasterScale = 2.5;
    public const double HighMaximumRasterScale = 4.0;
    public const double MaximumMaximumRasterScale = 4.0;
    public const double StandardPdfRasterScale = 2.0;
    public const double HighPdfRasterScale = 3.0;
    public const double MaximumPdfRasterScale = 4.0;
    public static readonly double NormalSpacingRatio = Math.Sqrt(2.0);

    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinimumZoom, MaximumZoom);

    public static double GetBaseRasterScale(QualityLevel quality) => quality switch
    {
        QualityLevel.Standard => StandardBaseRasterScale,
        QualityLevel.Maximum => MaximumBaseRasterScale,
        _ => HighBaseRasterScale
    };

    public static double GetMaximumRasterScale(QualityLevel quality) => quality switch
    {
        QualityLevel.Standard => StandardMaximumRasterScale,
        QualityLevel.Maximum => MaximumMaximumRasterScale,
        _ => HighMaximumRasterScale
    };

    public static double GetPdfRasterScale(QualityLevel quality) => quality switch
    {
        QualityLevel.Standard => StandardPdfRasterScale,
        QualityLevel.Maximum => MaximumPdfRasterScale,
        _ => HighPdfRasterScale
    };

    public static double ClampSpacingRatio(double ratio) =>
        Math.Clamp(ratio, MinimumSpacingRatio, MaximumSpacingRatio);

    public static double GetSpacingRatio(NoteSpacing spacing) => spacing switch
    {
        NoteSpacing.Compact => CompactSpacingRatio,
        NoteSpacing.Loose => LooseSpacingRatio,
        _ => NormalSpacingRatio
    };

    public static double NextZoom(double current, int direction)
    {
        if (direction == 0)
        {
            return ClampZoom(current);
        }

        const double epsilon = 1e-9;
        double steps = current / ZoomStep;
        double next = direction > 0
            ? (Math.Floor(steps + epsilon) + 1) * ZoomStep
            : (Math.Ceiling(steps - epsilon) - 1) * ZoomStep;
        return ClampZoom(Math.Round(next, 2));
    }
}
