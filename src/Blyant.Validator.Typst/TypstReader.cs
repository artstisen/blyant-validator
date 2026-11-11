using System.Text;

namespace Blyant.Validator.Typst;

/// <summary>A mistake in the structure of a Typst file, at an offset of the text.</summary>
public sealed record TypstProblem(int Offset, string Code, string Message, bool IsError = true);

/// <summary>A label (&lt;name&gt;) or a reference (@name) in the markup, at an offset of the text.</summary>
/// <param name="InWord">For a reference: it follows a letter or digit (as in an e-mail address).</param>
/// <param name="InCode">For a label: it is in content that code places any number of times (the body of an #if, #for
/// or #while, of a #let or a #show rule), so it is there, but may be there once, never, or many times.</param>
public sealed record TypstName(string Name, int Offset, bool InWord = false, bool InCode = false);

/// <summary>A path given to Typst in the file: #include, #import, image(), read(), bibliography() ...</summary>
public sealed record TypstPath(string Path, int Offset, string Function);

/// <summary>
/// Reads a Typst file (https://typst.app/docs/reference/syntax/) as the compiler does, as far as the checks need it:
/// its three modes, markup (the text), code (after #, and in its brackets) and math (between $), and how each goes
/// into the others. It finds what Typst refuses to compile, and that can be told without compiling: raw text, strings,
/// equations and brackets left open, a bracket closed that was not opened, *strong* and _emphasis_ not closed before
/// the end of their paragraph, heading, list item or content, and "#" followed by no code. It collects the labels, the
/// references and the paths of the file, for the checks of <see cref="TypstValidator"/>. Code is not evaluated: what
/// a name is, or which arguments a function takes, is the compiler's to tell. It does not touch the file system.
/// </summary>
public sealed class TypstReader
{
    /// <summary>How deep brackets may be nested: deeper, the file is not read further (the reading recurses).</summary>
    public const int MaxDepth = 256;

    // The words after # that start a statement, which goes on to the end of its line (or a ";")
    private static readonly HashSet<string> Statements = new(StringComparer.Ordinal)
    {
        "let", "set", "show", "import", "include", "return", "break", "continue",
    };

    // The words after # that start an expression with a body in brackets: #if x [..] else [..], #for, #while
    private static readonly HashSet<string> Conditionals = new(StringComparer.Ordinal) { "if", "for", "while" };

    // The functions whose first argument is the path of a file
    private static readonly HashSet<string> PathFunctions = new(StringComparer.Ordinal)
    {
        "image", "read", "json", "csv", "yaml", "toml", "xml", "cbor", "bibliography", "plugin",
    };

    // The functions that make labels or references the reading cannot follow (from strings, from a bibliography)
    private static readonly HashSet<string> DynamicFunctions = new(StringComparer.Ordinal)
    {
        "label", "bibliography", "cite", "ref", "query", "locate",
    };

    private readonly string _s;
    private int _pos;
    private int _depth;
    private bool _tooDeep;
    private int _inCode; // Inside the body of an #if, #for, #while, #let or #show: labels there are not counted

    public List<TypstProblem> Problems { get; } = [];
    public List<TypstName> Labels { get; } = [];
    public List<TypstName> References { get; } = [];
    public List<TypstPath> Paths { get; } = [];

    /// <summary>
    /// True when the file makes labels or references the reading cannot follow: label("..."), a bibliography (its keys
    /// are referred to with @), cite, ref, query
    /// </summary>
    public bool HasDynamicLabels { get; private set; }

    /// <summary>
    /// True when the file includes or imports a file whose path is made by code (include c, include "ch-" + n), which
    /// the reading cannot follow: the files of its document are not known
    /// </summary>
    public bool HasDynamicIncludes { get; private set; }

    private TypstReader(string text) => _s = text;

    public static TypstReader Read(string text)
    {
        var reader = new TypstReader(text);
        reader.Markup(contentOpen: -1);
        return reader;
    }

