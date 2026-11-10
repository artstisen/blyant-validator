using System.Text;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli;

/// <summary>
/// A file that is not UTF-8. It is not read: decoding it as UTF-8 would turn its other characters into U+FFFD, and
/// formatting would then write those back, losing the original bytes for good.
/// </summary>
/// <param name="line">The line of the first byte that is not UTF-8 (1 for a UTF-16 or UTF-32 file).</param>
internal sealed class NotUtf8Exception(string message, int line) : IOException(message)
{
    public int Line { get; } = line;

    /// <summary>The problem as a diagnostic (validation reports a file that is not UTF-8 as an error of the file).</summary>
    public Diagnostic ToDiagnostic(string path) =>
        new(path, Severity.Error, "file.not-utf8", Message, new SourcePosition(Line, 1), new SourcePosition(Line, 1));
}

/// <summary>Reads and writes the user's files. All file access of the program goes through here.</summary>
internal static class DocumentIo
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    // Throws on bytes that are not UTF-8, instead of replacing them
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<SourceDocument> LoadAsync(string path, CancellationToken ct) =>
        FromBytes(path, await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));

    public static SourceDocument Load(string path) => FromBytes(path, File.ReadAllBytes(path));

    /// <summary>
    /// The start of a file, at most <paramref name="maxBytes"/> of it (the peek shows a part of a large file, and must
    /// not read all of a file of gigabytes). A character cut in two at the end is left out
    /// </summary>
    /// <param name="cut">True when the file is longer than what was read.</param>
    public static SourceDocument LoadStart(string path, int maxBytes, out bool cut)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(maxBytes, Math.Max(0, stream.Length))];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        cut = stream.Length > read;
        var end = read;
        if (cut)
        {
            // Back to the start of the last character, if its bytes are not all there
            var start = end - 1;
            while (start > 0 && end - start < 4 && (buffer[start] & 0xC0) == 0x80) start--;
            var lead = buffer[start];
            var length = lead < 0x80 ? 1 : lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
            if (start + length > end) end = start;
        }

        return FromBytes(path, buffer[..end]);
    }

    /// <exception cref="NotUtf8Exception">The file is UTF-16 or UTF-32, or has bytes that are not UTF-8.</exception>
    internal static SourceDocument FromBytes(string path, byte[] bytes)
    {
        var span = bytes.AsSpan();
        // (UTF-32 LE starts as UTF-16 LE does, so it is asked first)
        var other = span.StartsWith((byte[])[0xFF, 0xFE, 0, 0]) || span.StartsWith((byte[])[0, 0, 0xFE, 0xFF]) ? "UTF-32"
            : span.StartsWith((byte[])[0xFF, 0xFE]) || span.StartsWith((byte[])[0xFE, 0xFF]) ? "UTF-16"
            : null;
        if (other != null)
            throw new NotUtf8Exception($"The file is in {other}, not UTF-8: it is not checked or formatted. Save it as UTF-8.", 1);

        var hasBom = span.StartsWith(Utf8WithBom.Preamble);
        var start = hasBom ? 3 : 0;
        string content;
        try
        {
            content = Utf8Strict.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException e)
        {
            var at = Math.Clamp(start + Math.Max(0, e.Index), 0, bytes.Length - 1);
            var line = 1 + span[..at].Count((byte)'\n');
            throw new NotUtf8Exception(
                $"The file is not in UTF-8: byte 0x{bytes[at]:X2} on line {line} is not UTF-8 (is the file in Windows-1252 " +
                "or Latin-1?). It is not checked or formatted. Save it as UTF-8.", line);
        }

        return new SourceDocument(path, content, SourceDocument.DetectKind(path)) { HasUtf8Bom = hasBom };
    }

    /// <summary>
    /// Replaces a file without ever leaving it empty or half written: the text goes to a temporary file
    /// next to it, which is flushed to the disk and then moved into place in one step. A link is followed (the file it
    /// leads to is written, and the link stays), and the permissions of the file (Linux, macOS) and its attributes
    /// (hidden …, Windows) are kept.
    /// </summary>
    public static void SafeWriteAllText(string path, string content, bool withBom)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
            full = Path.GetFullPath(target.FullName);
        var directory = Path.GetDirectoryName(full) ?? ".";
        // (short, so a long file name does not make the temporary name too long)
        var temp = Path.Combine(directory, $".bv-{Guid.NewGuid():N}.tmp");

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

            if (File.Exists(full))
            {
                if (OperatingSystem.IsWindows())
                    File.SetAttributes(temp, File.GetAttributes(full) & ~FileAttributes.ReadOnly);
                else
                    File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
