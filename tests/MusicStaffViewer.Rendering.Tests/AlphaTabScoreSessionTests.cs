using System.Text;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Com.Example.MusicStaffViewer.Rendering;
using AlphaSkia;
using Xunit;

namespace Com.Example.MusicStaffViewer.Rendering.Tests;

public sealed class AlphaTabScoreSessionTests
{
    private const string TwoPartScore = """
        <?xml version="1.0" encoding="UTF-8"?>
        <score-partwise version="3.1">
          <work><work-title>Test Sonata</work-title></work>
          <identification><creator type="composer">Test Composer</creator></identification>
          <part-list>
            <score-part id="P1"><part-name>Piano</part-name></score-part>
            <score-part id="P2"><part-name>Piano</part-name></score-part>
          </part-list>
          <part id="P1">
            <measure number="1">
              <attributes>
                <divisions>1</divisions>
                <key><fifths>0</fifths></key>
                <time><beats>4</beats><beat-type>4</beat-type></time>
                <clef><sign>G</sign><line>2</line></clef>
              </attributes>
              <note>
                <pitch><step>C</step><octave>4</octave></pitch>
                <duration>4</duration>
                <type>whole</type>
              </note>
            </measure>
          </part>
          <part id="P2">
            <measure number="1">
              <attributes>
                <divisions>1</divisions>
                <key><fifths>0</fifths></key>
                <time><beats>4</beats><beat-type>4</beat-type></time>
                <clef><sign>F</sign><line>4</line></clef>
              </attributes>
              <note>
                <pitch><step>C</step><octave>3</octave></pitch>
                <duration>4</duration>
                <type>whole</type>
              </note>
            </measure>
          </part>
        </score-partwise>
        """;

    [Fact]
    public async Task LaysOutAndRendersMusicXmlAsync()
    {
        string path = await WriteTemporaryScoreAsync();
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult layout = await session.LoadAsync(path, darkPaper: false, measuresPerSystem: 6,
                noteSpacing: NoteSpacing.Normal);
            Assert.NotEmpty(layout.Partials);
            Assert.True(layout.Width > 0);
            Assert.True(layout.Height > 0);
            Assert.Equal("Test Sonata", System.Text.RegularExpressions.Regex.Replace(layout.Title, @"\s+", " "));
            Assert.NotNull(layout.Header);
            Assert.DoesNotContain(layout.Partials, partial => partial.Id == layout.Header!.Id);

            byte[] png = (await session.RenderPartialAsync(layout.Partials[0].Id)).Png;
            Assert.True(png.Length > 1024);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
            byte[] headerPng = (await session.RenderHeaderPartialAsync()).Png;
            Assert.True(headerPng.Length > 1024);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, headerPng[..4]);
            Assert.InRange(Math.Abs(layout.Partials[0].Y - (layout.Header!.Y + layout.Header.Height)), 0, 1.5);
            using AlphaSkiaImage headerImage = AlphaSkiaImage.Decode(headerPng) ??
                throw new InvalidDataException("Header PNG decode failed");
            int expectedHeight = (int)Math.Ceiling(layout.Header.Height * AlphaTabScoreSession.RasterScale);
            Assert.InRange(headerImage.Height, expectedHeight - 1, expectedHeight + 1);
            AssertTopRowIsBackground(headerImage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChineseTitleDoesNotTouchHeaderTopEdgeAsync()
    {
        string score = TwoPartScore.Replace("Test Sonata", "貝多芬-月光奏鳴曲-Moonlight Sonata",
            StringComparison.Ordinal);
        string path = await WriteTemporaryScoreAsync(score);
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            await session.LoadAsync(path, darkPaper: false, measuresPerSystem: 6,
                noteSpacing: NoteSpacing.Normal);

            byte[] headerPng = (await session.RenderHeaderPartialAsync()).Png;
            using AlphaSkiaImage headerImage = AlphaSkiaImage.Decode(headerPng) ??
                throw new InvalidDataException("Header PNG decode failed");
            AssertTopRowIsBackground(headerImage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertTopRowIsBackground(AlphaSkiaImage image)
    {
        byte[] pixels = image.ReadPixels() ?? throw new InvalidDataException("Pixel readback failed");
        byte[] backgroundPixel = pixels[..4];
        for (int x = 0; x < image.Width; x++)
        {
            Assert.Equal(backgroundPixel, pixels.AsSpan(x * 4, 4).ToArray());
        }
    }

    [Fact]
    public async Task OmitsHeaderWhenScoreHasNoTitleAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateUntitledScore());
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult layout = await session.LoadAsync(path, darkPaper: false, measuresPerSystem: 6,
                noteSpacing: NoteSpacing.Normal);

            Assert.Null(layout.Header);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RenderHeaderPartialAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DarkPaperProducesOpaqueDarkBackgroundAsync()
    {
        string path = await WriteTemporaryScoreAsync();
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult layout = await session.LoadAsync(path, darkPaper: true, measuresPerSystem: 6,
                noteSpacing: NoteSpacing.Normal);
            byte[] png = (await session.RenderPartialAsync(layout.Partials[0].Id)).Png;
            using AlphaSkiaImage image = AlphaSkiaImage.Decode(png) ?? throw new InvalidDataException("PNG decode failed");
            byte[] pixels = image.ReadPixels() ?? throw new InvalidDataException("Pixel readback failed");
            Assert.True(pixels[0] < 100 && pixels[1] < 100 && pixels[2] < 100);
            Assert.True(pixels[3] > 240);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MeasuresPerSystemChangesLayoutHeightAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var fourMeasureSession = new AlphaTabScoreSession(new ScoreFileReader());
            await using var tenMeasureSession = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult fourMeasures = await fourMeasureSession.LoadAsync(path, false, 4, NoteSpacing.Normal);
            ScoreLayoutResult tenMeasures = await tenMeasureSession.LoadAsync(path, false, 10, NoteSpacing.Normal);

            Assert.True(fourMeasures.Height > tenMeasures.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public async Task RejectsMeasuresPerSystemOutsideSupportedRangeAsync(int measures)
    {
        await using var session = new AlphaTabScoreSession(new ScoreFileReader());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.LoadAsync("unused.musicxml", false, measures, NoteSpacing.Normal));
    }

    [Fact]
    public async Task RejectsUndefinedNoteSpacingAsync()
    {
        await using var session = new AlphaTabScoreSession(new ScoreFileReader());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.LoadAsync("unused.musicxml", false, 6, (NoteSpacing)42));
    }

    [Fact]
    public async Task LayoutIsStableAcrossRasterScalesAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var baseSession = new AlphaTabScoreSession(new ScoreFileReader());
            await using var highResSession = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult baseLayout = await baseSession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, 2.0);
            ScoreLayoutResult highResLayout = await highResSession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, 3.0);

