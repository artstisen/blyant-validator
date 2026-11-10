# Blyant Validator

A cross-platform command-line tool that validates and formats HTML and CSS files. It's written in
C#/.NET 10 and designed as an optional companion to the [Blyant editor](https://blyant-editor.com/),
but it works on its own too.

> **Status: early development (0.4.0).** CSS validation and both formatters work. HTML validation
> reports parse errors and has a small starter rule set; more rules will follow.
>
> Blyant Validator has no dependencies besides .NET.

## Credits

Blyant Validator is modelled on two excellent open-source projects:

- **The Nu Html Checker (v.Nu)**: <https://github.com/validator/validator>
- **The W3C CSS Validation Service**: <https://github.com/w3c/css-validator>

See [CREDITS.md](CREDITS.md) for details, and run `blyant-validator credits`.

## Usage

```
blyant-validator                          (the TUI, in the current directory)
blyant-validator browse   [folder] [options]
blyant-validator validate <paths>... [options]
blyant-validator format   <paths>... [options]
blyant-validator credits
```

### The TUI

Started without a command (or with `browse`), Blyant Validator is a file browser in the terminal, in the style of
the file browser of the Blyant editor: one or two panes, and a peek of the selected file in the other pane.

```
 FILES                                   Blyant Validator
 ~/sites/blog                          | about.html - 3 errors, 1 warning
-- PANE 1 -----------------------------+-- PEEK --------------------------
-> ..                                  |     10   <main>
   css/                                | E   11     <div><span>Welcome
   about.html          3E 1W    2.1 KB |     12     </div>
   index.html          ok       1.4 KB |     13   </main>
```

- Each HTML and CSS file shows its result: `ok`, the number of errors and warnings (`3E 1W`), `...` while it is
  checked, or `failed`. Files are checked in the background, and checked again when they change on disk, so the
  list stays up to date while you edit the files in another program.
- `V` peeks at the selected file: its text in colors, with a mark (`E`, `W`, `i`) in front of the lines with
  problems and the place of each error highlighted. `P` shows the list of its problems instead.
- `Tab` goes into the peek: `j`/`k` move, `n`/`N` go to the next and previous problem (its message is shown on
  the bottom line), `/` searches, `h`/`l` scroll sideways, and in the list of problems `Enter` shows a problem in
  the file. `Tab` or `Esc` goes back to the list.
- As in Vim, `n`/`N` follow what you looked for last: after a `/` search they go to the matches, and `]`/`[` (which
  always go to problems) make them go to the problems again.
- `F` formats the selected file, or the files marked with `Space`, after asking.
- `T` shows one pane or two, `/` filters the list, `:` opens the command line (`:cd`, `:engine vnu`, `:all` to
  list all files …, with `Tab` completion), and `?` shows all keys. `q` quits.

The TUI needs a window of at least 80×24, like Blyant.

It remembers where it was: the folder and the selected file of both panes, the pane with the focus, and whether one
or two panes were shown. `blyant-validator` (or `browse` without a folder) starts there again; `browse <folder>`
shows that folder in the pane with the focus. The first time, it starts in the current directory. This is kept in
`settings.ini`, in Blyant's format, in the folder `data` next to the program (or, if that folder cannot be written
to, in `Blyant Validator/data` in your application data folder).

Paths can be files or directories. Directories are searched for `.html`, `.htm`, `.xhtml` and `.css` files;
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
  that use `var()`, `env()` or `attr()` are not checked, and neither are properties without a grammar yet
  (for example `grid-template-columns` and `content`): a false error is worse than a missed one.
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
- Missing `<title>` and `lang`, `<img>` without `alt`, duplicate IDs.

## Formatting

Formatting only changes layout; the formatted file means exactly the same as before.

- **CSS**: one selector and one declaration per line, blocks indented, `: ` after property names,
  whitespace inside values made single spaces, and a missing last `;` added. Comments are kept, and so is
  one empty line where the file had empty lines. Custom properties (`--name`) keep their value exactly as
  written. A file with a block that is never closed is not formatted.
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
- [ ] CSS: grammars for the remaining properties (grid templates, masks, …)
- [ ] HTML: content-model rules, attribute checks, obsolete elements
- [ ] HTML: check `<style>` elements and `style` attributes with the CSS validator
- [ ] Comparison test suite against vnu output
- [ ] Release builds for Windows, Linux and macOS
