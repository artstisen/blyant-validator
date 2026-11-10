# Blyant Validator

A cross-platform command-line tool that validates and formats HTML and CSS files, and validates XML files such as sitemaps, and Markdown files such as llms.txt. It's written in
C#/.NET 10 and designed as an optional companion to the [Blyant editor](https://blyant-editor.com/),
but it works on its own too.

> **Status: early development (0.4.0).** CSS validation and both formatters work. HTML validation
> reports parse errors and has a small starter rule set; more rules will follow.
>
> Blyant Validator has no dependencies besides .NET.

![The TUI: the files of a site with their results, and the peek of a file going from problem to problem](docs/screenshots/peek-problem.svg)

## Credits

Blyant Validator is modelled on two excellent open-source projects:

- **The Nu Html Checker (v.Nu)**: <https://github.com/validator/validator>
- **The W3C CSS Validation Service**: <https://github.com/w3c/css-validator>

See [CREDITS.md](CREDITS.md) for details, and run `blyant-validator credits`.

## Usage

```
blyant-validator                          (the TUI, where it was left last)
blyant-validator browse   [folder] [options]
blyant-validator validate <paths>... [options]
blyant-validator format   <paths>... [options]
blyant-validator credits
```

### The TUI

Started without a command (or with `browse`), Blyant Validator is a file browser in the terminal, in the style of
the file browser of the Blyant editor: one or two panes, and a peek of the selected file in the other pane.

![The files of a site in two panes, with the result of each file](docs/screenshots/file-browser.svg)

- Each HTML, CSS, XML and Markdown file shows its result: `ok`, the number of errors and warnings (`3E 1W`), `...` while it is
  checked, or `failed`. Files are checked in the background, and checked again when they change on disk, so the
  list stays up to date while you edit the files in another program.
- `V` peeks at the selected file: its text in colors, with a mark (`E`, `W`, `i`) in front of the lines with
  problems and the place of each error highlighted. `P` shows the list of its problems instead.
- `Tab` goes into the peek: `j`/`k` move, `n`/`N` go to the next and previous problem (its message is shown on
  the bottom line), `/` searches, `h`/`l` scroll sideways, and in the list of problems `Enter` shows a problem in
  the file. `Tab` or `Esc` goes back to the list.
- `I` shows the full text of the problems on the line (in the list of problems: of the selected one) in a box, for
  messages longer than the window is wide. A message that is cut on the bottom line ends with "(I: all)".
- As in Vim, `n`/`N` follow what you looked for last: after a `/` search they go to the matches, and `]`/`[` (which
  always go to problems) make them go to the problems again.
- `F` formats the selected file, or the files marked with `Space`, after asking.
- `T` shows one pane or two, `/` filters the list, `:` opens the command line (`:cd`, `:engine vnu`, `:all` to
  list all files …, with `Tab` completion), and `?` shows all keys. `q` or `Esc` quits, after asking (as in
  Blyant); Ctrl+C quits at once.
- `:path` copies the full path of the selected file or folder to the clipboard, and `:name` its name (on `..`, the
  name of the folder shown), as in Blyant. It is sent with the
  terminal's clipboard sequence (OSC 52), so it works over SSH too, and no other program is started. Most terminals
  take it (Windows Terminal, kitty, WezTerm, Alacritty, iTerm2 …); the Terminal of macOS, GNOME Terminal and the
  Linux console do not, and are told about.

The TUI needs a window of at least 80×24, like Blyant.

It remembers where it was: the folder and the selected file of both panes, the pane with the focus, and whether one
or two panes were shown. `blyant-validator` (or `browse` without a folder) starts there again; `browse <folder>`
shows that folder in the pane with the focus. The first time, it starts in the current directory. This is kept in
`settings.ini`, in Blyant's format, in the folder `data` next to the program (or, if that folder cannot be written
to, in `Blyant Validator/data` in your application data folder). As in Blyant, a folder `data` with a
`settings.ini` in the directory the program is started in is used instead, for a portable setup.

### Screenshots

The screenshots are of the real program, made with [tools/screenshots.py](tools/screenshots.py) from a made-up
site with mistakes on purpose, as Blyant's are.

