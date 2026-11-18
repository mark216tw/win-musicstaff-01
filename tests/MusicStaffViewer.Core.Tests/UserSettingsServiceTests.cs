using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.Services;
using Xunit;

namespace Com.Example.MusicStaffViewer.Core.Tests;

public sealed class UserSettingsServiceTests
{
    [Fact]
    public async Task PersistsPreferencesAndPrunesMissingRecentFilesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string existing = Path.Combine(directory, "present.musicxml");
        string missing = Path.Combine(directory, "missing.musicxml");
        try
        {
            await File.WriteAllTextAsync(existing, "<score-partwise />");
            var service = new UserSettingsService(Path.Combine(directory, "settings.json"));
            var saved = new AppSettings
            {
                AccentTheme = AccentTheme.ForestGreen,
                DisplayMode = DisplayMode.Dark,
                SheetStyle = SheetStyle.Paper,
                PdfPageSize = PdfPageSize.A3,
                InitialZoomMode = InitialZoomMode.Custom,
                DefaultZoom = 3.0,
                DefaultMeasuresPerSystem = 12,
                NoteSpacing = NoteSpacing.Loose,
                ScoreQuality = QualityLevel.Maximum,
                PdfQuality = QualityLevel.Standard,
                Window = new WindowBounds(150, 170, 1200, 800, true),
                RecentFiles = [missing, existing, existing],
                FavoriteFiles = [missing, existing, existing]
            };
            await service.SaveAsync(saved);

            AppSettings loaded = await service.LoadAsync();
            Assert.Equal(AccentTheme.ForestGreen, loaded.AccentTheme);
            Assert.Equal(DisplayMode.Dark, loaded.DisplayMode);
            Assert.Equal(SheetStyle.Paper, loaded.SheetStyle);
            Assert.Equal(PdfPageSize.A3, loaded.PdfPageSize);
            Assert.Equal(InitialZoomMode.Custom, loaded.InitialZoomMode);
            Assert.Equal(3.0, loaded.DefaultZoom);
            Assert.Equal(10, loaded.DefaultMeasuresPerSystem);
            Assert.Equal(NoteSpacing.Loose, loaded.NoteSpacing);
            Assert.Equal(QualityLevel.Maximum, loaded.ScoreQuality);
            Assert.Equal(QualityLevel.Standard, loaded.PdfQuality);
            Assert.Equal(saved.Window, loaded.Window);
            Assert.Equal([existing], loaded.RecentFiles);
            Assert.Equal([missing, existing], loaded.FavoriteFiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void AddsAndRemovesFavoritesIgnoringCase()
    {
        var settings = new AppSettings();
        UserSettingsService.Favorite(settings, @"C:\scores\a.musicxml");
        UserSettingsService.Favorite(settings, @"C:\Scores\A.musicxml");
        Assert.Single(settings.FavoriteFiles);

        UserSettingsService.Unfavorite(settings, @"c:\scores\a.musicxml");
        Assert.Empty(settings.FavoriteFiles);
    }

    [Fact]
    public async Task KeepsMissingFavoriteFilesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string existing = Path.Combine(directory, "present.musicxml");
        string missing = Path.Combine(directory, "missing.musicxml");
        try
        {
            await File.WriteAllTextAsync(existing, "<score-partwise />");
            var service = new UserSettingsService(Path.Combine(directory, "settings.json"));
            await service.SaveAsync(new AppSettings { FavoriteFiles = [missing, existing] });

            AppSettings loaded = await service.LoadAsync();

            Assert.Equal([missing, existing], loaded.FavoriteFiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public async Task UsesSixMeasuresPerSystemByDefaultAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}", "settings.json");
        var service = new UserSettingsService(path);

        AppSettings loaded = await service.LoadAsync();

        Assert.Equal(6, loaded.DefaultMeasuresPerSystem);
        Assert.Equal(InitialZoomMode.FitWidth, loaded.InitialZoomMode);
        Assert.Equal(PdfPageSize.A4, loaded.PdfPageSize);
        Assert.Equal(NoteSpacing.Compact, loaded.NoteSpacing);
        Assert.Equal(QualityLevel.High, loaded.ScoreQuality);
        Assert.Equal(QualityLevel.High, loaded.PdfQuality);
    }

    [Fact]
    public async Task FallsBackToCompactNoteSpacingWhenValueIsInvalidAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"NoteSpacing": 99}""");
            var service = new UserSettingsService(path);

            AppSettings loaded = await service.LoadAsync();

            Assert.Equal(NoteSpacing.Compact, loaded.NoteSpacing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FallsBackToHighQualityWhenValuesAreInvalidAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"ScoreQuality": 99, "PdfQuality": -1}""");
            var service = new UserSettingsService(path);

            AppSettings loaded = await service.LoadAsync();

            Assert.Equal(QualityLevel.High, loaded.ScoreQuality);
            Assert.Equal(QualityLevel.High, loaded.PdfQuality);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AddsAndUpdatesViewStatesIgnoringCase()
    {
        var settings = new AppSettings();
        UserSettingsService.RememberView(settings, new ScoreViewState
        {
            Path = @"C:\scores\a.musicxml",
            Zoom = 1.5,
            TopSystem = 2
        });
        UserSettingsService.RememberView(settings, new ScoreViewState
        {
            Path = @"c:\SCORES\A.musicxml",
            Zoom = 2.0,
            TopSystem = 5,
            VisibleTrackIndices = [0]
        });

        Assert.Single(settings.ScoreViewStates);
        ScoreViewState? view = UserSettingsService.GetView(settings, @"C:\Scores\a.musicxml");
        Assert.NotNull(view);
        Assert.Equal(2.0, view.Zoom);
        Assert.Equal(5, view.TopSystem);
        Assert.Equal([0], view.VisibleTrackIndices);
    }

    [Fact]
    public void CapsRememberedViewStatesToTwoHundred()
    {
        var settings = new AppSettings();
        for (int index = 0; index < 205; index++)
        {
            UserSettingsService.RememberView(settings, new ScoreViewState
            {
                Path = $@"C:\scores\{index}.musicxml",
                TopSystem = index
            });
        }

        Assert.Equal(200, settings.ScoreViewStates.Count);
        Assert.Contains(settings.ScoreViewStates,
            view => view.Path.EndsWith(@"\204.musicxml", StringComparison.Ordinal));
        Assert.DoesNotContain(settings.ScoreViewStates,
            view => view.Path.EndsWith(@"\0.musicxml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersistsAndNormalizesViewStatesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"MusicStaffViewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {"ScoreViewStates":[
                  {"Path":" C:\\scores\\a.musicxml ","Zoom":9,"TopSystem":-2,
                   "OffsetWithinSystem":-5,"VisibleTrackIndices":[2,2,-1],
                   "UpdatedUtc":"2026-01-01T00:00:00+00:00"},
                  {"Path":"c:\\scores\\A.musicxml","Zoom":1.5,"TopSystem":3,
                   "VisibleTrackIndices":[0],"UpdatedUtc":"2026-02-01T00:00:00+00:00"},
                  {"Path":"","Zoom":1}
                ]}
                """);
            var service = new UserSettingsService(path);

            AppSettings loaded = await service.LoadAsync();

            Assert.Single(loaded.ScoreViewStates);
            ScoreViewState view = loaded.ScoreViewStates[0];
            Assert.Equal(@"c:\scores\A.musicxml", view.Path);
            Assert.Equal(1.5, view.Zoom);
            Assert.Equal(3, view.TopSystem);
            Assert.Equal(0, view.OffsetWithinSystem);
            Assert.Equal([0], view.VisibleTrackIndices);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
