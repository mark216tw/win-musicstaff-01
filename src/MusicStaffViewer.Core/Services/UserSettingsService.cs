using System.Text.Json;
using Com.Example.MusicStaffViewer.Core.Models;

namespace Com.Example.MusicStaffViewer.Core.Services;

public sealed class UserSettingsService(string path)
{
    private const int MaxViewStates = 200;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            try
            {
                await using FileStream stream = File.OpenRead(path);
                AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false) ?? new AppSettings();
                settings.RecentFiles ??= [];
                settings.RecentFiles = settings.RecentFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10).ToList();
                settings.FavoriteFiles ??= [];
                settings.FavoriteFiles = settings.FavoriteFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                settings.ScoreViewStates ??= [];
                settings.ScoreViewStates = NormalizeViewStates(settings.ScoreViewStates);
                settings.DefaultZoom = Math.Clamp(settings.DefaultZoom,
                    ScoreDisplayOptions.MinimumZoom, ScoreDisplayOptions.MaximumZoom);
                settings.DefaultMeasuresPerSystem = Math.Clamp(settings.DefaultMeasuresPerSystem,
                    ScoreDisplayOptions.MinimumMeasuresPerSystem, ScoreDisplayOptions.MaximumMeasuresPerSystem);
                if (!Enum.IsDefined(settings.NoteSpacing))
                {
                    settings.NoteSpacing = NoteSpacing.Compact;
                }

                if (!Enum.IsDefined(settings.ScoreQuality))
                {
                    settings.ScoreQuality = QualityLevel.High;
                }

                if (!Enum.IsDefined(settings.PdfQuality))
                {
                    settings.PdfQuality = QualityLevel.High;
                }

                if (!Enum.IsDefined(settings.DisplayMode))
                {
                    settings.DisplayMode = DisplayMode.System;
                }

                if (!Enum.IsDefined(settings.AccentTheme))
                {
                    settings.AccentTheme = AccentTheme.ClassicBlue;
                }

                if (!Enum.IsDefined(settings.PdfPageSize))
                {
                    settings.PdfPageSize = PdfPageSize.A4;
                }

                if (!Enum.IsDefined(settings.InitialZoomMode))
                {
                    settings.InitialZoomMode = InitialZoomMode.FitWidth;
                }
                return settings;
            }
            catch (JsonException)
            {
                File.Copy(path, path + ".invalid", overwrite: true);
                return new AppSettings();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = path + ".tmp";
            await using (FileStream stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (IOException)
                {
                    File.Move(temporary, path, overwrite: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporary, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public static void Remember(AppSettings settings, string filePath)
    {
        settings.RecentFiles.RemoveAll(path => path.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles.Insert(0, filePath);
        if (settings.RecentFiles.Count > 10)
        {
            settings.RecentFiles.RemoveRange(10, settings.RecentFiles.Count - 10);
        }

        settings.LastScorePath = filePath;
    }

    public static void Favorite(AppSettings settings, string filePath)
    {
        if (!settings.FavoriteFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            settings.FavoriteFiles.Add(filePath);
        }
    }

    public static void Unfavorite(AppSettings settings, string filePath) =>
        settings.FavoriteFiles.RemoveAll(path =>
            path.Equals(filePath, StringComparison.OrdinalIgnoreCase));

    public static ScoreViewState? GetView(AppSettings settings, string filePath) =>
        settings.ScoreViewStates.FirstOrDefault(state =>
            state.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));

    public static void RememberView(AppSettings settings, ScoreViewState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        settings.ScoreViewStates.RemoveAll(existing =>
            existing.Path.Equals(state.Path, StringComparison.OrdinalIgnoreCase));
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        settings.ScoreViewStates.Insert(0, state);
        if (settings.ScoreViewStates.Count > MaxViewStates)
        {
            settings.ScoreViewStates.RemoveRange(MaxViewStates,
                settings.ScoreViewStates.Count - MaxViewStates);
        }
    }

    private static List<ScoreViewState> NormalizeViewStates(List<ScoreViewState> states)
    {
        var normalized = new Dictionary<string, ScoreViewState>(StringComparer.OrdinalIgnoreCase);
        foreach (ScoreViewState? state in states)
        {
            if (state is null || string.IsNullOrWhiteSpace(state.Path))
            {
                continue;
            }

            state.Path = state.Path.Trim();
            state.Zoom = Math.Clamp(state.Zoom,
                ScoreDisplayOptions.MinimumZoom, ScoreDisplayOptions.MaximumZoom);
            state.TopSystem = Math.Max(0, state.TopSystem);
            state.TopMeasure = Math.Max(0, state.TopMeasure);
            state.OffsetWithinSystem = Math.Max(0, state.OffsetWithinSystem);
            state.VisibleTrackIndices = (state.VisibleTrackIndices ?? [])
                .Where(index => index >= 0).Distinct().OrderBy(index => index).ToList();
            if (state.UpdatedUtc == default)
            {
                state.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            if (!normalized.TryGetValue(state.Path, out ScoreViewState? existing) ||
                existing.UpdatedUtc < state.UpdatedUtc)
            {
                normalized[state.Path] = state;
            }
        }

        return normalized.Values
            .OrderByDescending(state => state.UpdatedUtc)
            .Take(MaxViewStates)
            .ToList();
    }
}
