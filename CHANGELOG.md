# Changelog

## Unreleased

### Added
- Typst (`.typ`): what the Typst compiler refuses and that can be told without compiling: raw text, strings,
  equations and brackets not closed, `*strong*` and `_emphasis_` not closed before the end of their paragraph,
  heading, list item or content (`Five * three`), a `]` or a bracket of code that closes nothing, and `#` with no
  code after it. References to labels that are not in the document or are there twice (the document: the file, the
  files it includes, and the files that include it), files given to `#include`, `#import`, `image()` … that are not
  there, and package names of the wrong form. Warnings: `=Heading` without its space, a label twice in a file. The
  errors were compared with those of the Typst compiler 0.15, on real documents and 300 damaged copies of them:
  none that the compiler does not find
- `--engine typst` (and `:engine typst` in the TUI): Typst files are checked by the Typst CLI (`typst compile`, the PDF
  thrown away), for all that the compiler finds, also what code means. A chapter is compiled as part of its document
  (its main file is found as for the references), and its own errors and warnings are kept. `--typst <path>` (or
  `BLYANT_TYPST`) tells where `typst` is. Engines can be given together: `--engine vnu,typst`. With this engine the
  program can go online: Typst downloads the packages a document imports
- Typst in the TUI: listed with its result, and shown in the peek in Blyant's Typst colors (`TypstHighlighter.cs`,
  copied from Blyant)

## 0.5.0 (2026-11-10)

### Fixed
- A crash (an unhandled exception) on digits of other scripts in sitemap and feed dates and in `<input>` values
  (`٢٠٢٦-11-10`): they are now errors of the file. Regular expressions match ASCII digits only
- A stack overflow on files nested thousands deep: HTML deeper than 512 elements (`html.too-deep`) and CSS deeper
  than 256 blocks or parentheses (`css.parse.too-deep`) are reported and not formatted
- Files that are not UTF-8 were read with their other characters replaced, and formatting wrote that back: they are
  now not read, and reported as `file.not-utf8` (with the line). UTF-16 and UTF-32 files too
- An error the program did not expect ended it with a stack trace and an undefined exit code: now exit code 2, and
  a file that cannot be read or written is told about while the other files are still done. In the TUI, such an
  error is shown for that file, and the other files are still checked
- A path that does not exist, given with others, now gives exit code 2, as README says
- Folders are searched without regard to case on every system (`INDEX.HTML` on Linux), folders that cannot be read
  are passed over, and a file given twice (by itself and in its folder) is done once
- TUI: a problem message quoting the file could send control characters to the terminal (the bottom line of the
  peek)
- CSS formatting: comments inside a declaration were dropped, escaped names were written unescaped
  (`--my\:var` became `--my:var`, another property), `!IMPORTANT` was lowercased, and a string with a line break
  in it took in what followed. Now only whitespace changes, and such a string stops formatting
- HTML formatting: a `<script>`, `<noscript>`, `<link>` or `<meta>` right next to text got a line of its own, adding
  a visible space; HTML in an SVG `<foreignObject>` was split up word by word; a no-break space in an unquoted value
  split it in two; a file ending inside a tag got a `>` added (it is now not formatted)
- HTML in an SVG `<foreignObject>` (and `desc`, `title`) is checked as HTML, and `xmlns` with the XHTML namespace is
  allowed on HTML elements
- XML formatting: SVG text (`<text>`, `<tspan>`), XHTML and elements with only whitespace in them are kept as written
- Line endings: a file with CR line endings (old Mac) was written with LF; the line ending a file mostly uses is kept
- A 4-digit year below 100 in an RSS date was read as a 2-digit year
- False errors on valid HTML:
  - pages that leave out the optional `<head>`, `</head>` and `<body>` tags (as minifiers write them);
  - `&quot;` and other character references in a `style` attribute, which are now decoded before the CSS is checked;
  - `<col>` right in a `<table>`, and `</colgroup>` or `</caption>` left out;
  - an `<a>` in SVG inside an `<a>`, a comment ending in `<!-->`, and a `<p>` closed by `<li>`, `<dt>`, `<dd>` or
    `<summary>`;
  - IDs inside a `<template>` counted with those of the page, and CSS in a CDATA section of an SVG `<style>`;
  - `table background` reported twice;
  - newer HTML: `dirname` on more input types, CSS colors in `<input type=color alpha>`, and the customizable
    `<select>` (a `<button>`, and `<div>` around options)
