"""Screenshots of the TUI of Blyant Validator for the README, as SVG images of a terminal window. Adapted from
Blyant's tools/screenshots.py.

    python tools/screenshots.py [--exe PATH] [scene ...]

The program is run in a hidden terminal (a pseudo console) of 100x30, with a demo site in C:\\Sites and a data
directory of its own in the temp folder, so nothing of the user's is shown or changed. Each scene gets the keys that
set it up, and what the terminal then shows (each cell with its colors, and the cursor) is drawn as an SVG in
docs/screenshots. Run it again after a change of the screens.

Needs Windows, and the Python packages pyte and pywinpty. The pages of the demo site are made up for it, with
mistakes on purpose. The program is the release build (dotnet build -c Release) unless --exe gives another.
"""
import argparse
import ctypes
import html
import os
import queue
import re
import shutil
import sys
import tempfile
import threading
import time

import pyte
import winpty

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'docs', 'screenshots')
SITES = r'C:\Sites'
SITE = os.path.join(SITES, 'harbour-lights')
MARKER = os.path.join(SITES, '.blyant-validator-demo')
COLS, ROWS = 100, 30

# ---------------------------------------------------------------------------------------------------------------
# The demo site (made up for the screenshots; about.html and site.css have mistakes on purpose)

INDEX = """<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>Harbour Lights</title>
    <link rel="stylesheet" href="css/site.css">
  </head>
  <body>
    <header>
      <h1>Harbour Lights</h1>
      <nav>
        <a href="index.html">Home</a>
        <a href="about.html">About</a>
        <a href="contact.html">Contact</a>
      </nav>
    </header>
    <main>
      <p>A novel about a lighthouse, a logbook and a light that should not be lit.</p>
      <img src="images/cover.jpg" alt="The lighthouse at dusk">
    </main>
  </body>
</html>
"""

ABOUT = """<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>About the book</title>
    <link rel="stylesheet" href="css/site.css">
  </head>
  <body>
    <header id="top">
      <h1>About the book</h1>
    </header>
    <main id="top">
      <p>Maren Holt comes back to the island to keep her grandfather's light.
      <img src="images/maren.jpg">
      <p>The logbook stops in March, in the middle of a sentence.</span>
      <section>
        <h2>The harbour lights</h2>
        <ul>
          <li>Harbour mouth: <b>steady green</li>
          <li>North pier: red, every four seconds</li>
          <li>The Teeth: two flashes, then dark &copy 2026</li>
        </ul>
      </section>
    </main>
  </body>
</html>
"""

CONTACT = """<!DOCTYPE html>
<html>
  <head>
    <meta charset="utf-8">
    <title>Contact</title>
    <link rel="stylesheet" href="css/site.css">
  </head>
  <body>
    <h1>Contact</h1>
    <p>Write to the harbour office.</p>
  </body>
</html>
"""

# Correct, but written without care for the layout: the one formatted in the screenshot
NEWS = """<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>News</title>
<link rel="stylesheet" href="css/site.css"></head><body><h1>News</h1>
<ul><li>The second draft is done.</li><li>The cover is ready: <a href="index.html">see it</a>.</li></ul>
<p>Readings in <b>October</b>,<br>at the harbour office.</p></body></html>
"""

SITE_CSS = """/* Harbour Lights */
:root {
  --sea: #1d3557;
  --light: #f1faee;
}

body {
  margin: 0;
  font: 400 1rem/1.6 Georgia, serif;
  color: var(--sea);
  background: var(--light);
}

header {
  display: flex;
  justify-content: space-between;
  padding: 1rem 2rem;
}

nav a:hover {
  colr: #e63946;
  text-decoration: underline;
}

main {
  max-width: 60ch;
  margin: 0 auto;
  padding: 2rem 1rm;
}

img {
  max-width: 100%;
  border-radius: -4px;
}
"""

