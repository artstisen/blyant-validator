# Changelog

## Unreleased

### Changed
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