- False errors on valid XML: entities declared in the file itself (every Adobe Illustrator SVG); an entity of an
  external DTD (as `&nbsp;` in XHTML) is now a warning, `xml.undeclared-entity`. `.xhtml` files are XML, and no
  longer checked by the rules of HTML's syntax. Atom dates with a leap second (`23:59:60`)
- False warnings on valid Markdown: HTML blocks over several lines (a comment, `<details>` …) were read as Markdown,
  fences in list items or block quotes were not seen, `arr[0][1]` was a reference link, and a setext heading over
  several lines had the anchor of its last line only. llms.txt: links with brackets in their name or parentheses in
  their URL, and lists in lists
- TUI:
  - after `:engine` or `:ignore`, a file being checked at that moment kept its old result;
  - the screen was cleared and drawn again on every key while peeking (flicker);
  - the peek read all of a file, also one of gigabytes; it now reads the part it shows, shows the start of a very long
    line (a minified file), and colors lines of up to 5,000 characters (longer ones took very long);
  - a file of exactly 20,000 lines was told to be cut, and problems after the cut were marked on its last line;
  - `F` wrote a file that was saved in another program while it asked (now left as it is), named the wrong file when
    one failed, and left out marked files hidden by a filter;
  - `:ignore` typed in the peek could ignore another problem when the file was checked again meanwhile; in the text
    of the peek it now ignores the first problem on the line of the cursor;
  - the rules of a folder (`:fragment`, `:ignore`) were copied into a subfolder changed after it, so a later change
    of the folder no longer reached the subfolder: a subfolder now keeps only what it changes (`checkunignore` in
    `settings.ini`);
  - Ctrl+C in the peek only went back to the list; it now quits at once, as everywhere;
  - bidi control characters in names and lines ("Trojan Source") could turn the text around; they are shown as
    spaces;
  - a place longer than 8 characters in the list of problems, a filter with wide characters and a long unknown
    command could write past the edge;
  - a file that is gone was asked for again and again; the files behind the peek were checked though not shown;
    `settings.ini` that could not be written was not told about
- vnu: Java went on running when the TUI closed during a check, and a check had no time limit (now 2 minutes); its
  output was not read as UTF-8 (quotes garbled on Windows); output of another shape stopped the program
- Command line:
  - `--recursive` went into `.git`, `node_modules` and links to folders;
  - text output was written in the console's code page (on Windows "?" for other characters, also in what `format`
    prints): it is now UTF-8;
  - `format --write` replaced a link by a file and reset the permissions of a file (Linux, macOS) or its attributes
    (Windows); a long file name could make the temporary name too long;
  - `-` (standard input, not supported), an option given twice, and `format --check --write` are now errors
- HTML: `type=url` values such as `/path` were accepted on Linux and macOS only (.NET reads them as file paths there),
  and a doctype at the very end of a file lost its last letter
- False errors on valid CSS: relative selectors and declarations in `@scope`, WebKit's scrollbar pseudo-classes
  (`::-webkit-scrollbar-thumb:horizontal`), `font: -apple-system-body`, the old names `color-adjust` and
  `inset-area`, `@function` and calls of custom functions (`--double(4px)`)

### Changed
- Linux and macOS: a link to a file that is gone is not a file on any system (File.Exists said it was there), a
  Markdown link written with a backslash is the same path on every system, and two names that differ only in case
  are one file or two as the folder has them (also on a case-sensitive volume of macOS). Tests that can only run on
  some systems are shown as skipped elsewhere
- Code that the validator does not use is removed: the C#, C++, Rust and PHP highlighting and the JSON highlighting
  copied from Blyant (only JavaScript, in HTML's script elements, is used), and members nothing called (in the
  boxes, the command line, the clipboard, the CSS tokens and parser). The colors of the peek are the same
- The target framework and the language options are set once, in `Directory.Build.props`

### Added
- Release builds: `publish.ps1` builds a self-contained single file for Windows (x64, ARM64), Linux (x64, ARM64,
  ARM) and macOS (Intel, Apple silicon), and a portable version, packed as zip and tar.gz for the releases page, as
  Blyant's. README has a section "Installation"
- The release archives hold the program, `LICENSE` and a short `README.txt` of their own (`docs/release/README.txt`):
  the commands, the keys, the credits and the notices of the licences, also of the .NET runtime that the program file
  includes, without the screenshots and links of the repository's files