    private char At(int i) => i >= 0 && i < _s.Length ? _s[i] : '\0';

    private void Error(int offset, string code, string message) => Problems.Add(new TypstProblem(offset, code, message));

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    private static bool IsLabelChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';

    /// <summary>
    /// The character at an offset (a letter outside the BMP is two chars) is a letter or digit of a word, as the Typst
    /// compiler tells it for "*" and "_" between two (5*3, snake_case): not the letters of Chinese, Japanese and Korean,
    /// where words are not separated by blanks
    /// </summary>
    private bool IsWordChar(int start, bool before)
    {
        var i = start;
        if (before && i >= 1 && char.IsLowSurrogate(At(i)) && char.IsHighSurrogate(At(i - 1))) i--;
        if (i < 0 || i >= _s.Length || !Rune.TryGetRuneAt(_s, i, out var rune)) return false;
        return Rune.IsLetterOrDigit(rune) && !IsCjk(rune.Value);
    }

    // Han, Hiragana, Katakana and Hangul (the scripts Typst's lexer leaves out of words)
    private static bool IsCjk(int c) =>
        c is >= 0x2E80 and <= 0x2FDF or 0x3005 or 0x3007 or >= 0x3021 and <= 0x3029 or >= 0x3038 and <= 0x303B
            or >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF or >= 0xFF66 and <= 0xFF9F or >= 0x20000 and <= 0x3FFFF
            or >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xA960 and <= 0xA97F or >= 0xAC00 and <= 0xD7FF
            or >= 0xFFA0 and <= 0xFFDC;

    // Brackets, content and code nested deeper than MaxDepth: told once, and the rest of the file is not read
    private bool Enter()
    {
        if (_depth < MaxDepth)
        {
            _depth++;
            return true;
        }

        if (!_tooDeep)
            Error(_pos, "typst.too-deep", $"Brackets are nested more than {MaxDepth} deep: the rest of the file is not checked.");
        _tooDeep = true;
        _pos = _s.Length;
        return false;
    }

    #region Markup

    // The markup of the file (contentOpen -1), or of content in [ ] (contentOpen: where its "[" is), to its "]"
    private void Markup(int contentOpen)
    {
        if (!Enter()) return;
        var strong = -1; // Where an open *strong* starts
        var emphasis = -1; // Where an open _emphasis_ starts
        var heading = false; // On the line of a heading
        var lineStart = true; // Before the first character of a line that is not blank
        var brackets = 0; // "[" in the text, which a "]" closes before it closes the content

        void EndBlock()
        {
            if (strong >= 0) UnclosedDelimiter(strong);
            if (emphasis >= 0) UnclosedDelimiter(emphasis);
            strong = emphasis = -1;
            heading = false;
        }

        while (_pos < _s.Length)
        {
            var c = _s[_pos];
            if (lineStart && c is ' ' or '\t')
            {
                _pos++;
                continue;
            }

            if (lineStart && c is not ('\n' or '\r'))
            {
                lineStart = false;
                if (LineMarker(out var isHeading))
                {
                    heading = isHeading;
                    continue;
                }
            }

            switch (c)
            {
                case '\n' or '\r':
                    _pos += c == '\r' && At(_pos + 1) == '\n' ? 2 : 1;
                    // A heading ends with its line, unless strong text or emphasis in it goes on to the next; a
                    // paragraph or list item with a blank line, or a line that starts a heading or a list item
                    if (heading && strong < 0 && emphasis < 0 || NextLineEndsBlock()) EndBlock();
                    lineStart = true;
                    break;
                case '\\':
                    // An escape (\*, \u{1f600}); before the end of a line, a line break (the line ends as any other)
                    _pos += At(_pos + 1) is '\n' or '\r' ? 1 : 2;
                    break;
                case '/' when At(_pos + 1) == '/':
                    LineComment();
                    break;
                case '/' when At(_pos + 1) == '*':
                    BlockComment();
                    break;
                case '`':
                    Raw();
                    break;
                case '$':
                    Math();
                    break;
                case '#':
                    Embedded();
                    break;
                case '*' or '_':
                    // A mark between two letters or digits is text (5*3, snake_case); else it opens or closes
                    if (IsWordChar(_pos - 1, before: true) && IsWordChar(_pos + 1, before: false))
                    {
                        _pos++;
                        break;
                    }

                    if (c == '*') strong = strong >= 0 ? -1 : _pos;
                    else emphasis = emphasis >= 0 ? -1 : _pos;
                    _pos++;
                    break;
                case '<':
                    if (!Label()) _pos++;
                    break;
                case '@':
                    Reference();
                    break;
                case 'h' when Link():
                    break;
                case '[':
                    brackets++;
                    _pos++;
                    break;
                case ']':
                    if (brackets > 0)
                    {
                        brackets--;
                        _pos++;
                        break;
                    }

                    if (contentOpen >= 0)
                    {
                        // The end of the content
                        EndBlock();
                        _pos++;
                        _depth--;
                        return;
                    }

                    Error(_pos, "typst.unexpected-close",
                        "Unexpected “]”: there is no “[” to close. Write \\] for a bracket in the text.");
                    _pos++;
                    break;
                default:
                    _pos++;
                    break;
            }
        }

        EndBlock();
        if (contentOpen >= 0) UnclosedBracket(contentOpen, '[');
        _depth--;
    }