PRINT_CSS = """@media print {
  nav {
    display: none;
  }

  body {
    font-size: 11pt;
  }
}
"""

SITEMAP = """<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://harbour-lights.example/</loc>
    <lastmod>2026-11-10</lastmod>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>https://harbour-lights.example/about.html</loc>
    <lastmod>10/11/2026</lastmod>
  </url>
  <url>
    <loc>https://harbour-lights.example/news.html</loc>
    <changefreq>daily</changefreq>
  </url>
</urlset>
"""

LLMS = """# Harbour Lights

> A novel about a lighthouse, a logbook and a light that should not be lit.

The site of the book: the story, the author, and readings at the harbour office.

## Pages

- [Home](https://harbour-lights.example/): The book, and where to buy it.
- [About](https://harbour-lights.example/about.html): Maren Holt, the island and the lights.
- News: readings and the second draft.

## Optional

- [Sitemap](https://harbour-lights.example/sitemap.xml)
"""

FILES = {
    'index.html': INDEX,
    'sitemap.xml': SITEMAP,
    'llms.txt': LLMS,
    'about.html': ABOUT,
    'contact.html': CONTACT,
    'news.html': NEWS,
    os.path.join('css', 'site.css'): SITE_CSS,
    os.path.join('css', 'print.css'): PRINT_CSS,
}


def hide(path):
    # Hidden on Windows: the marker is not shown in the file browser
    ctypes.windll.kernel32.SetFileAttributesW(path, 0x02)


def make_demo():
    """The demo site in C:\\Sites (made again each time; a C:\\Sites that is not the demo is left alone)"""
    if os.path.exists(SITES) and not os.path.exists(MARKER):
        sys.exit(SITES + ' is there and is not the demo of this script: nothing was changed')
    if os.path.exists(SITES):
        shutil.rmtree(SITES)
    os.makedirs(SITES)
    with open(MARKER, 'w', encoding='utf-8') as f:
        f.write('Made by tools/screenshots.py of Blyant Validator. It is made again each time the script runs.\n')
    hide(MARKER)
    for name, text in FILES.items():
        path = os.path.join(SITE, name)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(text)
    # Pictures (only their folder is shown)
    os.makedirs(os.path.join(SITE, 'images'), exist_ok=True)
    for name in ('cover.jpg', 'maren.jpg'):
        with open(os.path.join(SITE, 'images', name), 'wb') as f:
            f.write(bytes(2048))


# ---------------------------------------------------------------------------------------------------------------
# The data directory of the demo: the settings, with where the panes are

SETTINGS = {
    'browsedir1': SITE, 'browsedir2': os.path.join(SITE, 'css'), 'browsesel1': 'about.html',
    'browsesel2': 'site.css', 'browsepane': '1', 'browsedual': 'true',
}


