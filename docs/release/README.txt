BLYANT VALIDATOR
================

Version {version}

Validates and formats HTML and CSS files, and validates XML (sitemaps, RSS and
Atom feeds, SVG), Markdown (llms.txt) and Typst, in the terminal. A companion
to the Blyant editor (https://blyant-editor.com/), and a program of its own
too.

Run it side by side with your favorite editor, for real-time validation of all
your files: the file browser watches the folders it shows and checks a file
again the moment it is saved, so its errors and warnings are always up to date
while you work, in any editor.

The program is one file with the .NET runtime in it: nothing else has to be
installed. The full documentation, with screenshots, is on GitHub:

    {repository}


STARTING IT
-----------

    blyant-validator                 The file browser (TUI), where it was left
    blyant-validator browse <folder> The file browser, in a folder
    blyant-validator validate <paths> [options]
    blyant-validator format <paths> [options]
    blyant-validator credits
    blyant-validator <command> --help

On Linux and macOS, start it with ./blyant-validator in its folder. If it was
not unpacked with tar, make it executable once with:

    chmod +x blyant-validator

On macOS the program is not signed: if macOS refuses to open it, allow it once
with:

    xattr -d com.apple.quarantine blyant-validator

The portable version needs the .NET 10 runtime, and is started with:

    dotnet blyant-validator.dll


THE FILE BROWSER
----------------

Each file shows its result: ok, the errors and warnings found (3E 1W), or ...
while it is checked. The window must be at least 80 columns and 24 rows.

    j/k, arrows   Select a file or folder
    Enter, l      Open a folder; on a file: the peek, with the focus in it
    h, Backspace  The folder above
    V             The peek: the file in the other pane, its problems marked
    P             The list of the problems of the file
    Tab           The other pane, or into the peek (n/N: next problem there)
    F             Format the file (or the marked files), after asking
    /             Only list the names containing a text
    :             A command: :cd, :fragment, :ignore, :number, :path ...
                  :typst makes a Typst document into PDF, PNG, SVG or HTML
    ?             All the keys and commands
    q, Esc        Quit, after asking

The settings are kept in the folder "data" next to the program, or, when that
cannot be written to, in "Blyant Validator/data" in your application data.


THE COMMAND LINE
----------------

    validate -r, --recursive     Folders and their subfolders
             -o, --output json   Results for editors and scripts
             --fragment          HTML for a page made elsewhere (a CMS)
             -i, --ignore <codes>  Leave out problems by their code (css.*)
             -e, --engine vnu    The Nu Html Checker (needs Java and vnu.jar)
             -e, --engine typst  The Typst compiler for Typst files (needs
                                 the Typst CLI; Typst can download packages)

    format   -w, --write         Write the files (else: print the result)
             --check             Only tell whether files are formatted

Formatting only changes layout: the formatted file means the same as before.
A file that is not UTF-8 is never read or written.

Exit codes: 0 no errors, 1 errors found (or files not formatted with --check),
2 the program itself failed (a bad option, a file that cannot be read ...).


CREDITS
-------

Blyant Validator is modelled on two open-source projects, and written from
scratch: no code from them is included.

  - The Nu Html Checker (v.Nu), MIT License
    https://github.com/validator/validator
  - The W3C CSS Validation Service, W3C Software and Document License
    https://github.com/w3c/css-validator

Its file browser is adapted from the Blyant editor, by the same author.


LICENCE AND NOTICES
-------------------

Blyant Validator is released under the MIT License: see the file LICENSE next
to this one. Copyright (c) 2026 Lars Brandt Stisen.

It contains code adapted from Blyant (https://blyant-editor.com/), under the
same MIT License: Copyright (c) 2022-2026 Lars Brandt Stisen.

The program file includes the .NET runtime, under the MIT License:

    Copyright (c) .NET Foundation and Contributors

    Permission is hereby granted, free of charge, to any person obtaining a
    copy of this software and associated documentation files (the
    "Software"), to deal in the Software without restriction, including
    without limitation the rights to use, copy, modify, merge, publish,
    distribute, sublicense, and/or sell copies of the Software, and to permit
    persons to whom the Software is furnished to do so, subject to the
    following conditions:

    The above copyright notice and this permission notice shall be included
    in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    USE OR OTHER DEALINGS IN THE SOFTWARE.

The third-party components of the .NET runtime, and their licences, are
listed at:

    https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
