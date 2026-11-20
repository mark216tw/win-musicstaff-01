using AlphaSkia;
using AlphaTab;
using AlphaTab.Importer;
using AlphaTab.Model;
using AlphaTab.Rendering;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using BridgeImage = AlphaTab.Platform.Skia.AlphaSkiaBridge.AlphaSkiaImage;

namespace Com.Example.MusicStaffViewer.Rendering;

public sealed class AlphaTabScoreSession(ScoreFileReader fileReader) : IAsyncDisposable
{
    private readonly RenderWorker _worker = new();
    private ScoreRenderer? _renderer;
    private Score? _score;
    private ScoreLayoutResult? _layout;
    private Exception? _renderError;
    private int _disposed;
    private uint _background = 0xFFFFFFFF;

    public const int LogicalWidth = 1600;
    public const double RasterScale = 2.0;
    public const double MinimumRasterScale = 1.0;
    public const double MaximumRasterScale = 4.0;
    private const int ScoreInfoBottomPadding = 17;
    private const System.Reflection.BindingFlags InternalPropertyFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic;
    private static readonly System.Reflection.PropertyInfo? RendererLayoutProperty =
        typeof(ScoreRenderer).GetProperty("Layout", InternalPropertyFlags);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?>
        SystemsPropertyCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, SystemPropertyAccessors>
        SystemAccessorsCache = new();

    public Task<ScoreLayoutResult> LoadAsync(string path, bool darkPaper, int measuresPerSystem,
        NoteSpacing noteSpacing, CancellationToken cancellationToken = default) =>
        LoadAsync(path, darkPaper, measuresPerSystem, noteSpacing, RasterScale, null, cancellationToken);