    // (when the file is too deep it is not read to its end: what is open then is not told)
    private void UnclosedDelimiter(int at)
    {
        if (_tooDeep) return;
        var mark = _s[at];
        Error(at, "typst.unclosed-delimiter", mark == '*'
            ? "“*” starts strong text that is never closed (before the end of its paragraph, heading, list item or content). Write \\* for an asterisk."
            : "“_” starts emphasis that is never closed (before the end of its paragraph, heading, list item or content). Write \\_ for an underscore.");
    }

    private void UnclosedBracket(int at, char open)
    {
        if (_tooDeep) return;
        var close = open switch { '(' => ')', '{' => '}', _ => ']' };
        Error(at, "typst.unclosed-delimiter", $"“{open}” is never closed (missing “{close}”).");
    }

    // At the start of a line: a heading ("== "), or the marker of a list ("- ", "+ ", "1. ", "/ Term:"). A "=" right
    // before a word is text, as Typst reads it, but most likely a heading without its space
    private bool LineMarker(out bool heading)
    {
        heading = false;
        var c = _s[_pos];
        if (c == '=')
        {
            var end = _pos;
            while (At(end) == '=') end++;
            var after = At(end);
            if (after is ' ' or '\t' or '\n' or '\r' or '\0')
            {
                _pos = end;
                heading = true;
                return true;
            }

            if (char.IsLetterOrDigit(after))
                Problems.Add(new TypstProblem(_pos, "typst.heading-space",
                    $"“{_s[_pos..end]}{after}” is text: a heading needs a space after its “{_s[_pos..end]}”.", IsError: false));
            return false;
        }

        if (c is '-' or '+' && At(_pos + 1) is ' ' or '\t' or '\n' or '\r' or '\0' ||
            c == '/' && At(_pos + 1) == ' ')
        {
            _pos++;
            return true;
        }

        if (char.IsAsciiDigit(c))
        {
            var end = _pos;
            while (char.IsAsciiDigit(At(end))) end++;
            if (At(end) == '.' && At(end + 1) is ' ' or '\t' or '\n' or '\r' or '\0')
            {
                _pos = end + 1;
                return true;
            }
        }

        return false;
    }

