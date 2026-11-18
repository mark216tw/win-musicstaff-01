using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml;
using Com.Example.MusicStaffViewer.Core.Models;

namespace Com.Example.MusicStaffViewer.Core.MusicXml;

public sealed class ScoreMetadataReader
{
    private const int MaxCachedScores = 64;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ScoreDocument Read(string path)
    {
        var info = new FileInfo(path);
        bool exists = info.Exists;
        DateTime lastWrite = exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        long length = exists ? info.Length : -1;
        if (exists && _cache.TryGetValue(path, out CacheEntry? cached) &&
            cached.LastWriteUtc == lastWrite && cached.Length == length)
        {
            return cached.Document with { IsCurrent = false };
        }

        ScoreDocument document = ReadCore(path);
        if (exists)
        {
            _cache[path] = new CacheEntry(lastWrite, length, document);
            TrimCache();
        }
        else
        {
            _cache.TryRemove(path, out _);
        }

        return document with { IsCurrent = false };
    }

    private void TrimCache()
    {
        if (_cache.Count <= MaxCachedScores)
        {
            return;
        }

        foreach (string path in _cache.Keys)
        {
            if (!File.Exists(path))
            {
                _cache.TryRemove(path, out _);
            }
        }

        foreach (string path in _cache.Keys.Take(_cache.Count - MaxCachedScores))
        {
            _cache.TryRemove(path, out _);
        }
    }

    private static ScoreDocument ReadCore(string path)
    {
        string fallback = Path.GetFileNameWithoutExtension(path);
        try
        {
            using FileStream stream = File.OpenRead(path);
            if (Path.GetExtension(path).Equals(".mxl", StringComparison.OrdinalIgnoreCase))
            {
                return new ScoreDocument(path, fallback, string.Empty);
            }

            Span<byte> head = stackalloc byte[8];
            int read = stream.Read(head);
            int offset = read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
            while (offset < read && char.IsWhiteSpace((char)head[offset]))
            {
                offset++;
            }

            if (offset < read && head[offset] == (byte)'{')
            {
                stream.Position = offset;
                using JsonDocument json = JsonDocument.Parse(stream);
                JsonElement root = json.RootElement;
                string title = root.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() ?? fallback : fallback;
                string composer = root.TryGetProperty("meta", out JsonElement meta) &&
                    meta.TryGetProperty("composer", out JsonElement creator) ? creator.GetString() ?? string.Empty : string.Empty;
                return new ScoreDocument(path, title, composer);
            }

            stream.Position = 0;
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });
            string? titleFromXml = null;
            string? movement = null;
            string composerFromXml = string.Empty;
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    if (!reader.Read())
                    {
                        break;
                    }

                    continue;
                }

                if (reader.LocalName == "part-list")
                {
                    break;
                }

                if (reader.LocalName == "work-title")
                {
                    titleFromXml = reader.ReadElementContentAsString();
                }
                else if (reader.LocalName == "movement-title")
                {
                    movement = reader.ReadElementContentAsString();
                }
                else if (reader.LocalName == "creator" && reader.GetAttribute("type") == "composer")
                {
                    composerFromXml = reader.ReadElementContentAsString();
                }
                else if (!reader.Read())
                {
                    break;
                }
            }

            return new ScoreDocument(path, string.IsNullOrWhiteSpace(titleFromXml) ? movement ?? fallback : titleFromXml,
                composerFromXml, movement);
        }
        catch (Exception exception) when (exception is IOException or XmlException or JsonException)
        {
            return new ScoreDocument(path, fallback, string.Empty);
        }
    }

    private sealed record CacheEntry(DateTime LastWriteUtc, long Length, ScoreDocument Document);
}