def make_data(work, settings):
    """The settings of a scene in "data" in the directory the program is started in (as Blyant's script does)"""
    data = os.path.join(work, 'data')
    if os.path.exists(data):
        shutil.rmtree(data)
    os.makedirs(data)
    values = dict(SETTINGS, **settings)
    lines = ['; Blyant Validator screenshots', '[SETTINGS]'] + ['%s=%s' % item for item in values.items()]
    with open(os.path.join(data, 'settings.ini'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')


# ---------------------------------------------------------------------------------------------------------------
# Running the program in a pseudo console

KEYS = {'<Esc>': '\x1b', '<Enter>': '\r', '<Tab>': '\t', '<Down>': '\x1b[B', '<Up>': '\x1b[A',
        '<Left>': '\x1b[D', '<Right>': '\x1b[C', '<BS>': '\x7f'}


def run(exe, work, args, steps):
    """Start the program, send the steps of keys, and return the screen when all is drawn"""
    proc = winpty.PtyProcess.spawn([exe] + args, cwd=work, dimensions=(ROWS, COLS))
    screen = pyte.Screen(COLS, ROWS)
    stream = pyte.Stream(screen)
    chunks = queue.Queue()

    def reader():
        while True:
            try:
                data = proc.read(65536)
            except Exception:
                return
            if data:
                chunks.put(data)

    threading.Thread(target=reader, daemon=True).start()

    def pump(seconds):
        end = time.time() + seconds
        while time.time() < end:
            try:
                stream.feed(chunks.get(timeout=0.05))
            except queue.Empty:
                pass

    # (the files are checked in the background: their results come in while waiting)
    pump(4)
    for step in steps:
        for part in re.split(r'(<[^>]+>)', step):
            if part == '<wait>':
                pump(1)
            elif part:
                proc.write(KEYS.get(part, part))
                pump(0.3)
        pump(1.2)
    pump(1.5)
    proc.terminate(force=True)
    return screen


# ---------------------------------------------------------------------------------------------------------------
# The screen as an SVG of a terminal window (as in Blyant's screenshots)

# The 16 colors of the console (the palette of Windows Terminal, Campbell)
PALETTE = {
    'black': '#0C0C0C', 'red': '#C50F1F', 'green': '#13A10E', 'brown': '#C19C00', 'blue': '#0037DA',
    'magenta': '#881798', 'cyan': '#3A96DD', 'white': '#CCCCCC', 'brightblack': '#767676', 'brightred': '#E74856',
    'brightgreen': '#16C60C', 'brightbrown': '#F9F1A5', 'brightblue': '#3B78FF', 'brightmagenta': '#B4009E',
    'brightcyan': '#61D6D6', 'brightwhite': '#F2F2F2',
}
DEFAULT_FG, DEFAULT_BG = '#CCCCCC', '#0C0C0C'
CELL_W, CELL_H, FONT_SIZE = 8.4, 17, 14
PAD, BAR = 14, 34


def color(name, default):
    if name == 'default':
        return default
    if name in PALETTE:
        return PALETTE[name]
    if re.fullmatch(r'[0-9a-fA-F]{6}', name):
        return '#' + name
    return default


def svg(screen, title, show_cursor):
    width = PAD * 2 + COLS * CELL_W
    height = BAR + PAD * 2 + ROWS * CELL_H
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">'
           % (width, height, width, height),
           '<title>%s</title>' % html.escape(title),
           '<style>text{font-family:"Cascadia Mono","Cascadia Code",Consolas,"DejaVu Sans Mono",Menlo,'
           'monospace;font-size:%dpx;white-space:pre}</style>' % FONT_SIZE,
           '<rect width="100%%" height="100%%" rx="10" fill="%s"/>' % DEFAULT_BG,
           '<rect width="100%%" height="%d" rx="10" fill="#2B2B2B"/>' % BAR,
           '<rect y="%d" width="100%%" height="10" fill="#2B2B2B"/>' % (BAR - 10)]
    for i, dot in enumerate(['#FF5F57', '#FEBC2E', '#28C840']):
        out.append('<circle cx="%d" cy="%d" r="6" fill="%s"/>' % (20 + i * 20, BAR // 2, dot))
    out.append('<text x="%.1f" y="%d" fill="#A0A0A0" text-anchor="middle">%s</text>'
               % (width / 2, BAR // 2 + 5, html.escape(title)))
    top = BAR + PAD
    cursor = None if screen.cursor.hidden or not show_cursor else (screen.cursor.y, screen.cursor.x)
    for y in range(ROWS):
        row = screen.buffer[y]
        cells = []
        for x in range(COLS):
            char = row[x]
            fg, bg = color(char.fg, DEFAULT_FG), color(char.bg, DEFAULT_BG)
            if char.reverse:
                fg, bg = bg, fg
            if cursor == (y, x):
                fg, bg = DEFAULT_BG, DEFAULT_FG
            cells.append((char.data or ' ', fg, bg, char.bold))
        # The backgrounds, in runs of the same color
        x = 0
        while x < COLS:
            bg = cells[x][2]
            end = x
            while end < COLS and cells[end][2] == bg:
                end += 1
            if bg != DEFAULT_BG:
                out.append('<rect x="%.1f" y="%d" width="%.1f" height="%d" fill="%s"/>'
                           % (PAD + x * CELL_W, top + y * CELL_H, (end - x) * CELL_W, CELL_H, bg))
            x = end
        # The text, in runs of the same color (each run placed at its column, so any font lines up)
        x = 0
        while x < COLS:
            fg, bold = cells[x][1], cells[x][3]
            end = x
            while end < COLS and cells[end][1] == fg and cells[end][3] == bold:
                end += 1
            text = ''.join(cell[0] for cell in cells[x:end])
            if text.strip():
                stripped = text.rstrip()
                out.append('<text x="%.1f" y="%.1f" fill="%s"%s textLength="%.1f" lengthAdjust="spacingAndGlyphs"'
                           ' xml:space="preserve">%s</text>'
                           % (PAD + x * CELL_W, top + y * CELL_H + CELL_H * 0.75, fg,
                              ' font-weight="bold"' if bold else '', len(stripped) * CELL_W,
                              html.escape(stripped)))
            x = end
    out.append('</svg>')
    return '\n'.join(out) + '\n'


# ---------------------------------------------------------------------------------------------------------------
# The scenes: a name, a title, the settings, the arguments of the program, the keys, and if the cursor is drawn

SCENES = [
    ('file-browser', 'Blyant Validator: the files of a site, with their results', {}, [], ['<wait>'], False),
    ('peek', 'Blyant Validator: V, the peek of a file with its problems marked', {}, [], ['V'], False),
    ('peek-problem', 'Blyant Validator: in the peek, n goes from problem to problem', {}, [],
     ['V', '<Tab>', 'n'], False),
    ('problems', 'Blyant Validator: P, the problems of a file', {}, [], ['P', '<Tab>', 'jj'], False),
    ('css', 'Blyant Validator: a style sheet in the peek', {'browsepane': '2'}, [], ['V', '<Tab>', 'n'], False),
    ('format', 'Blyant Validator: F, formatting a file after asking', {'browsesel1': 'news.html'}, [], ['F'], False),
    ('sitemap', 'Blyant Validator: a sitemap, checked by the rules of sitemaps.org', {'browsesel1': 'sitemap.xml'}, [],
     ['V', '<Tab>', 'n'], False),
    ('llms-txt', 'Blyant Validator: llms.txt, checked against the llms.txt format', {'browsesel1': 'llms.txt'}, [],
     ['V', '<Tab>', 'n'], False),
]


def main():
    parser = argparse.ArgumentParser(description='Screenshots of the TUI of Blyant Validator as SVG images')
    parser.add_argument('--exe', default=os.path.join(ROOT, 'src', 'Blyant.Validator.Cli', 'bin', 'Release',
                                                      'net10.0', 'blyant-validator.exe'))
    parser.add_argument('scenes', nargs='*', help='the scenes to make (all when none is given)')
    options = parser.parse_args()
    if not os.path.exists(options.exe):
        sys.exit('The program is not there: ' + options.exe + ' (run dotnet build -c Release, or give --exe)')
    make_demo()
    os.makedirs(OUT, exist_ok=True)
    work = os.path.join(tempfile.gettempdir(), 'blyant-validator-screenshots')
    os.makedirs(work, exist_ok=True)
    for name, title, settings, args, steps, show_cursor in SCENES:
        if options.scenes and name not in options.scenes:
            continue
        make_data(work, settings)
        screen = run(options.exe, work, args, steps)
        with open(os.path.join(OUT, name + '.svg'), 'w', encoding='utf-8', newline='\n') as f:
            f.write(svg(screen, title, show_cursor))
        print(name)


if __name__ == '__main__':
    main()