    // After a line break: the next line is blank, or starts a heading or a list item (it ends the paragraph)
    private bool NextLineEndsBlock()
    {
        var i = _pos;
        while (At(i) is ' ' or '\t') i++;
        var c = At(i);
        if (c is '\n' or '\r' or '\0') return true;
        if (c == '=')
        {
            while (At(i) == '=') i++;
            return At(i) is ' ' or '\t' or '\n' or '\r' or '\0';
        }

        if (c is '-' or '+') return At(i + 1) is ' ' or '\t' or '\n' or '\r' or '\0';
        if (c == '/') return At(i + 1) == ' ';
        if (!char.IsAsciiDigit(c)) return false;
        while (char.IsAsciiDigit(At(i))) i++;
        return At(i) == '.' && At(i + 1) is ' ' or '\t' or '\n' or '\r' or '\0';
    }

    // <label>
    private bool Label()
    {
        var end = _pos + 1;
        while (IsLabelChar(At(end))) end++;
        if (end == _pos + 1 || At(end) != '>') return false;
        Labels.Add(new TypstName(_s[(_pos + 1)..end], _pos, InCode: _inCode > 0));
        _pos = end + 1;
        return true;
    }

    // @reference (also inside a word: Typst reads the at sign of an e-mail address as a reference), with its
    // supplement in [ ]
    private void Reference()
    {
        var end = _pos + 1;
        while (IsLabelChar(At(end))) end++;
        while (end > _pos + 1 && At(end - 1) is '.' or ':') end--;
        if (end == _pos + 1)
        {
            _pos++;
            return;
        }

        References.Add(new TypstName(_s[(_pos + 1)..end], _pos, char.IsLetterOrDigit(At(_pos - 1))));
        _pos = end;
        if (At(_pos) == '[')
        {
            _pos++;
            Markup(_pos - 1);
        }
    }

    // A web address in the text (its "//" is not a comment, nor its "_" emphasis): to a blank, with its brackets in
    // pairs, and without the punctuation at its end
    private bool Link()
    {
        if (string.CompareOrdinal(_s, _pos, "https://", 0, 8) != 0 && string.CompareOrdinal(_s, _pos, "http://", 0, 7) != 0)
            return false;
        var end = _pos;
        var open = 0;
        while (end < _s.Length && !char.IsWhiteSpace(_s[end]) && _s[end] is not ('<' or '>'))
        {
            if (_s[end] is '(' or '[') open++;
            else if (_s[end] is ')' or ']' && --open < 0) break;
            end++;
        }

        while (end > _pos && _s[end - 1] is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"') end--;
        _pos = end;
        return true;
    }

    #endregion

    #region What the modes share

    private void LineComment()
    {
        while (_pos < _s.Length && _s[_pos] is not ('\n' or '\r')) _pos++;
    }

    // /* comment */, which can hold comments of its own. One that is never closed goes to the end (Typst allows it)
    private void BlockComment()
    {
        _pos += 2;
        var open = 1;
        while (_pos < _s.Length && open > 0)
        {
            if (_s[_pos] == '/' && At(_pos + 1) == '*')
            {
                open++;
                _pos += 2;
            }
            else if (_s[_pos] == '*' && At(_pos + 1) == '/')
            {
                open--;
                _pos += 2;
            }
            else
            {
                _pos++;
            }
        }
    }

    // `raw`, `` (empty), and ``` blocks, to the same number of backticks (also over several lines)
    private void Raw()
    {
        var start = _pos;
        var ticks = 0;
        while (At(start + ticks) == '`') ticks++;
        if (ticks == 2)
        {
            _pos += 2;
            return;
        }

        var mark = new string('`', ticks);
        var close = _s.IndexOf(mark, start + ticks, StringComparison.Ordinal);
        if (close < 0)
        {
            Error(start, "typst.unclosed-raw", ticks >= 3
                ? $"The raw block opened with “{mark}” is never closed."
                : "“`” starts raw text that is never closed. Write \\` for a backtick.");
            _pos = _s.Length;
            return;
        }

        _pos = close + ticks;
        // (a block is closed by a run of backticks at least as long)
        if (ticks >= 3)
        {
            while (At(_pos) == '`') _pos++;
        }
    }

    // "text", with escapes (\"), also over several lines
    private void QuotedString(out string? value)
    {
        var start = _pos;
        _pos++;
        var text = new System.Text.StringBuilder();
        while (_pos < _s.Length && _s[_pos] != '"')
        {
            if (_s[_pos] == '\\' && _pos + 1 < _s.Length)
            {
                var escaped = _s[_pos + 1];
                _pos += 2;
                if (escaped == 'u' && At(_pos) == '{')
                {
                    // \u{1f600}: a character by its number
                    var close = _s.IndexOf('}', _pos);
                    if (close > _pos && int.TryParse(_s.AsSpan(_pos + 1, close - _pos - 1), System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out var number) && Rune.IsValid(number))
                    {
                        text.Append(char.ConvertFromUtf32(number));
                        _pos = close + 1;
                        continue;
                    }
                }

                text.Append(escaped switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var other => other });
                continue;
            }

            text.Append(_s[_pos]);
            _pos++;
        }

        if (_pos >= _s.Length)
        {
            Error(start, "typst.unclosed-string", "The string is never closed (missing “\"”).");
            value = null;
            return;
        }

        _pos++;
        value = text.ToString();
    }

