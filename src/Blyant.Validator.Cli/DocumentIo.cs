using System.Text;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli;

/// <summary>Reads and writes the user's files. All file access of the program goes through here.</summary>
internal static class DocumentIo
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task<SourceDocument> LoadAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var hasBom = bytes.AsSpan().StartsWith(Utf8WithBom.Preamble);
        var content = Utf8NoBom.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        return new SourceDocument(path, content, SourceDocument.DetectKind(path)) { HasUtf8Bom = hasBom };
    }

    /// <summary>
    /// Replaces a file without ever leaving it empty or half written: the text goes to a temporary file
    /// next to it, which is flushed to the disk and then moved into place in one step.
    /// </summary>
    public static void SafeWriteAllText(string path, string content, bool withBom)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var bytes = (withBom ? Utf8WithBom : Utf8NoBom).GetPreamble()
                .Concat(Utf8NoBom.GetBytes(content))
                .ToArray();

            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