| | |
|---|---|
| ![The files of a site with their results](docs/screenshots/file-browser.svg) | ![The peek of a file](docs/screenshots/peek.svg) |
| The files of a site in two panes, each with its result | `V`: the peek, with the lines that have problems marked and each error's place highlighted |
| ![From problem to problem](docs/screenshots/peek-problem.svg) | ![The problems of a file](docs/screenshots/problems.svg) |
| `Tab` into the peek, and `n` to the next problem: its message on the bottom line | `P`: the problems of the file; `Enter` shows one in the file |
| ![A style sheet in the peek](docs/screenshots/css.svg) | ![Formatting a file](docs/screenshots/format.svg) |
| A style sheet: an unknown property and an unknown unit | `F`: formatting a file, after asking |
| ![A sitemap](docs/screenshots/sitemap.svg) | ![llms.txt](docs/screenshots/llms-txt.svg) |
| A sitemap, checked by the rules of sitemaps.org | `llms.txt`, checked against the llms.txt format |

Paths can be files or directories. Directories are searched for `.html`, `.htm`, `.xhtml`, `.css`, `.xml`, `.svg`, `.md` and `.markdown` files, and `llms.txt` and `llms-full.txt`;
add `--recursive` to include subdirectories.

### validate

| Option | Description |
| --- | --- |
| `-e, --engine native\|vnu` | `native` (default) uses the built-in checker. `vnu` runs the Nu Html Checker; see below. |
| `-o, --output text\|json` | `text` (default) prints `file:line:column: severity: message [code]`. `json` is for editors and scripts. |
| `-r, --recursive` | Search directories recursively. |
| `--vnu-jar <path>` | Path to `vnu.jar` (or set `BLYANT_VNU_JAR`). |
| `--java <path>` | Path to `java` (default: `JAVA_HOME`, then `PATH`). |

### format

| Option | Description |
| --- | --- |
| `-w, --write` | Overwrite the files instead of printing the result. |
| `--check` | Change nothing; exit with code 1 if a file isn't formatted. |
| `--indent-size <n>` | Spaces per indentation level (default 2). |
| `--use-tabs` | Indent with tabs. |
| `--line-width <n>` | HTML elements whose content fits within this width stay on one line (default 100). |

Without `--write` or `--check`, the formatted text is printed. `--write` only touches files that change, and
replaces them safely (a temporary file is written first), keeping their encoding and line endings.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | No errors found (warnings are allowed). |
| 1 | Errors found, or `format --check` found unformatted files. |
| 2 | The tool itself failed: bad arguments, missing files, vnu not available, or a file could not be formatted. |

## What is checked

### CSS

- **Syntax**, following CSS Syntax Level 3: unclosed blocks, strings, comments and `url()`, missing `:`,
  stray `}`.