    // $math$, to the "$" that ends it. Brackets in math do not need to pair ($[a, b)$); code goes in after #
    private void Math()
    {
        var start = _pos;
        _pos++;
        if (!Enter()) return;
        while (_pos < _s.Length)
        {
            var c = _s[_pos];
            switch (c)
            {
                case '\\':
                    _pos += 2;
                    break;
                case '$':
                    _pos++;
                    _depth--;
                    return;
                case '"':
                    QuotedString(out _);
                    break;
                case '/' when At(_pos + 1) == '/':
                    LineComment();
                    break;
                case '/' when At(_pos + 1) == '*':
                    BlockComment();
                    break;
                case '#':
                    Embedded();
                    break;
                default:
                    _pos++;
                    break;
            }
        }

        if (!_tooDeep)
            Error(start, "typst.unclosed-delimiter", "“$” starts an equation that is never closed (missing “$”). Write \\$ for a dollar sign.");
        _depth--;
    }

    #endregion

    #region Code

    // # in markup or math: a statement (#let x = 1, to the end of its line), a name with its fields and calls
    // (#calc.pi, #figure(..)[..]), or code, content or a string in brackets (#(1 + 2), #{ .. }, #[ .. ], #"text")
    private void Embedded()
    {
        var hash = _pos;
        _pos++;
        var next = At(_pos);
        if (IsIdentStart(next))
        {
            var name = Identifier();
            if (Statements.Contains(name))
            {
                Statement(name);
                return;
            }

            if (Conditionals.Contains(name))
            {
                Conditional();
                return;
            }

            if (name == "context")
            {
                // context takes one expression, and the text goes on after it ("(p. #context counter(page).get())")
                while (At(_pos) is ' ' or '\t') _pos++;
                Expression();
                return;
            }

            Postfix(name);
            return;
        }

        switch (next)
        {
            case '(' or '{':
                _pos++;
                Code(next, _pos - 1);
                Postfix(null);
                return;
            case '[':
                _pos++;
                Markup(_pos - 1);
                return;
            case '"':
                QuotedString(out _);
                return;
            case ' ' or '\t' or '\n' or '\r' or '\0':
                Error(hash, "typst.expected-expression", "“#” starts code, but no code follows it. Write \\# for a number sign.");
                return;
        }

        if (char.IsAsciiDigit(next))
        {
            while (char.IsLetterOrDigit(At(_pos)) || At(_pos) == '.' && char.IsAsciiDigit(At(_pos + 1))) _pos++;
        }
    }

