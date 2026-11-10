# Rules for working on Blyant Validator

For anyone changing the code, people and coding agents alike.

Blyant Validator is a command-line companion to the Blyant editor. Many of these rules follow Blyant's own
`AGENTS.md`, so the two projects work the same way.

## Privacy
- **No personal email addresses in the project.** Not in source files, project files, Markdown files, tests,
  sample files or commit messages. `ProjectRulesTests` fails on any email address in the files of the
  repository, except GitHub noreply addresses (`...@users.noreply.github.com`)
- Commits are made with the owner's GitHub noreply address. It is set in this repository's own Git
  configuration (`git config user.email`), so a different global address is not used by mistake. Check it
  with `git log --format="%ae"` before pushing
- The author is credited by name only (`Authors` in `Directory.Build.props`)

## Credits
Blyant Validator is modelled on the Nu Html Checker (<https://github.com/validator/validator>) and the
W3C CSS Validation Service (<https://github.com/w3c/css-validator>), and written from scratch in C#.

- Both projects are named, with links to their repositories, in `README.md`, `CREDITS.md` and the
  `credits` command (`Credits.cs`). Keep the three in step
- No source code is copied from them. If code or data from another project is ever added, its licence
  text goes into `THIRD-PARTY-NOTICES.md` in the same change

## Dependencies
- No dependencies besides .NET itself, as in Blyant. The HTML reader (`Html/Parsing`), the CSS parser
  (`Css/Syntax`) and the command-line parser (`Cli/CommandLine.cs`) are written for this project.
  `ProjectRulesTests` fails on a `PackageReference` outside `tests`
- The test project may use test libraries (xUnit); they are not part of the program
- Validation data (lists of properties, value grammars) is kept as text files in the repository,
  embedded in the program: the program never downloads anything while it runs

## Command line
- The commands, their options and the exit codes are described in `README.md`. When one is added,
  renamed or changed, update `README.md` and `CHANGELOG.md` in the same change
- Exit codes: 0 no errors, 1 errors found (or files not formatted with `format --check`), 2 the tool
  itself failed. Blyant relies on them: do not change their meaning
- Results go to standard output, problems of the tool itself (a missing file, a bad option) to
  standard error
- Options are declared in `Program.cs` (a `CommandSpec` per command); the help text is made from them
- The JSON output (`--output json`) is read by Blyant: fields may be added, never renamed or removed
- Diagnostic codes (`css.unknown-property`, `html.img-missing-alt` ...) are stable identifiers: do not
  rename them. Messages may be improved
- Text shown to the user is English

## The TUI
The TUI (`Cli/Tui`) is adapted from the file browser of Blyant (MIT License, same author). Its terminal parts
(`Tui/Terminal`: keys, colors, boxes, the command line, the help) and the highlighters (`Tui/Highlighting`) are
copies of Blyant's code, trimmed; each file says what it was adapted from. They are kept apart from Blyant on
purpose: a fix in one is carried to the other by hand, when it matters.

- It looks and works like Blyant's file browser: the same keys where they do the same thing (`j`/`k`, `gg`/`G`,
  `Tab`, `T`, `V`, `/`, `:`, `?`), the same colors (Blyant's Default scheme) and the same layout
- The screen is 79 columns and 23 rows at least (a window of 80x24): the last column and row of the window are not
  used. Key hints that do not fit are left out (`Menu.WriteKeyHints`), and nothing is placed at a fixed column
  after a text of variable length
- Text shown is English, written where it is used. There are no language files
- When a key or command is added or changed, update `Browser.CommandTable`, `RunCommand`, `HelpRows`, the key
  hints, `README.md` and `CHANGELOG.md` together. `TuiTests.Commands_AreHandledAndInTheHelp` checks the commands
- The engines run on a background thread (`ResultStore`); everything that draws runs on the main thread. The
  TUI never starts other programs, except Java for `:engine vnu`
- Files are only written by formatting, after asking, and the settings file (`Settings.cs`), when the TUI closes.
  Both with `DocumentIo.SafeWriteAllText`
- The settings file is `settings.ini` in the data directory, in Blyant's format (`[SETTINGS]` and `name=value` read
  by name). A new setting is read and written by name in `Settings.cs`, and tested in `SettingsTests`
- The icon (`BlyantValidator.ico` and `.svg`) is drawn by `tools/icon.py`: change the script, not the files
- The screenshots in `docs/screenshots` are made by `tools/screenshots.py` (Windows, with the Python packages
  `pyte` and `pywinpty`), from the release build and a made-up site in `C:\Sites`. When a screen changes, run it
  again in the same change. Like Blyant's, it never changes a `C:\Sites` that it did not make
- Text from files (names, lines) is written with `TextMetrics.Printable` or `Tools.TruncateString`, so a file can
  never send escape codes to the terminal
- `PeekDocument`, `ResultStore` and the command line classes do not touch the console, and are tested. The drawing
  is tried in a terminal: `dotnet run --project src/Blyant.Validator.Cli -- browse <folder>`

## Validators and formatters
- The engines in `Blyant.Validator.Html`, `Blyant.Validator.Css` and `Blyant.Validator.Xml` do not touch the
  console or the file system, so they can be tested.
- The program never goes online: formats with a spec (llms.txt, sitemaps) have their rules built in, and URLs are
  only checked for their form. The Markdown validator looks up relative links through a function it is given (the
  program passes `File.Exists`/`Directory.Exists`); without one they are not checked
- XML is read with .NET's `XmlReader` with no resolver (`XmlResolver = null`): a file must never make the validator
  read other files or the network. Only the internal DTD subset of the file is read (`DtdProcessing.Parse`, for the
  entities it declares), and entities may expand to at most `XmlValidator.MaxEntityCharacters` characters.
  `XmlValidatorTests.ExternalEntities_AreNotRead` and `FalsePositiveTests.EntitiesThatExpandWithoutEnd_AreStopped`
  check it. Reading, writing and printing belong in `Blyant.Validator.Cli`
- A formatter changes only layout (whitespace, line breaks, indentation, a missing last `;`), never
  what the document means. Tests check that formatting twice gives the same result as formatting once,
  and that the tokens of a CSS file are the same before and after
- Whitespace that matters is kept as it is: the contents of `<pre>`, `<textarea>` and `<script>`,
  and custom properties (`--name`) in CSS
- A file with errors that make its structure unclear (a block that is never closed) is not formatted:
  the formatter says why and leaves the file as it is
- What HTML allows (elements, attributes, values, obsolete features) is kept in `Html/Rules/HtmlSpec.cs`, and
  read by the rules. A new rule gets tests of real-world markup that must pass, and of mistakes it must find
  (`HtmlRuleTests.RealWorldPage_HasNoProblems` is the page that must stay without problems)
- Fragment mode (`--fragment`, `:fragment`) checks HTML as the content of a `<body>`: the rules about the page
  around it (doctype, title, language) are left out of `HtmlRuleSet.Fragment`. A new rule about the page as a whole
  is left out there too
- Ignored codes (`--ignore`, `:ignore`) are matched by `IgnoreList`, and rely on the codes being stable
- Regular expressions use `[0-9]`, not `\d`: in .NET `\d` matches the digits of every script, which `int.Parse`
  then refuses
- The checks and formatters walk trees recursively: input nested deeper than `HtmlReader.MaxDepth`,
  `CssParser.MaxDepth` or `XmlFormatter.MaxDepth` is reported and not walked, so the stack never overflows
- A false error is worse than a missed one. When a value cannot be checked (it uses `var()`, or the
  property has no grammar yet), it is accepted

## Files
The files of the user must never be left empty or half written, whenever the program is stopped.

- `format --write` replaces a file with `SafeFile.WriteAllText`: it writes a temporary file next to it,
  flushes it to the disk, and then puts it in the place of the file in one step. Never `File.WriteAllText`,
  `File.WriteAllBytes`, `FileMode.Create` or `FileMode.Truncate`, which empty the file first.
  `ProjectRulesTests` fails on them
- A file is written back with the encoding it had: a UTF-8 file with a BOM keeps its BOM, and the line
  endings of the file (CRLF or LF) are kept
- A file that the formatting does not change is not written
- A file that is not UTF-8 is not read (`DocumentIo` throws `NotUtf8Exception`): decoded as UTF-8 its other
  characters would be replaced, and formatting would write that back

## Linux and macOS
Blyant Validator is written and tested on Windows, but it must work the same on Linux and macOS. The
GitHub workflow `Build and test` builds and tests it on all three systems.

- Do not take the behaviour of the file system from Windows: names of files are case-sensitive on Linux,
  the separator is `/` (use `Path.Combine`, never a written `\`), and a name starting with `.` is hidden
- Passing tests on Windows say nothing about the rest: look at the workflow

## Code
- Source files (`.cs`, `.csproj`, `.props`, `.slnx`) are UTF-8 with BOM and CRLF line endings. Markdown
  files have LF. `ProjectRulesTests` checks this
- Shared settings (version, author, language options) are in `Directory.Build.props`

## Checks before a change is done
- `dotnet test BlyantValidator.slnx` passes
- The change is tried in the running program (`dotnet run --project src/Blyant.Validator.Cli -- ...`),
  not only in tests
- `README.md` and `CHANGELOG.md` describe the change

## Releases
- The version is set in `Directory.Build.props` (`Version`)
- A release is made in this order: push `main`, wait until `Build and test` has passed on Windows, Linux
  and macOS, and only then tag it and publish the release