            Assert.Equal(baseLayout.Partials.Count, highResLayout.Partials.Count);
            for (int index = 0; index < baseLayout.Partials.Count; index++)
            {
                Assert.InRange(Math.Abs(baseLayout.Partials[index].X - highResLayout.Partials[index].X), 0, 1);
                Assert.InRange(Math.Abs(baseLayout.Partials[index].Width - highResLayout.Partials[index].Width), 0, 1);
                Assert.InRange(Math.Abs(baseLayout.Partials[index].Height - highResLayout.Partials[index].Height), 0, 1);
            }

            Assert.Equal(baseLayout.Header is null, highResLayout.Header is null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(4.0)]
    public async Task LayoutIsStableAtQualityExtremesAsync(double rasterScale)
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var baseSession = new AlphaTabScoreSession(new ScoreFileReader());
            await using var qualitySession = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult baseLayout = await baseSession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, 2.0);
            ScoreLayoutResult qualityLayout = await qualitySession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, rasterScale);

            Assert.Equal(baseLayout.Partials.Count, qualityLayout.Partials.Count);
            for (int index = 0; index < baseLayout.Partials.Count; index++)
            {
                Assert.InRange(Math.Abs(baseLayout.Partials[index].X - qualityLayout.Partials[index].X), 0, 1);
                Assert.InRange(Math.Abs(baseLayout.Partials[index].Width - qualityLayout.Partials[index].Width), 0, 1);
                Assert.InRange(Math.Abs(baseLayout.Partials[index].Height - qualityLayout.Partials[index].Height), 0, 1);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(4.1)]
    public async Task RejectsRasterScaleOutsideSupportedRangeAsync(double rasterScale)
    {
        await using var session = new AlphaTabScoreSession(new ScoreFileReader());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.LoadAsync("unused.musicxml", false, 6, NoteSpacing.Normal, rasterScale));
    }

    [Fact]
    public async Task RendersPartialsByIndexAcrossSessionsAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var baseSession = new AlphaTabScoreSession(new ScoreFileReader());
            await using var highResSession = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult baseLayout = await baseSession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, 2.0);
            ScoreLayoutResult highResLayout = await highResSession.LoadAsync(path, false, 6,
                NoteSpacing.Normal, 3.0);
            Assert.Equal(baseLayout.Partials.Count, highResLayout.Partials.Count);

            foreach (int index in new[] { 0, baseLayout.Partials.Count - 1 })
            {
                byte[] png = (await highResSession.RenderPartialAsync(index)).Png;
                Assert.True(png.Length > 1024);
                Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
            }

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                highResSession.RenderPartialAsync(-1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                highResSession.RenderPartialAsync(highResLayout.Partials.Count));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NoteSpacingChangesRenderedSystemsAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMixedDurationScore(6));
        try
        {
            await using var compactSession = new AlphaTabScoreSession(new ScoreFileReader());
            await using var looseSession = new AlphaTabScoreSession(new ScoreFileReader());

            ScoreLayoutResult compact = await compactSession.LoadAsync(path, false, 6,
                NoteSpacing.Compact, 2.0);
            ScoreLayoutResult loose = await looseSession.LoadAsync(path, false, 6,
                NoteSpacing.Loose, 2.0);

            byte[] compactPng = (await compactSession.RenderPartialAsync(compact.Partials[^1].Id)).Png;
            byte[] loosePng = (await looseSession.RenderPartialAsync(loose.Partials[^1].Id)).Png;

            Assert.False(compactPng.SequenceEqual(loosePng));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportsDecodedSystemsAsPdf()
    {
        using AlphaSkiaCanvas canvas = new();
        canvas.BeginRender(4, 4, 1f);
        canvas.Color = 0xFFFFFFFF;
        canvas.FillRect(0, 0, 4, 4);
        using AlphaSkiaImage image = canvas.EndRender() ?? throw new InvalidDataException("Image creation failed");
        byte[] png = image.ToPng() ?? throw new InvalidDataException("PNG encoding failed");

        using var output = new MemoryStream();
        PdfExporter.Write(output, "Test Sonata", "Test Composer", [png]);

        byte[] pdf = output.ToArray();
        Assert.True(pdf.Length > 4);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public void ExportsSystemsWithNaturalWidthsAsPdf()
    {
        using AlphaSkiaCanvas canvas = new();
        canvas.BeginRender(4, 4, 1f);
        canvas.Color = 0xFFFFFFFF;
        canvas.FillRect(0, 0, 4, 4);
        using AlphaSkiaImage image = canvas.EndRender() ?? throw new InvalidDataException("Image creation failed");
        byte[] png = image.ToPng() ?? throw new InvalidDataException("PNG encoding failed");

        using var output = new MemoryStream();
        PdfExporter.Write(output, "Test Sonata", "Test Composer",
            [new PdfSystem(png, 100, 50), new PdfSystem(png, 50, 50)],
            new PdfExportOptions
            {
                PageWidth = PdfExportOptions.A3Width,
                PageHeight = PdfExportOptions.A3Height
            });

        byte[] pdf = output.ToArray();
        Assert.True(pdf.Length > 4);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public async Task ExportsStreamedSystemsAsPdfAsync()
    {
        using AlphaSkiaCanvas canvas = new();
        canvas.BeginRender(4, 4, 1f);
        canvas.Color = 0xFFFFFFFF;
        canvas.FillRect(0, 0, 4, 4);
        using AlphaSkiaImage image = canvas.EndRender() ?? throw new InvalidDataException("Image creation failed");
        byte[] png = image.ToPng() ?? throw new InvalidDataException("PNG encoding failed");

        using var output = new MemoryStream();
        int loaded = 0;
        await PdfExporter.WriteAsync(output, "Test Sonata", "Test Composer", 2,
            (_, _) =>
            {
                loaded++;
                return Task.FromResult(png);
            });

        byte[] pdf = output.ToArray();
        Assert.Equal(2, loaded);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public async Task ExportsSystemsWithHeaderAsPdfAsync()
    {
        using AlphaSkiaCanvas canvas = new();
        canvas.BeginRender(4, 4, 1f);
        canvas.Color = 0xFFFFFFFF;
        canvas.FillRect(0, 0, 4, 4);
        using AlphaSkiaImage image = canvas.EndRender() ?? throw new InvalidDataException("Image creation failed");
        byte[] png = image.ToPng() ?? throw new InvalidDataException("PNG encoding failed");

        using var withHeader = new MemoryStream();
        using var withoutHeader = new MemoryStream();
        int loaded = 0;
        Func<int, CancellationToken, Task<PdfSystem>> load = (_, _) =>
        {
            loaded++;
            return Task.FromResult(new PdfSystem(png, 100, 50));
        };

        await PdfExporter.WriteSystemsAsync(withHeader, "Test Sonata", "Test Composer", 2, 100,
            load, new PdfSystem(png, 100, 20));
        await PdfExporter.WriteSystemsAsync(withoutHeader, "Test Sonata", "Test Composer", 2, 100, load);

        byte[] withHeaderPdf = withHeader.ToArray();
        Assert.Equal(4, loaded);
        Assert.Equal("%PDF"u8.ToArray(), withHeaderPdf[..4]);
        Assert.True(withHeaderPdf.Length > withoutHeader.Length);
    }

    [Fact]
    public async Task ExposesTracksAndMasterBarCountAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(8));
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult layout = await session.LoadAsync(path, false, 6, NoteSpacing.Normal);

            Assert.Single(layout.Tracks);
            Assert.Equal("Piano", layout.Tracks[0].Name);
            Assert.False(layout.Tracks[0].IsPercussion);
            Assert.Single(layout.SelectedTrackIndices);
            Assert.Equal(0, layout.SelectedTrackIndices[0]);
            Assert.Equal(8, layout.MasterBarCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectsVisibleTracksAsync()
    {
        string path = await WriteTemporaryScoreAsync();
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult all = await session.LoadAsync(path, false, 6, NoteSpacing.Normal);
            Assert.Equal(2, all.Tracks.Count);
            Assert.Equal(2, all.SelectedTrackIndices.Count);
            byte[] allPng = (await session.RenderPartialAsync(0)).Png;

            ScoreLayoutResult firstTrack = await session.RelayoutAsync(false, 6, NoteSpacing.Normal,
                2.0, [0]);
            Assert.Single(firstTrack.SelectedTrackIndices);
            Assert.Equal(0, firstTrack.SelectedTrackIndices[0]);
            byte[] firstPng = (await session.RenderPartialAsync(0)).Png;

            ScoreLayoutResult secondTrack = await session.RelayoutAsync(false, 6, NoteSpacing.Normal,
                2.0, [1]);
            Assert.Equal(2, secondTrack.Tracks.Count);
            Assert.Single(secondTrack.SelectedTrackIndices);
            Assert.Equal(1, secondTrack.SelectedTrackIndices[0]);
            Assert.NotEmpty(secondTrack.Partials);
            byte[] secondPng = (await session.RenderPartialAsync(0)).Png;

            Assert.False(firstPng.SequenceEqual(secondPng));
            Assert.False(allPng.SequenceEqual(firstPng));
            Assert.True(secondPng.Length > 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsUnknownTrackIndexAsync()
    {
        string path = await WriteTemporaryScoreAsync();
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                session.LoadAsync(path, false, 6, NoteSpacing.Normal, 2.0, [2]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LayoutReportsMasterBarRangesAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult layout = await session.LoadAsync(path, false, 6, NoteSpacing.Normal);

            Assert.Equal(0, layout.Partials[0].FirstMasterBarIndex);
            Assert.InRange(layout.Partials[0].LastMasterBarIndex, 0, layout.MasterBarCount - 1);
            Assert.Equal(layout.MasterBarCount - 1,
                layout.Partials[^1].LastMasterBarIndex);

            RenderedPartial first = await session.RenderPartialAsync(0);
            Assert.True(first.Png.Length > 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RelayoutReusesLoadedScoreAsync()
    {
        string path = await WriteTemporaryScoreAsync(CreateMultiMeasureScore(16));
        try
        {
            await using var session = new AlphaTabScoreSession(new ScoreFileReader());
            ScoreLayoutResult sixMeasures = await session.LoadAsync(path, false, 6, NoteSpacing.Normal);
            ScoreLayoutResult tenMeasures = await session.RelayoutAsync(false, 10,
                NoteSpacing.Normal, 2.0, null);

            Assert.True(sixMeasures.Partials.Count > tenMeasures.Partials.Count);
            Assert.Equal(sixMeasures.MasterBarCount, tenMeasures.MasterBarCount);
            byte[] png = (await session.RenderPartialAsync(0)).Png;
            Assert.True(png.Length > 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTemporaryScoreAsync(string score = TwoPartScore)
    {
        string path = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}.musicxml");
        await File.WriteAllTextAsync(path, score, Encoding.UTF8);
        return path;
    }

    private static string CreateMixedDurationScore(int measureCount)
    {
        var measures = new StringBuilder();
        for (int number = 1; number <= measureCount; number++)
        {
            string attributes = number == 1
                ? "<attributes><divisions>4</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>"
                : string.Empty;
            measures.Append($"<measure number=\"{number}\">{attributes}"
                + "<note><pitch><step>C</step><octave>4</octave></pitch><duration>8</duration><type>half</type></note>"
                + "<note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>"
                + "<note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>"
                + "</measure>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <score-partwise version="3.1">
              <work><work-title>Mixed Duration Test</work-title></work>
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">{measures}</part>
            </score-partwise>
            """;
    }

    private static string CreateUntitledScore() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <score-partwise version="3.1">
          <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
          <part id="P1">
            <measure number="1">
              <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>
              <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>
            </measure>
          </part>
        </score-partwise>
        """;

    private static string CreateMultiMeasureScore(int measureCount)
    {
        var measures = new StringBuilder();
        for (int number = 1; number <= measureCount; number++)
        {
            string attributes = number == 1
                ? "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>"
                : string.Empty;
            measures.Append($"<measure number=\"{number}\">{attributes}<note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <score-partwise version="3.1">
              <work><work-title>Measure Layout Test</work-title></work>
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">{measures}</part>
            </score-partwise>
            """;
    }
}
