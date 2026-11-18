using SkiaSharp;

namespace Com.Example.MusicStaffViewer.Rendering;

public static class PdfExporter
{
    public static void Write(Stream output, string title, string? composer, IReadOnlyList<byte[]> systemPngs) =>
        Write(output, title, composer,
            systemPngs.Select(png => new PdfSystem(png, 1, 1)).ToArray(), new PdfExportOptions());

    public static void Write(Stream output, string title, string? composer,
        IReadOnlyList<PdfSystem> systems, PdfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(systems);
        if (systems.Count == 0)
        {
            throw new InvalidOperationException("The score contains no printable systems.");
        }

        PdfExportOptions effectiveOptions = options ?? new PdfExportOptions();
        double maxWidth = Math.Max(1, systems.Max(system => system.Width));
        using SKDocument document = CreateDocument(output, title, composer);
        var writer = new PageWriter(document, effectiveOptions);
        try
        {
            foreach (PdfSystem system in systems)
            {
                using SKBitmap bitmap = Decode(system.Png);
                writer.Draw(bitmap, Math.Max(1, system.Width), maxWidth);
            }
        }
        finally
        {
            writer.EndPage();
            document.Close();
        }
    }

    public static async Task WriteAsync(Stream output, string title, string? composer, int systemCount,
        Func<int, CancellationToken, Task<byte[]>> loadPngAsync, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(loadPngAsync);
        if (systemCount <= 0)
        {
            throw new InvalidOperationException("The score contains no printable systems.");
        }

        using SKDocument document = CreateDocument(output, title, composer);
        var writer = new PageWriter(document, new PdfExportOptions());
        try
        {
            for (int index = 0; index < systemCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] png = await loadPngAsync(index, cancellationToken).ConfigureAwait(false);
                using SKBitmap bitmap = Decode(png);
                writer.Draw(bitmap, 1, 1);
                progress?.Report(index + 1);
            }
        }
        finally
        {
            writer.EndPage();
            document.Close();
        }
    }

    public static async Task WriteSystemsAsync(Stream output, string title, string? composer,
        int systemCount, double maxLogicalWidth,
        Func<int, CancellationToken, Task<PdfSystem>> loadSystemAsync, PdfSystem? header = null,
        PdfExportOptions? options = null, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(loadSystemAsync);
        if (systemCount <= 0)
        {
            throw new InvalidOperationException("The score contains no printable systems.");
        }

        PdfExportOptions effectiveOptions = options ?? new PdfExportOptions();
        double maxWidth = Math.Max(1, maxLogicalWidth);
        using SKDocument document = CreateDocument(output, title, composer);
        var writer = new PageWriter(document, effectiveOptions);
        try
        {
            if (header is not null)
            {
                using SKBitmap headerBitmap = Decode(header.Png);
                writer.Draw(headerBitmap, Math.Max(1, header.Width), maxWidth);
            }

            for (int index = 0; index < systemCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PdfSystem system = await loadSystemAsync(index, cancellationToken).ConfigureAwait(false);
                using SKBitmap bitmap = Decode(system.Png);
                writer.Draw(bitmap, Math.Max(1, system.Width), maxWidth);
                progress?.Report(index + 1);
            }
        }
        finally
        {
            writer.EndPage();
            document.Close();
        }
    }

    private static SKDocument CreateDocument(Stream output, string title, string? composer) =>
        SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
        {
            Title = title,
            Author = composer ?? string.Empty,
            Creator = "MusicStaffViewer",
            Producer = "MusicStaffViewer",
            RasterDpi = 300f
        });

    private static SKBitmap Decode(byte[] png) =>
        SKBitmap.Decode(png) ?? throw new InvalidDataException("Could not decode a rendered system image.");

    private sealed class PageWriter(SKDocument document, PdfExportOptions options)
    {
        private readonly float _contentWidth = Math.Max(1, options.PageWidth - (options.Margin * 2));
        private readonly float _contentHeight = Math.Max(1, options.PageHeight - (options.Margin * 2));
        private SKCanvas? _canvas;
        private float _y;

        public void Draw(SKBitmap bitmap, double logicalWidth, double maxLogicalWidth)
        {
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                throw new InvalidDataException("Could not decode a rendered system image.");
            }

            float widthRatio = maxLogicalWidth > 0
                ? (float)(logicalWidth / maxLogicalWidth)
                : 1f;
            widthRatio = Math.Clamp(widthRatio, 0.05f, 1f);
            float drawWidth = _contentWidth * widthRatio;
            float drawHeight = bitmap.Height * (drawWidth / bitmap.Width);
            if (drawHeight > _contentHeight)
            {
                drawHeight = _contentHeight;
                drawWidth = bitmap.Width * (_contentHeight / bitmap.Height);
            }

            if (drawWidth <= 0 || drawHeight <= 0)
            {
                throw new InvalidDataException("Could not lay out a rendered system image.");
            }

            if (_canvas is null || _y + drawHeight > _contentHeight)
            {
                EndPage();
                _canvas = document.BeginPage(options.PageWidth, options.PageHeight);
                _canvas.Clear(SKColors.White);
                _y = 0f;
            }

            float x = options.Margin + ((_contentWidth - drawWidth) / 2f);
            _canvas.DrawBitmap(bitmap, new SKRect(x, options.Margin + _y,
                x + drawWidth, options.Margin + _y + drawHeight));
            _y += drawHeight + options.SystemGap;
        }

        public void EndPage()
        {
            if (_canvas is null)
            {
                return;
            }

            document.EndPage();
            _canvas.Dispose();
            _canvas = null;
        }
    }
}