    // One expression of code in the text: a name with its fields and calls, or code or content in brackets
    private void Expression()
    {
        var c = At(_pos);
        if (IsIdentStart(c))
        {
            var name = Identifier();
            if (Conditionals.Contains(name)) Conditional();
            else Postfix(name);
            return;
        }

        switch (c)
        {
            case '(' or '{':
                _pos++;
                Code(c, _pos - 1);
                Postfix(null);
                break;
            case '[':
                _pos++;
                Markup(_pos - 1);
                break;
            case '"':
                QuotedString(out _);
                break;
        }
    }

    // #if, #for or #while, after its word: the condition (or "x in list") to its body in [ ] or { }, and for #if an
    // "else" with another body or #if (on the same line, or the next)
    private void Conditional()
    {
        _inCode++;
        try
        {
            ConditionalBodies();
        }
        finally
        {
            _inCode--;
        }
    }

    private void ConditionalBodies()
    {
        while (_pos < _s.Length)
        {
            var c = _s[_pos];
            switch (c)
            {
                case '\n' or '\r':
                    // (no body: the compiler tells what is wrong)
                    return;
                case '[':
                    _pos++;
                    Markup(_pos - 1);
                    if (!Else()) return;
                    continue;
                case '{':
                    _pos++;
                    Code('{', _pos - 1);
                    if (!Else()) return;
                    continue;
                case '(':
                    _pos++;
                    Code('(', _pos - 1);
                    continue;
                case '"':
                    QuotedString(out _);
                    continue;
                case '/' when At(_pos + 1) == '/':
                    LineComment();
                    continue;
                case '/' when At(_pos + 1) == '*':
                    BlockComment();
                    continue;
                default:
                    _pos++;
                    continue;
            }
        }
    }

    // After the body of an #if: "else" ahead (on the same line or the next) is taken, and its body (or "if") follows
    private bool Else()
    {
        var i = _pos;
        while (At(i) is ' ' or '\t') i++;
        if (At(i) is '\n' or '\r')
        {
            i += At(i) == '\r' && At(i + 1) == '\n' ? 2 : 1;
            while (At(i) is ' ' or '\t') i++;
        }

        if (string.CompareOrdinal(_s, i, "else", 0, 4) != 0 || IsIdentPart(At(i + 4))) return false;
        _pos = i + 4;
        while (At(_pos) is ' ' or '\t') _pos++;
        if (string.CompareOrdinal(_s, _pos, "if", 0, 2) == 0 && !IsIdentPart(At(_pos + 2))) _pos += 2;
        return true;
    }

    // A name's fields and calls right after it: .field, (arguments), [content]
    private void Postfix(string? name)
    {
        while (true)
        {
            var c = At(_pos);
            if (c == '.' && IsIdentStart(At(_pos + 1)))
            {
                _pos++;
                name = Identifier();
            }
            else if (c == '(')
            {
                _pos++;
                CallArguments(name);
            }
            else if (c == '[')
            {
                _pos++;
                Markup(_pos - 1);
            }
            else
            {
                return;
            }
        }
    }

    // The arguments of a call, after its "(": a path given to a function that reads a file is noted
    private void CallArguments(string? name)
    {
        var open = _pos - 1;
        if (name != null)
        {
            if (DynamicFunctions.Contains(name)) HasDynamicLabels = true;
            if (PathFunctions.Contains(name)) NotePath(name);
        }

        Code('(', open);
    }

    /// <summary>
    /// A string right ahead (after blanks) as the path of a file, for a function or for import and include: only a
    /// string on its own, not one that code adds to ("fig-" + n + ".png"). For import and include, a path that is not
    /// such a string is made by code, and cannot be followed (<see cref="HasDynamicIncludes"/>)
    /// </summary>
    private void NotePath(string function)
    {
        var i = _pos;
        while (At(i) is ' ' or '\t') i++;
        var path = At(i) == '"' ? StringOnItsOwn(i) : null;
        if (path != null) Paths.Add(new TypstPath(path, i, function));
        else if (function is "import" or "include") HasDynamicIncludes = true;
    }

