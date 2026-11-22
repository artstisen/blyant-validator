namespace Blyant.Validator.Core;

/// <summary>What the messages of the checks share: a text quoted in a message.</summary>
public static class Texts
{
    /// <summary>
    /// The text cut to at most <paramref name="max"/> characters, with "…" after it when it was longer. A character of two
    /// UTF-16 units (an emoji) is not cut in two: the cut is made before it
    /// </summary>
    public static string Shorten(string text, int max)
    {
        if (text.Length <= max) return text;
        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end] + "…";
    }
}
