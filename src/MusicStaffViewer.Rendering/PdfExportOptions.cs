namespace Com.Example.MusicStaffViewer.Rendering;

public sealed record PdfExportOptions
{
    public const float A4Width = 595.28f;
    public const float A4Height = 841.89f;
    public const float A3Width = 841.89f;
    public const float A3Height = 1190.55f;
    public const float LetterWidth = 612f;
    public const float LetterHeight = 792f;

    public float PageWidth { get; init; } = A4Width;
    public float PageHeight { get; init; } = A4Height;
    public float Margin { get; init; } = 40f;
    public float SystemGap { get; init; } = 14f;
}