    // The value of the string at an offset when no code follows it (an operator, a field or a call): else null
    private string? StringOnItsOwn(int at)
    {
        var save = _pos;
        _pos = at;
        QuotedString(out var path);
        var after = _pos;
        _pos = save;
        while (At(after) is ' ' or '\t') after++;
        var operation = At(after) is '+' or '*' or '.' or '(' or '[' or '=' or '<' or '>' or '!' or '-' ||
                        At(after) == '/' && At(after + 1) is not ('/' or '*');
        return operation ? null : path;
    }

    private string Identifier()
    {
        var start = _pos;
        while (IsIdentPart(At(_pos)) && !(At(_pos) == '-' && !IsIdentPart(At(_pos + 1)))) _pos++;
        return _s[start.._pos];
    }

    // A statement after # (#let, #set, #show, #import ...): code to the end of its line, or a ";", or the "]" of the
    // content it is in
    private void Statement(string keyword)
    {
        if (keyword is "import" or "include") NotePath(keyword);
        var placed = keyword is "let" or "show" or "set";
        if (placed) _inCode++;
        Code('\n', _pos);
        if (placed) _inCode--;
    }

    // Code to its closing bracket (closer "(" or "{", opened at "open"), or a statement (closer '\n')
    private void Code(char opener, int open)
    {
        if (!Enter()) return;
        var statement = opener == '\n';
        var closer = opener switch { '(' => ')', '{' => '}', _ => '\n' };
        while (_pos < _s.Length)
        {
            var c = _s[_pos];
            if (statement)
            {
                if (c is '\n' or '\r')
                {
                    _depth--;
                    return;
                }

                if (c == ';')
                {
                    _pos++;
                    _depth--;
                    return;
                }

                if (c == ']')
                {
                    // The end of the content the statement is in (#box[#let x = 1])
                    _depth--;
                    return;
                }
            }

            switch (c)
            {
                case '/' when At(_pos + 1) == '/':
                    LineComment();
                    break;
                case '/' when At(_pos + 1) == '*':
                    BlockComment();
                    break;
                case '"':
                    QuotedString(out _);
                    break;
                case '`':
                    Raw();
                    break;
                case '$':
                    Math();
                    break;
                case '[':
                    _pos++;
                    Markup(_pos - 1);
                    break;
                case '(' or '{':
                    _pos++;
                    Code(c, _pos - 1);
                    break;
                case ')' or '}' or ']':
                    if (c == closer)
                    {
                        _pos++;
                        _depth--;
                        return;
                    }

                    Error(_pos, "typst.unexpected-close", statement
                        ? $"Unexpected “{c}”: there is no “{Opening(c)}” to close."
                        : $"Unexpected “{c}”: “{opener}” is closed with “{closer}”.");
                    _pos++;
                    break;
                default:
                    if (IsIdentStart(c) && !IsIdentPart(At(_pos - 1)))
                    {
                        var name = Identifier();
                        if (name is "import" or "include") NotePath(name);
                        else if (At(_pos) == '(') CallArgumentsAhead(name);
                        else if (DynamicFunctions.Contains(name)) HasDynamicLabels = true;
                        break;
                    }

                    _pos++;
                    break;
            }
        }

        if (!statement) UnclosedBracket(open, opener);
        _depth--;
    }

    private void CallArgumentsAhead(string name)
    {
        if (DynamicFunctions.Contains(name)) HasDynamicLabels = true;
        if (PathFunctions.Contains(name))
        {
            _pos++;
            NotePath(name);
            _pos--;
        }
    }

    private static char Opening(char close) => close switch { ')' => '(', '}' => '{', _ => '[' };

    #endregion
}
