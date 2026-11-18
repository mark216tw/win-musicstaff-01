using System.Text;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Xunit;

namespace Com.Example.MusicStaffViewer.Core.Tests;

public sealed class ScoreMetadataReaderTests
{
    [Fact]
    public async Task ReadsTitleMovementAndComposerFromXmlAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, "<score-partwise><work><work-title>Sonata</work-title></work>" +
                "<movement-title>Allegro</movement-title><identification><creator type=\"composer\">Mozart</creator>" +
                "</identification><part-list /></score-partwise>");
            var score = new ScoreMetadataReader().Read(path);
            Assert.Equal("Sonata", score.Title);
            Assert.Equal("Allegro", score.Movement);
            Assert.Equal("Mozart", score.Composer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadsJsonWrapperMetadataWithUtf8BomAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path,
                "{\"title\":\"Trio\",\"meta\":{\"composer\":\"Mozart\"},\"musicxml\":\"<score-partwise/>\"}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var score = new ScoreMetadataReader().Read(path);
            Assert.Equal("Trio", score.Title);
            Assert.Equal("Mozart", score.Composer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RefreshesCachedMetadataWhenFileChangesAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            var reader = new ScoreMetadataReader();
            await File.WriteAllTextAsync(path, "<score-partwise><work><work-title>First</work-title></work></score-partwise>");
            Assert.Equal("First", reader.Read(path).Title);

            await File.WriteAllTextAsync(path, "<score-partwise><work><work-title>Second Movement</work-title></work></score-partwise>");
            Assert.Equal("Second Movement", reader.Read(path).Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CachedMetadataDoesNotShareCurrentStateAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            var reader = new ScoreMetadataReader();
            await File.WriteAllTextAsync(path, "<score-partwise><work><work-title>Sonata</work-title></work></score-partwise>");
            var first = reader.Read(path);
            first.IsCurrent = true;

            Assert.False(reader.Read(path).IsCurrent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadsJsonWrapperMetadataAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, "{\"title\":\"Trio\",\"meta\":{\"composer\":\"Mozart\"},\"musicxml\":\"<score-partwise/>\"}");
            var score = new ScoreMetadataReader().Read(path);
            Assert.Equal("Trio", score.Title);
            Assert.Equal("Mozart", score.Composer);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