- A licence: MIT (`LICENSE`), as Blyant's
- README: a section "Standards", telling what HTML, CSS, XML, sitemaps, feeds, Markdown and llms.txt are checked
  against, and when the rules were last brought up to date
- XML: an entity declared to be in another file is a warning, `xml.external-entity` (it is never read)
- TUI: `:number` and `:nonumber` number the rows of the panes, kept in `settings.ini` (`filenumbers`), and a count in
  front of a key moves that many rows or goes to that row (`5j`, `5k`, `12gg`, `12G`), as in Blyant's file browser
- Fragment mode, for HTML written to be inserted in a page made elsewhere (a CMS article): `validate --fragment`,
  and `:fragment` in the TUI. The doctype, title and language of the page are not checked; a doctype, `<html>`,
  `<head>`, `<body>`, `<title>`, `<meta>` or `<base>` in a fragment is a warning (`html.fragment-page-part`)
- Ignored problems: `validate --ignore <codes>` (`-i`, comma-separated or repeated; `css.*` ignores all CSS codes),
  and `:ignore` and `:unignore` in the TUI (`:ignore` alone ignores the problem selected in the peek)
- In the TUI, fragments and ignored codes are rules of a folder and its subfolders, kept in `settings.ini`. A folder
  that is not fully checked has the badges `FRAGMENT` and `IGNORE n`, and the peek says it with the result of a file
- A summary that says when a check was not a full one, and the JSON fields `fragment`, `ignore` and `ignored`
- Commands can be typed in the peek (`:`); they run in the list
- CSS: grammars for the last 27 properties: anchor positioning (`position-area`, `position-try`,
  `position-try-fallbacks`), motion paths (`offset-*`), `mask-border-*`, `animation-range`, `corner-shape` and more.
  All properties but the non-standard `font-smooth` have their values checked
- HTML: sectioning (`header`/`footer` nesting, `address`, `main` placement), elements not allowed inside
  themselves, labels with more than one control or a mismatched `for`, `hgroup`, and empty headings (warning)
- HTML: the attributes of `<input>` per type, and the form of `min`, `max`, `step` and `value` per type
- XML formatting (`format`, and `F` in the TUI): elements that hold only elements are indented; text, mixed content
  and `xml:space="preserve"` are kept as written. Not-well-formed files are not formatted
- RSS 2.0 and Atom feeds are checked: required elements, RFC 822 and RFC 3339 dates, URLs, guids, enclosures, ids,
  authors. `.rss` and `.atom` files are XML too
- Markdown (`.md`, `.markdown`): warnings for code blocks never closed, `#Heading` without a space, empty and
  skipped headings, more than one H1, unclosed and empty links, undefined and duplicate references, anchors with no
  heading, and relative links and images to files that do not exist
- `llms.txt` is checked against the llms.txt format (built in, nothing fetched): one `# Name` first, sections under
  `##` that are lists of links; `llms-full.txt` is checked as Markdown
- Markdown in the TUI: listed with its result, and shown in the peek in Blyant's Markdown colors; an llms.txt in the
  screenshots
- XML: `.xml` and `.svg` files are checked for well-formedness, with the common mistakes (a bare `&`, `&nbsp;`) told
  in plain words. DTDs are ignored and nothing outside the file is loaded
- Sitemaps and sitemap indexes are checked by the rules of sitemaps.org: namespace, UTF-8, `loc`, `lastmod`,
  `changefreq`, `priority`, unknown and repeated elements, size limits; duplicate URLs and other hosts are warnings
- XML in the TUI: listed with its result, and shown in the peek in colors; a sitemap in the screenshots
- HTML: the CSS of a page is validated: `<style>` elements as style sheets and `style` attributes as declarations,
  with the places of the problems in the HTML file
- HTML: obsolete elements and attributes (`<center>`, `<font>`, `align`, `bgcolor`, `<a name>` …), with what to use
  instead; warnings for `border="0"`/`border="1"` and an unneeded `type` on `<script>` and `<style>`
- HTML: unknown elements and attributes, bad values of boolean, enumerated, numeric, ID and URL attributes,
  missing required attributes, and `<label for>` pointing at nothing
- HTML: content models: what lists, tables, `<select>`, `<head>` and `<picture>` may hold, where list items and
  table parts may be, blocks inside inline elements, interactive elements inside links and buttons, text where
  only elements go, and more than one visible `<main>`
