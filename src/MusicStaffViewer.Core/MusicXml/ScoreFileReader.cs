using System.Text;
using System.Text.Json;

namespace Com.Example.MusicStaffViewer.Core.MusicXml;

public sealed class ScoreFileReader
{
    public static readonly string[] SupportedExtensions = [".musicxml", ".xml", ".mxl"];

    public async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Unsupported score file extension.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (Path.GetExtension(path).Equals(".mxl", StringComparison.OrdinalIgnoreCase))
        {
            return bytes;
        }

        int offset = 0;
        while (offset < bytes.Length && (bytes[offset] is 0xEF or 0xBB or 0xBF or 0x20 or 0x09 or 0x0A or 0x0D))
        {
            offset++;
        }

        if (offset < bytes.Length && bytes[offset] == (byte)'{')
        {
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(offset));
            if (!document.RootElement.TryGetProperty("musicxml", out JsonElement xml) ||
                xml.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(xml.GetString()))
            {
                throw new InvalidDataException("The JSON score does not contain MusicXML.");
            }

            return Encoding.UTF8.GetBytes(xml.GetString()!);
        }

        if (offset >= bytes.Length || bytes[offset] != (byte)'<')
        {
            throw new InvalidDataException("The score is not MusicXML.");
        }

        return bytes;
    }
}