- **Property names**: unknown properties are errors, with a suggestion for typos ("Did you mean
  “background-color”?"). Vendor-prefixed names (`-webkit-…`) are reported as info.
- **Property values**, checked against grammars written in the notation of the CSS specifications
  (`src/Blyant.Validator.Css/Data/properties.txt`), with ranges such as “no negative padding”. Values
  that use `var()`, `env()` or `attr()` are not checked: a false error is worse than a missed one. 469 of the
  497 known properties have a grammar, grid templates (`repeat()`, `minmax()`, line names, `subgrid`),
  `content`, masks, SVG strokes and scroll-driven animations included; the rest are new or rare features
  whose values are not checked yet (anchor positioning areas, `offset-*`, `mask-border-*` …).
- **Units**: unknown units (`10pz`) are errors everywhere.
- **Selectors**: unknown pseudo-classes and pseudo-elements, IDs starting with a digit, misplaced
  combinators, empty selectors. Nested rules (CSS Nesting) are supported.
- **At-rules**: unknown at-rules (warning), `@charset` and `@import` in the wrong place, `@font-face`
  descriptors, keyframe selectors.

### HTML

- **Parse errors**, as the HTML standard defines them: missing or obsolete doctype, unclosed elements,
  stray end tags (`</span>` with no `<span>`, `</p>` with no `<p>`, `</br>`), duplicate attributes, missing
  space between attributes, bad characters in unquoted values, `/>` on non-void elements, nested `<a>`,
  `<form>` and `<button>`, text directly inside tables, broken comments (`<!-->`, `--!>`, nested `<!--`),
  CDATA and `<?…?>` in HTML, bad character references (`&#xZZ;`, `&#0;`, `&copy` without `;`), and
  the end of the file inside a tag, comment or element. End tags that HTML lets you leave out (`</li>`,
  `</p>`, `</td>` …) are not reported.
- **Elements and attributes**: unknown elements (custom elements with a `-` are allowed) and unknown
  attributes (`data-*`, `aria-*` and event handlers are allowed); wrong values of boolean attributes
  (`disabled="false"`), of attributes with a set of values (`type`, `method`, `loading`, `dir` …), of numbers
  (`width="100px"`), IDs and URLs (spaces, backslashes); required attributes (`src` on `<img>`, `rel` and
  `href` on `<link>`, `content` on `<meta name>` …); `<label for>` pointing at no element.
- **Obsolete elements and attributes**: `<center>`, `<font>`, `<marquee>`, `align`, `bgcolor`, `cellpadding`,
  `<a name>` … with what to use instead; `border="0"` on images, `border="1"` on tables and an unneeded `type`
  on `<script>` and `<style>` are warnings.
- **Content models**: what lists, definition lists, tables, `<select>`, `<head>` and `<picture>` may hold; where
  `<li>`, `<dt>`/`<dd>`, table parts, `<option>`, `<figcaption>`, `<legend>`, `<summary>` and `<source>` may be;
  blocks inside inline elements (`<div>` in `<span>`, also through `<a>`); links and buttons inside links and
  buttons; text where only elements may be; more than one visible `<main>`.
- **CSS in the page**: `<style>` elements and `style` attributes are checked with the CSS validator, with the
  places of the problems in the HTML file.
- Missing `<title>` and `lang`, `<img>` without `alt`, duplicate IDs.

SVG and MathML inside a page are only checked for parse errors.

### XML

- **Well-formedness** of `.xml` and `.svg` files (XML 1.0): unclosed and mismatched tags, duplicate attributes,
  a bare `&`, HTML entities such as `&nbsp;` that XML does not know, more than one root element … The first
  mistake is reported, at its line and column (XML stops there, as browsers do). DTDs are ignored and nothing outside
  the file is ever loaded.
- **Sitemaps** (`urlset` and `sitemapindex`), by the rules of [sitemaps.org](https://www.sitemaps.org/protocol.html):
  the namespace, UTF-8, a required `<loc>` with an absolute http(s) URL of at most 2,048 characters, `lastmod` as a
  W3C date (`2026-11-10`), `changefreq` and `priority` values, unknown or repeated elements, at most 50,000 entries
  and 50 MB; URLs listed twice and URLs of another host are warnings. Extensions (`image:`, `video:`, `news:`,
  `xhtml:link`) are allowed.
- **News feeds**: RSS 2.0 (the [RSS Advisory Board's spec](https://www.rssboard.org/rss-specification)): `version`,
  one `channel` with `title`, `link` and `description`, items with a `title` or `description`, RFC 822 dates
  (`Tue, 10 Nov 2026 14:30:00 GMT`; a wrong weekday is a warning), absolute links, a `guid` that is a URL unless
  `isPermaLink="false"`, `enclosure`, `ttl`, `image`, `skipHours` and `skipDays`. Atom (RFC 4287): `id`, `title`
  and `updated` on the feed and its entries, an author, RFC 3339 dates (`2026-11-10T14:30:00Z`), ids as absolute
  IRIs, `link href`, content or an alternate link. Extensions in a namespace (`atom:link`, `content:encoded`,
  `dc:creator`, `itunes:*`) are allowed. Feeds can be `.xml`, `.rss` or `.atom` files.
- XML is checked natively also with `--engine vnu`.

### Markdown and llms.txt

Markdown has no invalid documents, so its checks are warnings, about what probably does not show as meant:

- A code block (````` or `~~~`) that is never closed, which makes the rest of the file code.
- `#Heading` without a space (not a heading), empty headings, more than one level 1 heading, and levels that skip
  one (`#` to `###`).
- Links: `[text](` not closed on its line, empty targets `[text]()`, references `[text][label]` with no
  `[label]: url`, labels defined twice, `#anchors` with no heading (or `id`) of that name in the file, and relative
  links and images to files that do not exist.

Code (fenced and indented), code spans and front matter are not checked. Web addresses are not visited: only the
`#anchors` of the file and relative paths on the disk are looked up.

`llms.txt` (and only that name: other `.txt` files are not checked) is also checked against the
[llms.txt format](https://llmstxt.org/), whose rules are built in, so nothing is fetched: it starts with one `# Name`,
(optionally) a `> summary` and some text, then `##` sections that are lists of links `- [name](url): notes`.
Breaking the format is an error; a URL listed twice is a warning. `llms-full.txt` is checked as Markdown, with any
number of level 1 headings.

## Formatting

Formatting only changes layout; the formatted file means exactly the same as before.

- **CSS**: one selector and one declaration per line, blocks indented, `: ` after property names,
  whitespace inside values made single spaces, and a missing last `;` added. Comments are kept, and so is
  one empty line where the file had empty lines. Custom properties (`--name`) keep their value exactly as
  written. A file with a block that is never closed is not formatted.
- **XML**: elements that hold only elements get their children on lines of their own, indented. An element with
  text in it (a `<loc>`, a `<title>`, SVG `<text>`, mixed content) is kept exactly as written, and so is everything
  under `xml:space="preserve"`. Comments, CDATA, the declaration and the doctype are kept, and one empty line where the
  file had empty lines. Runs of spaces between attributes become one space. A file that is not well-formed is not
  formatted.
- **HTML**: block-level elements (`div`, `p`, `li`, `table` …) get lines of their own and are indented.
  Text and inline elements stay together, keeping the line breaks of the source; elements whose content
  fits within `--line-width` stay on one line. Whitespace is only added or removed where the browser
  ignores it. The contents of `<pre>`, `<textarea>` and `<script>` are kept exactly as written; `<style>`
  elements are formatted as CSS. End tags that the source leaves out (`</li>`, `</td>`, `</p>` …) are
  not added.

### JSON output

```json
{
  "diagnostics": [
    {
      "file": "index.html",
      "severity": "error",
      "code": "html.img-missing-alt",
      "message": "An “img” element must have an “alt” attribute, except under certain conditions.",
      "line": 12, "column": 5, "endLine": 12, "endColumn": 5,
      "engine": "native"
    }
  ]
}
```

### Using the Nu Html Checker (`--engine vnu`)

For the most complete HTML checking, Blyant Validator can run the Nu Html Checker itself:

1. Install a Java runtime.
2. Download `vnu.jar` from <https://github.com/validator/validator/releases>.
3. Run `blyant-validator validate index.html --engine vnu --vnu-jar path/to/vnu.jar`,
   or set `BLYANT_VNU_JAR` once and leave out `--vnu-jar`.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
dotnet run --project src/Blyant.Validator.Cli -- validate index.html
```

On Windows the program has an icon in the style of Blyant's, with a checkmark:
`src/Blyant.Validator.Cli/BlyantValidator.ico` (and `.svg`), drawn by `python tools/icon.py` (needs Pillow; only to
draw the icon, not to build).

To publish a single self-contained executable (no .NET install needed on the target machine), pick a
runtime identifier such as `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` or `osx-arm64`:

```
dotnet publish src/Blyant.Validator.Cli -c Release -r win-x64 --self-contained
```

## Project layout

| Project | Purpose |
| --- | --- |
| `src/Blyant.Validator.Core` | Shared types: `Diagnostic`, `IValidator`, `IFormatter`, credits. |
| `src/Blyant.Validator.Html` | HTML reader with parse errors (`Parsing/`), validator rules (`Rules/`) and formatter. |
| `src/Blyant.Validator.Css` | CSS tokenizer and parser (`Syntax/`), value grammars (`Values/`, `Data/`), validator and formatter. |
| `src/Blyant.Validator.Xml` | XML validator: well-formedness and sitemaps. |
| `src/Blyant.Validator.Markdown` | Markdown reader and validator, and the llms.txt format. |
| `src/Blyant.Validator.Vnu` | Adapter that runs `vnu.jar` and converts its JSON output. |
| `src/Blyant.Validator.Cli` | The `blyant-validator` program: the commands, and the TUI in `Tui/` (its terminal parts in `Tui/Terminal/` and the highlighters in `Tui/Highlighting/` are adapted from Blyant). |
| `tests/Blyant.Validator.Tests` | xUnit tests. |

Rules for working on the code are in [AGENTS.md](AGENTS.md); changes are listed in [CHANGELOG.md](CHANGELOG.md).

## Roadmap

- [x] Solution structure, CLI, text and JSON output
- [x] HTML: parse errors, plus rules for title, lang, img alt and duplicate IDs
- [x] Optional `--engine vnu` backend
- [x] CSS: tokenizer and parser following CSS Syntax Level 3
- [x] CSS: property, value, unit, selector and at-rule checks
- [x] HTML and CSS formatters
- [x] TUI: file browser with results, peek with problems marked, formatting
- [x] CSS: grammars for grid templates, generated content, masks, SVG and more (469 of 497 properties)
- [x] HTML: content-model rules, attribute checks, obsolete elements
- [x] HTML: check `<style>` elements and `style` attributes with the CSS validator
- [ ] CSS: grammars for the last new features (anchor positioning areas, `offset-*`, `mask-border-*`)
- [ ] HTML: more content models (forms, headings, sectioning), and the attributes of `<input>` per type
- [x] XML: well-formedness, and sitemaps
- [x] XML: formatting, and RSS/Atom feeds
- [x] Markdown checks, and the llms.txt format
- [ ] Comparison test suite against vnu output
- [ ] Release builds for Windows, Linux and macOS
