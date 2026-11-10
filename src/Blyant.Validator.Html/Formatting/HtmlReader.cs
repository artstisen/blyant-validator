using System.Collections.Frozen;

namespace Blyant.Validator.Html.Formatting;

/// <summary>
/// Reads HTML into an <see cref="HElement"/> tree for the formatter. It is forgiving: it never fails,
/// knows the end tags that may be left out (<c>li</c>, <c>p</c>, <c>td</c> …), keeps end tags without
/// a start tag, and records the source text of everything.
/// </summary>
internal sealed class HtmlReader
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Start tags that close an open <c>p</c>.</summary>
    private static readonly FrozenSet<string> ClosesParagraph = new[]
    {
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header",
        "hgroup", "hr", "main", "menu", "nav", "ol", "p", "pre", "search", "section", "table", "ul",
        "listing", "xmp", "plaintext",
    }.ToFrozenSet(Ci);

    private readonly string _s;
    private readonly List<HElement> _stack = [];
    private int _pos;

    private HtmlReader(string source)
    {
        _s = source;
        _stack.Add(new HElement("#root") { ContentStart = 0 });
    }

    public static HElement Read(string source)
    {
        var reader = new HtmlReader(source);
        reader.Run();
        return reader._stack[0];
    }

    private HElement Current => _stack[^1];

    private bool InForeignContent => _stack.Any(e => e.Lower is "svg" or "math");

    private void Run()
    {
        while (_pos < _s.Length)
        {
            if (_s[_pos] == '<' && IsMarkupStart(_pos))
            {
                ReadMarkup();
                continue;
            }

            var start = _pos;
            _pos++;
            while (_pos < _s.Length && !(_s[_pos] == '<' && IsMarkupStart(_pos))) _pos++;
            Current.Children.Add(new HText(_s[start.._pos]) { Start = start });
        }

        foreach (var open in _stack) open.ContentEnd = _s.Length;
    }

    private bool IsMarkupStart(int p)
    {
        if (p + 1 >= _s.Length) return false;
        var c = _s[p + 1];
        if (char.IsAsciiLetter(c) || c is '!' or '?') return true;
        return c == '/' && p + 2 < _s.Length && char.IsAsciiLetter(_s[p + 2]);
    }

    private void ReadMarkup()
    {
        var start = _pos;
        if (At("<!--"))
        {
            var end = _s.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
            _pos = end < 0 ? _s.Length : end + 3;
            Current.Children.Add(new HVerbatim(_s[start.._pos], false) { Start = start });
            return;
        }
        if (At("<![CDATA["))
        {
            var end = _s.IndexOf("]]>", _pos, StringComparison.Ordinal);
            _pos = end < 0 ? _s.Length : end + 3;
            Current.Children.Add(new HVerbatim(_s[start.._pos], false) { Start = start });
            return;
        }
        if (_s[_pos + 1] is '!' or '?')
        {
            var end = _s.IndexOf('>', _pos);
            _pos = end < 0 ? _s.Length : end + 1;
            var raw = _s[start.._pos];
            Current.Children.Add(new HVerbatim(raw, raw.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)) { Start = start });
            return;
        }
        if (_s[_pos + 1] == '/')
        {
            ReadEndTag();
            return;
        }
        ReadStartTag();
    }

    private bool At(string text) => string.CompareOrdinal(_s, _pos, text, 0, text.Length) == 0;

    private void ReadEndTag()
    {
        var start = _pos;
        _pos += 2;
        var nameStart = _pos;
        while (_pos < _s.Length && !char.IsWhiteSpace(_s[_pos]) && _s[_pos] is not ('>' or '/')) _pos++;
        var name = _s[nameStart.._pos];
        var end = _s.IndexOf('>', _pos);
        _pos = end < 0 ? _s.Length : end + 1;
        var raw = _s[start.._pos];

        var lower = name.ToLowerInvariant();
        for (var k = _stack.Count - 1; k > 0; k--)
        {
            if (_stack[k].Lower != lower) continue;
            PopTo(k, start);
            var element = _stack[k];
            element.EndTag = raw;
            element.ContentEnd = start;
            _stack.RemoveAt(k);
            return;
        }

        Current.Children.Add(new HStrayEndTag(raw) { Start = start });
    }

    /// <summary>Closes the elements above index <paramref name="k"/>, which have no end tag.</summary>
    private void PopTo(int k, int at)
    {
        while (_stack.Count - 1 > k)
        {
            _stack[^1].ContentEnd = at;
            _stack.RemoveAt(_stack.Count - 1);
        }
    }

    private void ReadStartTag()
    {
        var start = _pos;
        _pos++;
        var nameStart = _pos;
        while (_pos < _s.Length && !char.IsWhiteSpace(_s[_pos]) && _s[_pos] is not ('>' or '/')) _pos++;
        var element = new HElement(_s[nameStart.._pos]) { Start = start };

        while (_pos < _s.Length)
        {
            var hadSpace = SkipWhitespace();
            if (_pos >= _s.Length) break;
            var c = _s[_pos];
            if (c == '>')
            {
                _pos++;
                break;
            }
            if (c == '/' && _pos + 1 < _s.Length && _s[_pos + 1] == '>')
            {
                element.SelfClosing = true;
                element.SpaceBeforeSlash = hadSpace;
                _pos += 2;
                break;
            }
            if (c == '/')
            {
                _pos++;
                continue;
            }

            var attrStart = _pos;
            _pos++;
            while (_pos < _s.Length && !char.IsWhiteSpace(_s[_pos]) && _s[_pos] is not ('>' or '/' or '=')) _pos++;
            var attrName = _s[attrStart.._pos];

            var save = _pos;
            SkipWhitespace();
            if (_pos < _s.Length && _s[_pos] == '=')
            {
                _pos++;
                SkipWhitespace();
                element.Attributes.Add(new HAttribute(attrName, ReadAttributeValue()));
            }
            else
            {
                _pos = save;
                element.Attributes.Add(new HAttribute(attrName, null));
            }
        }

        element.ContentStart = _pos;
        CloseImplied(element.Lower, start);
        Current.Children.Add(element);

        if (element.SelfClosing || element.IsVoid) return;

        var raw = HtmlNames.RawText.Contains(element.Lower) && !(element.Lower == "title" && InForeignContent);
        if (element.Lower == "plaintext")
        {
            element.RawContent = _s[_pos..];
            _pos = _s.Length;
            element.ContentEnd = _pos;
            return;
        }
        if (raw)
        {
            ReadRawContent(element);
            return;
        }

        _stack.Add(element);
    }

    private string ReadAttributeValue()
    {
        if (_pos >= _s.Length) return "";
        var start = _pos;
        var quote = _s[_pos];
        if (quote is '"' or '\'')
        {
            var end = _s.IndexOf(quote, _pos + 1);
            _pos = end < 0 ? _s.Length : end + 1;
            return _s[start.._pos];
        }
        while (_pos < _s.Length && !char.IsWhiteSpace(_s[_pos]) && _s[_pos] != '>') _pos++;
        return _s[start.._pos];
    }

    private bool SkipWhitespace()
    {
        var start = _pos;
        while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        return _pos > start;
    }

    /// <summary>Reads the text of a script, style, textarea … up to its end tag.</summary>
    private void ReadRawContent(HElement element)
    {
        var end = FindEndTag(element.Lower, _pos);
        if (end < 0)
        {
            element.RawContent = _s[_pos..];
            element.ContentEnd = _s.Length;
            _pos = _s.Length;
            return;
        }

        element.RawContent = _s[_pos..end];
        element.ContentEnd = end;
        var close = _s.IndexOf('>', end);
        _pos = close < 0 ? _s.Length : close + 1;
        element.EndTag = _s[end.._pos];
    }

    private int FindEndTag(string name, int from)
    {
        var p = from;
        while (true)
        {
            var i = _s.IndexOf("</", p, StringComparison.Ordinal);
            if (i < 0) return -1;
            var after = i + 2 + name.Length;
            if (after <= _s.Length &&
                string.Compare(_s, i + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                (after == _s.Length || char.IsWhiteSpace(_s[after]) || _s[after] is '>' or '/'))
                return i;
            p = i + 2;
        }
    }

    /// <summary>Closes elements whose end tag may be left out, as a new start tag implies (simplified from the HTML standard).</summary>
    private void CloseImplied(string name, int at)
    {
        switch (name)
        {
            case "li":
                CloseUpTo(["li"], ["ul", "ol", "menu"], at);
                break;
            case "dt" or "dd":
                CloseUpTo(["dt", "dd"], ["dl"], at);
                break;
            case "tr":
                CloseUpTo(["tr"], ["table", "thead", "tbody", "tfoot"], at);
                break;
            case "td" or "th":
                CloseUpTo(["td", "th"], ["tr", "table"], at);
                break;
            case "thead" or "tbody" or "tfoot":
                CloseUpTo(["thead", "tbody", "tfoot"], ["table"], at);
                break;
            case "option":
                CloseUpTo(["option"], ["select", "datalist", "optgroup"], at);
                break;
            case "optgroup":
                CloseUpTo(["option", "optgroup"], ["select"], at);
                break;
            case "rt" or "rp":
                CloseUpTo(["rt", "rp"], ["ruby"], at);
                break;
            case "body":
                CloseUpTo(["head"], ["html"], at);
                break;
        }

        if (ClosesParagraph.Contains(name))
        {
            for (var k = _stack.Count - 1; k > 0; k--)
            {
                var lower = _stack[k].Lower;
                if (lower == "p")
                {
                    PopTo(k - 1, at);
                    return;
                }
                if (HtmlNames.Block.Contains(lower) || HtmlNames.Container.Contains(lower) || lower == "button") return;
            }
        }
    }

    private void CloseUpTo(string[] targets, string[] boundaries, int at)
    {
        for (var k = _stack.Count - 1; k > 0; k--)
        {
            var lower = _stack[k].Lower;
            if (targets.Contains(lower))
            {
                PopTo(k - 1, at);
                return;
            }
            if (boundaries.Contains(lower)) return;
        }
    }
}