    public async Task<ScoreLayoutResult> LoadAsync(string path, bool darkPaper, int measuresPerSystem,
        NoteSpacing noteSpacing, double rasterScale, IReadOnlyList<int>? trackIndices = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(measuresPerSystem, noteSpacing, rasterScale);
        byte[] bytes = await fileReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return await _worker.RunAsync(() =>
        {
            Score score = ScoreLoader.LoadScoreFromBytes(bytes);
            _score = score;
            return LayoutCore(score, darkPaper, measuresPerSystem, noteSpacing, rasterScale, trackIndices);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ScoreLayoutResult> RelayoutAsync(bool darkPaper, int measuresPerSystem,
        NoteSpacing noteSpacing, double rasterScale, IReadOnlyList<int>? trackIndices,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(measuresPerSystem, noteSpacing, rasterScale);
        return _worker.RunAsync(() =>
        {
            Score score = _score ?? throw new InvalidOperationException("No score is loaded.");
            return LayoutCore(score, darkPaper, measuresPerSystem, noteSpacing, rasterScale, trackIndices);
        }, cancellationToken);
    }

    private static void ValidateOptions(int measuresPerSystem, NoteSpacing noteSpacing, double rasterScale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(measuresPerSystem,
            ScoreDisplayOptions.MinimumMeasuresPerSystem);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(measuresPerSystem,
            ScoreDisplayOptions.MaximumMeasuresPerSystem);
        if (!Enum.IsDefined(noteSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(noteSpacing));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(rasterScale, MinimumRasterScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rasterScale, MaximumRasterScale);
    }

    private ScoreLayoutResult LayoutCore(Score score, bool darkPaper, int measuresPerSystem,
        NoteSpacing noteSpacing, double rasterScale, IReadOnlyList<int>? requestedTracks)
    {
        if (score.Tracks.Count == 0)
        {
            throw new InvalidDataException("The score contains no tracks.");
        }

        List<int> tracks = ResolveTrackIndices(score, requestedTracks);
        _renderer?.Destroy();
        _renderer = null;
        _layout = null;
        _renderError = null;

        Settings settings = CreateSettings(darkPaper, measuresPerSystem, noteSpacing, rasterScale);
        _background = darkPaper ? 0xFF181818 : 0xFFFFFFFF;

        var renderer = new ScoreRenderer(settings) { Width = LogicalWidth * rasterScale };
        var partials = new List<ScorePartialLayout>();
        double width = 0;
        double height = 0;
        renderer.PartialLayoutFinished.On(partial => partials.Add(new ScorePartialLayout(
            partial.Id, partial.X / rasterScale, partial.Y / rasterScale,
            partial.Width / rasterScale, partial.Height / rasterScale)));
        renderer.RenderFinished.On(result =>
        {
            width = result.TotalWidth / rasterScale;
            height = result.TotalHeight / rasterScale;
        });
        renderer.Error.On(exception => _renderError = exception);

        renderer.RenderScore(score, tracks.Select(index => (double)index).ToList());
        if (_renderError is not null)
        {
            renderer.Destroy();
            throw new InvalidDataException("AlphaTab could not lay out this score.", _renderError);
        }

        (partials, ScorePartialLayout? header) = SplitPartials(partials, renderer, score);
        if (partials.Count == 0)
        {
            renderer.Destroy();
            throw new InvalidDataException("The score contains no printable systems.");
        }

        if (header is not null)
        {
            double headerTrim = Math.Min(header.Height, ScoreInfoBottomPadding / rasterScale);
            header = header with { Height = Math.Max(0, header.Height - headerTrim) };
            for (int index = 0; index < partials.Count; index++)
            {
                partials[index] = partials[index] with { Y = partials[index].Y - headerTrim };
            }

            height = Math.Max(0, height - headerTrim);
        }

        double contentLeft = partials.Min(partial => partial.X);
        double contentRight = partials.Max(partial => partial.X + partial.Width);
        if (header is not null)
        {
            contentLeft = Math.Min(contentLeft, header.X);
            contentRight = Math.Max(contentRight, header.X + header.Width);
        }

        for (int index = 0; index < partials.Count; index++)
        {
            partials[index] = partials[index] with { X = partials[index].X - contentLeft };
        }

        header = header is null ? null : header with { X = header.X - contentLeft };
        width = contentRight - contentLeft;
        _renderer = renderer;
        _layout = new ScoreLayoutResult(score.Title, score.Artist, width, height, partials, header,
            CreateTrackInfos(score), tracks, score.MasterBars.Count, CreateSectionInfos(score));
        return _layout;
    }

    private static (List<ScorePartialLayout> Systems, ScorePartialLayout? Header) SplitPartials(
        List<ScorePartialLayout> candidates, ScoreRenderer renderer, Score score)
    {
        object? layout = RendererLayoutProperty?.GetValue(renderer);
        if (layout is null)
        {
            return (candidates, null);
        }

        System.Reflection.PropertyInfo? systemsProperty = SystemsPropertyCache.GetOrAdd(
            layout.GetType(), static type => type.GetProperty("Systems", InternalPropertyFlags));
        object? value = systemsProperty?.GetValue(layout);
        if (value is not System.Collections.IEnumerable systems)
        {
            return (candidates, null);
        }

        var systemObjects = new List<object>();
        foreach (object? system in systems)
        {
            if (system is not null)
            {
                systemObjects.Add(system);
            }
        }

        if (systemObjects.Count == 0)
        {
            return (candidates, null);
        }

        SystemPropertyAccessors accessors = GetSystemAccessors(systemObjects[0].GetType());
        double firstY = ReadDouble(systemObjects[0], accessors.Y);
        double firstHeight = ReadDouble(systemObjects[0], accessors.Height);
        int first = -1;
        for (int index = 0; index < candidates.Count; index++)
        {
            if (Math.Abs(candidates[index].Y - firstY) <= 1.5 &&
                Math.Abs(candidates[index].Height - firstHeight) <= 1.5)
            {
                first = index;
                break;
            }
        }

        if (first < 0 || first + systemObjects.Count > candidates.Count)
        {
            return (candidates, null);
        }

        ScorePartialLayout? header = first > 0 && HasScoreInfo(score) ? candidates[0] : null;
        var result = new List<ScorePartialLayout>(systemObjects.Count);
        for (int index = 0; index < systemObjects.Count; index++)
        {
            object system = systemObjects[index];
            SystemPropertyAccessors systemAccessors = system.GetType() == accessors.Type
                ? accessors
                : GetSystemAccessors(system.GetType());
            result.Add(candidates[first + index] with
            {
                FirstMasterBarIndex = ReadInt(system, systemAccessors.FirstBarIndex),
                LastMasterBarIndex = ReadInt(system, systemAccessors.LastBarIndex)
            });
        }

        return (result, header);
    }

    private static SystemPropertyAccessors GetSystemAccessors(Type type) =>
        SystemAccessorsCache.GetOrAdd(type, static targetType => new SystemPropertyAccessors(
            targetType,
            targetType.GetProperty("Y", InternalPropertyFlags),
            targetType.GetProperty("Height", InternalPropertyFlags),
            targetType.GetProperty("FirstBarIndex", InternalPropertyFlags),
            targetType.GetProperty("LastBarIndex", InternalPropertyFlags)));

    private static bool HasScoreInfo(Score score) =>
        !string.IsNullOrWhiteSpace(score.Title) ||
        !string.IsNullOrWhiteSpace(score.SubTitle) ||
        !string.IsNullOrWhiteSpace(score.Artist) ||
        !string.IsNullOrWhiteSpace(score.Copyright);

    private static double ReadDouble(object target, System.Reflection.PropertyInfo? property) =>
        Convert.ToDouble(property?.GetValue(target), System.Globalization.CultureInfo.InvariantCulture);

    private static int ReadInt(object target, System.Reflection.PropertyInfo? property) =>
        Convert.ToInt32(property?.GetValue(target), System.Globalization.CultureInfo.InvariantCulture);

    private sealed record SystemPropertyAccessors(
        Type Type,
        System.Reflection.PropertyInfo? Y,
        System.Reflection.PropertyInfo? Height,
        System.Reflection.PropertyInfo? FirstBarIndex,
        System.Reflection.PropertyInfo? LastBarIndex);

    private static List<int> ResolveTrackIndices(Score score, IReadOnlyList<int>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return Enumerable.Range(0, score.Tracks.Count).ToList();
        }

        List<int> tracks = requested.Distinct().OrderBy(index => index).ToList();
        foreach (int index in tracks)
        {
            if (index < 0 || index >= score.Tracks.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(requested), index,
                    "The requested track index is outside the score.");
            }
        }

        return tracks;
    }

    private static Settings CreateSettings(bool darkPaper, int measuresPerSystem, NoteSpacing noteSpacing,
        double rasterScale)
    {
        var settings = new Settings();
        settings.Core.Engine = "skia";
        settings.Core.EnableLazyLoading = true;
        settings.Display.LayoutMode = LayoutMode.Page;
        settings.Display.Scale = rasterScale;
        settings.Display.BarsPerRow = measuresPerSystem;
        settings.Display.SpacingRatio = ScoreDisplayOptions.GetSpacingRatio(noteSpacing);
        Color ink = darkPaper ? new Color(240, 240, 240, 255) : new Color(16, 16, 16, 255);
        Color staff = darkPaper ? new Color(174, 174, 174, 255) : new Color(80, 80, 80, 255);
        foreach (NotationElement element in new[]
        {
            NotationElement.ScoreTitle,
            NotationElement.ScoreSubTitle,
            NotationElement.ScoreArtist,
            NotationElement.ScoreAlbum,
            NotationElement.ScoreWords,
            NotationElement.ScoreMusic,
            NotationElement.ScoreWordsAndMusic,
            NotationElement.ScoreCopyright
        })
        {
            settings.Display.Resources.ElementFonts.Get(element)!.Families = ["Microsoft JhengHei"];
        }
        settings.Display.Resources.MainGlyphColor = ink;
        settings.Display.Resources.SecondaryGlyphColor = ink;
        settings.Display.Resources.ScoreInfoColor = ink;
        settings.Display.Resources.BarNumberColor = ink;
        settings.Display.Resources.BarSeparatorColor = staff;
        settings.Display.Resources.StaffLineColor = staff;
        return settings;
    }

    private static List<ScoreTrackInfo> CreateTrackInfos(Score score)
    {
        var tracks = new List<ScoreTrackInfo>(score.Tracks.Count);
        for (int index = 0; index < score.Tracks.Count; index++)
        {
            Track track = score.Tracks[index];
            tracks.Add(new ScoreTrackInfo(index, track.Name ?? string.Empty,
                track.ShortName ?? string.Empty, track.IsPercussion));
        }

        return tracks;
    }

    private static List<ScoreSectionInfo> CreateSectionInfos(Score score)
    {
        var sections = new List<ScoreSectionInfo>();
        for (int index = 0; index < score.MasterBars.Count; index++)
        {
            Section? section = score.MasterBars[index].Section;
            if (section is null)
            {
                continue;
            }

            sections.Add(new ScoreSectionInfo(index, section.Marker ?? string.Empty,
                section.Text ?? string.Empty));
        }

        return sections;
    }

    public Task<RenderedPartial> RenderPartialAsync(string id, CancellationToken cancellationToken = default) =>
        _worker.RunAsync(() => RenderPartialCore(id), cancellationToken);

    public Task<RenderedPartial> RenderPartialAsync(int index, CancellationToken cancellationToken = default) =>
        _worker.RunAsync(() =>
        {
            ScoreLayoutResult layout = _layout ?? throw new InvalidOperationException("No score is loaded.");
            if ((uint)index >= (uint)layout.Partials.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return RenderPartialCore(layout.Partials[index].Id);
        }, cancellationToken);

    public Task<RenderedPartial> RenderHeaderPartialAsync(CancellationToken cancellationToken = default) =>
        _worker.RunAsync(() =>
        {
            ScoreLayoutResult layout = _layout ?? throw new InvalidOperationException("No score is loaded.");
            ScorePartialLayout header = layout.Header ??
                throw new InvalidOperationException("The score has no header partial.");
            return RenderPartialCore(header.Id, ScoreInfoBottomPadding);
        }, cancellationToken);

    private RenderedPartial RenderPartialCore(string id, int bottomTrim = 0)
    {
        ScoreRenderer renderer = _renderer ?? throw new InvalidOperationException("No score is loaded.");
        bool known = _layout is not null &&
            (_layout.Partials.Any(partial => partial.Id == id) || _layout.Header?.Id == id);
        if (!known)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        byte[]? png = null;
        Exception? error = null;
        int firstMasterBarIndex = -1;
        int lastMasterBarIndex = -1;
        Action<RenderFinishedEventArgs> onPartial = result =>
        {
            if (result.Id != id)
            {
                return;
            }

            firstMasterBarIndex = (int)result.FirstMasterBarIndex;
            lastMasterBarIndex = (int)result.LastMasterBarIndex;
            if (result.RenderResult is not BridgeImage bridge)
            {
                error = new InvalidDataException("The rendered partial has no Skia image.");
                return;
            }

            using AlphaSkiaCanvas canvas = new();
            int width = Math.Max(1, (int)Math.Ceiling(result.Width));
            int fullHeight = Math.Max(1, (int)Math.Ceiling(result.Height));
            int height = Math.Max(1, fullHeight - bottomTrim);
            canvas.BeginRender(width, height, 1f);
            canvas.Color = _background;
            canvas.FillRect(0, 0, width, height);
            canvas.DrawImage(bridge.Image, 0, 0, width, fullHeight);
            using AlphaSkiaImage? image = canvas.EndRender();
            png = image?.ToPng();
            bridge.Image.Dispose();
        };
        renderer.PartialRenderFinished.On(onPartial);
        try
        {
            _renderError = null;
            renderer.RenderResult(id);
            if (_renderError is not null)
            {
                throw new InvalidDataException("AlphaTab could not render this system.", _renderError);
            }

            if (error is not null)
            {
                throw error;
            }

            return new RenderedPartial(png ?? throw new InvalidDataException(
                "The partial did not produce a PNG image."), firstMasterBarIndex, lastMasterBarIndex);
        }
        finally
        {
            renderer.PartialRenderFinished.Off(onPartial);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _worker.RunAsync(() =>
        {
            _renderer?.Destroy();
            _renderer = null;
            _score = null;
            return true;
        }).ConfigureAwait(false);
        await _worker.DisposeAsync().ConfigureAwait(false);
    }
}
