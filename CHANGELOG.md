# Changelog

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
