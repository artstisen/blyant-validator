using System.Globalization;
using System.Text;

namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// Helpers for working with text as it is seen on screen: a "text element" is what the user perceives
/// as one character (it can be several chars, like a letter with combining marks or an emoji), and its
/// width is the number of terminal cells it occupies.
/// Copied from Blyant's Editor/TextMetrics.cs.
/// </summary>
internal static class TextMetrics
{
    // Chars below this value are never combined with the char before them
    private const char FirstCombiningChar = '̀';

    /// <summary>
    /// The length (in chars) of the text element starting at index
    /// </summary>
    internal static int ElementLength(string s, int index)
    {
        if (s[index] < FirstCombiningChar && (index + 1 == s.Length || s[index + 1] < FirstCombiningChar))
        {
            return 1;
        }

        return StringInfo.GetNextTextElementLength(s, index);
    }

    /// <summary>
    /// The length (in chars) of the text element ending just before index
    /// </summary>
    internal static int PreviousElementLength(string s, int index)
    {
        if (index <= 0) return 0;
        if (s[index - 1] < FirstCombiningChar) return 1;
        // Go back to a char that is known to start an element and walk forward from there
        var start = index - 1;
        while (start > 0 && s[start] >= FirstCombiningChar)
        {
            start--;
        }

        while (start < index)
        {
            var length = ElementLength(s, start);
            if (start + length >= index) return index - start;
            start += length;
        }

        return 1;
    }

    /// <summary>
    /// The number of terminal cells used by the text element at index
    /// </summary>
    internal static int Width(string s, int index, int length)
    {
        var c = s[index];
        // An emoji variation selector makes the element wide (also "1" + selector + keycap)
        if (length > 1 && s.AsSpan(index, length).Contains('️')) return 2;
        if (c < FirstCombiningChar) return 1; // Control characters are displayed as a blank space
        if (!Rune.TryGetRuneAt(s, index, out var rune)) return 1;
        return RuneWidth(rune);
    }

    /// <summary>
    /// The number of terminal cells used by the chars from start and up to (not including) end
    /// </summary>
    internal static int WidthOfRange(string s, int start, int end)
    {
        var cells = 0;
        while (start < end)
        {
            var length = ElementLength(s, start);
            cells += Width(s, start, length);
            start += length;
        }

        return cells;
    }

    internal static int WidthOf(string s) => WidthOfRange(s, 0, s.Length);

    /// <summary>
    /// A text (a path) that fits in a number of terminal cells: as it is, or its end with "..." in front of it.
    /// Wide characters count as two cells, and a character (or emoji) is never split
    /// </summary>
    internal static string KeepEnd(string s, int cells)
    {
        if (WidthOf(s) <= cells) return Printable(s);
        var dots = cells > 3 ? "..." : string.Empty;
        return dots + Printable(EndPart(s, cells - dots.Length));
    }

    /// <summary>
    /// The start of a text that fits in a number of terminal cells (a character is never split)
    /// </summary>
    internal static string StartPart(string s, int cells)
    {
        var end = 0;
        var used = 0;
        while (end < s.Length)
        {
            var length = ElementLength(s, end);
            var width = Width(s, end, length);
            if (used + width > cells) break;
            used += width;
            end += length;
        }

        return s[..end];
    }

    /// <summary>
    /// The end of a text that fits in a number of terminal cells (a character is never split)
    /// </summary>
    internal static string EndPart(string s, int cells)
    {
        var start = s.Length;
        var used = 0;
        while (start > 0)
        {
            var length = PreviousElementLength(s, start);
            var width = Width(s, start - length, length);
            if (used + width > cells) break;
            used += width;
            start -= length;
        }

        return s[start..];
    }

    // The symbols below U+3000 that are shown as emoji, two columns wide (East Asian Width W)
    private static bool IsWideSymbol(int value)
    {
        return value is 0x231A or 0x231B or 0x2329 or 0x232A or >= 0x23E9 and <= 0x23EC or 0x23F0 or 0x23F3
            or 0x25FD or 0x25FE or 0x2614 or 0x2615 or >= 0x2648 and <= 0x2653 or 0x267F or 0x2693 or 0x26A1
            or 0x26AA or 0x26AB or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or 0x26F2
            or 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or 0x274E
            or >= 0x2753 and <= 0x2755 or 0x2757 or >= 0x2795 and <= 0x2797 or 0x27B0 or 0x27BF or 0x2B1B
            or 0x2B1C or 0x2B50 or 0x2B55;
    }

    private static int RuneWidth(Rune rune)
    {
        var value = rune.Value;
        if (value <= 0xFFFF && IsUnsafe((char)value)) return 1; // Shown as a blank space (Printable)
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) return 0; // Zero width space, joiners etc.
        if (value < 0x1100) return 1;
        if (value <= 0x115F || // Hangul Jamo
            value is >= 0x2E80 and <= 0x303E || // CJK radicals and punctuation
            value is >= 0x3041 and <= 0x33FF || // Hiragana, Katakana, CJK symbols
            value is >= 0x3400 and <= 0x4DBF || // CJK extension A
            value is >= 0x4E00 and <= 0x9FFF || // CJK unified ideographs
            value is >= 0xA000 and <= 0xA4CF || // Yi
            value is >= 0xAC00 and <= 0xD7A3 || // Hangul syllables
            value is >= 0xF900 and <= 0xFAFF || // CJK compatibility ideographs
            value is >= 0xFE30 and <= 0xFE4F || // CJK compatibility forms
            value is >= 0xFF00 and <= 0xFF60 || // Fullwidth forms
            value is >= 0xFFE0 and <= 0xFFE6 ||
            IsWideSymbol(value) || // Symbols shown as emoji
            value is >= 0x16FE0 and <= 0x18CFF || // Tangut, Khitan
            value is >= 0x1B000 and <= 0x1B2FF || // Kana supplement and extensions
            value is 0x1F004 or 0x1F0CF or 0x1F18E || // Mahjong, joker, AB button
            value is >= 0x1F191 and <= 0x1F19A || // Squared CL ... VS
            value is >= 0x1F200 and <= 0x1F2FF || // Enclosed ideographs
            value is >= 0x1F300 and <= 0x1F64F || // Emoji
            value is >= 0x1F680 and <= 0x1F6FF ||
            value is >= 0x1F7E0 and <= 0x1F7F0 || // Colored circles and squares
            value is >= 0x1F900 and <= 0x1FAFF ||
            value is >= 0x20000 and <= 0x3FFFD) // CJK extensions
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// The text to write to the terminal (control characters are shown as blank spaces): a text that comes from
    /// outside (a file name, a line of a file) must never send its own escape codes to the terminal, nor turn the
    /// direction of the text around (the bidi controls of "Trojan Source": a name "report\u202Egnp.html" would show
    /// as "reportlmth.png", and a terminal that orders bidi text would turn the rest of the row around too)
    /// </summary>
    internal static string Printable(string s)
    {
        var needsReplacing = false;
        foreach (var c in s)
        {
            if (!IsUnsafe(c)) continue;
            needsReplacing = true;
            break;
        }

        if (!needsReplacing) return s;
        var builder = new StringBuilder(s.Length);
        foreach (var c in s) builder.Append(IsUnsafe(c) ? ' ' : c);
        return builder.ToString();
    }

    // Control characters (C0, DEL, C1), the bidi controls (embeddings, overrides, isolates, marks) and the line and
    // paragraph separators
    private static bool IsUnsafe(char c) =>
        char.IsControl(c) || c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069' or '\u200E' or '\u200F' or '\u061C'
            or '\u2028' or '\u2029';
}
