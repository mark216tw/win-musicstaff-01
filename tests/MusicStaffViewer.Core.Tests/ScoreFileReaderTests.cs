using System.Text;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Xunit;

namespace Com.Example.MusicStaffViewer.Core.Tests;

public sealed class ScoreFileReaderTests
{
    [Fact]
    public async Task ReadsEmbeddedMusicXmlFromJsonWrapperAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, "{\"title\":\"Test\",\"musicxml\":\"<score-partwise/>\"}");
            byte[] bytes = await new ScoreFileReader().ReadAsync(path);
            Assert.Equal("<score-partwise/>", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadsJsonWrapperWithUtf8BomAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, "{\"musicxml\":\"<score-partwise/>\"}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            byte[] bytes = await new ScoreFileReader().ReadAsync(path);
            Assert.Equal("<score-partwise/>", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsMissingEmbeddedMusicXmlAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, "{\"title\":\"Test\"}");
            await Assert.ThrowsAsync<InvalidDataException>(() => new ScoreFileReader().ReadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