- CSS: grammars for 111 more properties (469 of 497): grid templates, `content`, `quotes`, `font-variant`,
  `border-image`, masks, `container`, view transitions, SVG paint and strokes, scroll-driven timelines and more
- CSS: suggestions for misspelled properties count two swapped letters as one mistake ("widht" → "width")
- The TUI remembers where it was (the folders and selected files of both panes, the pane with the focus, one or two
  panes) in `settings.ini` in the data directory, as Blyant does, and starts there again. `browse <folder>` shows that
  folder in the pane with the focus
- An icon (Windows): a checkmark in the style of Blyant's icon, drawn by `tools/icon.py`
- `:path` and `:name` in the TUI: the full path, or the name, of the selected file or folder to the clipboard
  (OSC 52), as in Blyant's file browser
- `I` in the peek: the full text of the problems of the line (or the selected problem) in a box, with its place,
  severity and code. A message cut on the bottom line ends with "(I: all)"
- Screenshots of the TUI in the README, made by `tools/screenshots.py` (as Blyant's) from a made-up site
- As in Blyant, a folder `data` with a `settings.ini` in the directory the program is started in is used for the
  settings instead of the one next to the program

### Fixed
- The title of the peek no longer says "0 warnings" or "0 errors"
- With `:all`, a file that is not HTML or CSS (Markdown, SVG …) no longer shows "Checking..." for ever in the
  problems and the title of the peek: it says that it is not checked

### Changed
- `Esc`, `q` and `:quit` ask before the TUI is closed ("Exit Blyant Validator?"), as Blyant does. Ctrl+C still
  closes it at once
- In the peek, `n`/`N` go to the next and previous problem, as `]`/`[` do. After a `/` search they go to the
  matches instead (as in Vim and Blyant), and `]`/`[` make them go to the problems again

## 0.4.0

### Added
- The TUI: `blyant-validator` without a command (or `blyant-validator browse [folder]`) is a file browser in the
  terminal, adapted from the file browser of Blyant. One or two panes list folders and HTML/CSS files with their
  results (`ok`, `3E 1W`), checked in the background and again when a file changes
- The peek (`V`): the selected file in colors, with the lines that have problems marked and the place of each error
  highlighted; `Tab` goes into it (`j`/`k`, `]`/`[` next/previous problem, `/` search). `P` lists the problems
- Formatting in the TUI (`F`), of the selected or the marked files, after asking
- Commands on the `:` command line with Tab completion: `:cd`, `:home`, `:engine native|vnu`, `:all`, `:refresh`,
  `:panes`, `:peek`, `:problems`, `:format`, `:help`, `:quit`

## 0.3.0

### Changed
- No dependencies besides .NET: AngleSharp and System.CommandLine are removed
- HTML parse errors come from Blyant Validator's own HTML reader (the one the formatter uses). It
  reports doctype problems, unclosed elements, stray end tags, attribute mistakes, broken comments,
  bad character references, nested `<a>`/`<form>`/`<button>` and text inside tables. Error codes are now
  named (`html.parse.stray-end-tag`) instead of numbered
- The command line is read by Blyant Validator's own parser. Options are the same; `--version` is new,
  `--indent-size` must be 1–16 and `--line-width` at least 20

## 0.2.0

### Added
- CSS validation: syntax errors, unknown properties (with suggestions), invalid values checked against
  property grammars, unknown units, selectors (pseudo-classes, pseudo-elements, combinators, IDs),
  at-rules (unknown at-rules, `@charset` and `@import` placement, `@font-face` descriptors, keyframe
  selectors) and CSS Nesting. Vendor extensions are reported as info
- `format` for CSS: one selector and declaration per line, indented blocks, comments and empty lines kept.
  Files with unclosed blocks are not formatted
- `format` for HTML: block-level elements on their own lines and indented, inline content kept together
  with its line breaks, `<pre>`, `<textarea>` and `<script>` kept as written, `<style>` formatted as CSS
- `format --line-width`
- `AGENTS.md` with the rules for working on the code, and a GitHub workflow that builds and tests on
  Windows, Linux and macOS

### Changed
- `format --write` replaces files safely (a temporary file is written and moved into place) and keeps
  their BOM and line endings. Files that do not change are not written
- `format` exits with code 2 when a file could not be formatted

## 0.1.0

### Added
- First version: `validate` (HTML parse errors and a few rules, `--engine vnu`, text and JSON output),
  `format` (placeholder) and `credits`
