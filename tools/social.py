"""Draws the social preview of the repository on GitHub (tools/github-social-preview.png, 1280 x 640), in the style
of Blyant's (the tools/images.py of the Blyant website): a frame as Blyant draws it, Blyant's logo, the word
VALIDATOR with the checkmark icon of the program, what it does, and where it is.

    python tools/social.py

Needs Pillow (pip install pillow), and a monospaced font on this computer (Cascadia Mono, Consolas, DejaVu Sans
Mono): only to draw the image, not to build. The image is kept in git, and uploaded by hand to the repository
(Settings, General, Social preview); run this when the words or the icon change.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "tools", "github-social-preview.png")
ICON = os.path.join(ROOT, "src", "Blyant.Validator.Cli", "BlyantValidator.ico")
WIDTH, HEIGHT = 1280, 640

# The colors of Blyant's card
BG = (16, 20, 24)
FRAME = (58, 67, 76)
CYAN = (97, 214, 214)
YELLOW = (249, 241, 165)
TEXT = (204, 204, 204)
DIM = (138, 143, 148)
FONTS = [
    r"C:\Windows\Fonts\CascadiaMono.ttf",
    r"C:\Windows\Fonts\consola.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    "/System/Library/Fonts/Menlo.ttc",
]

# Blyant's logo (the built-in logo of the editor, LogoArt.cs)
LOGO = [
    '"""""MM_ MMM      MMM  MMM """""MM_ M_   MMM """MMM"""',
    'MMM _MM" M%M      "MM__MM" MMM _M%M MMM_ M%M    M%M',
    'M%M" M%M M%M  ___   M%M    M%M" M%M M%M"_M%M    M%M',
    'MMM__MM" "MM__MMM   MMM    MMM  MMM MMM  "MM    MMM',
]


def font(size):
    for path in FONTS:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    sys.exit("social.py: no monospaced font found (" + ", ".join(FONTS) + ")")


def centered(draw, y, text, size, fill):
    face = font(size)
    draw.text(((WIDTH - draw.textlength(text, font=face)) / 2, y), text, font=face, fill=fill)


def main():
    img = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(img)

    # A frame as Blyant draws it: . and ' at the corners, - and | between
    small = font(22)
    cell = draw.textlength("-", font=small)
    cols = int((WIDTH - 80) // cell)
    x0 = (WIDTH - cell * cols) / 2
    draw.text((x0, 30), "." + "-" * (cols - 2) + ".", font=small, fill=FRAME)
    draw.text((x0, HEIGHT - 58), "'" + "-" * (cols - 2) + "'", font=small, fill=FRAME)
    for y in range(58, HEIGHT - 58, 26):
        draw.text((x0, y), "|", font=small, fill=FRAME)
        draw.text((x0 + cell * (cols - 1), y), "|", font=small, fill=FRAME)

    # Blyant's logo, two thirds of the width
    size = 20
    widest = "M" * max(len(row) for row in LOGO)
    while draw.textlength(widest, font=font(size + 1)) < WIDTH * 0.66:
        size += 1
    big = font(size)
    line = size * 1.12
    lx = (WIDTH - draw.textlength(widest, font=big)) / 2
    for i, row in enumerate(LOGO):
        draw.text((lx, 92 + i * line), row, font=big, fill=CYAN)

    # VALIDATOR, spaced out, with the checkmark icon in front of it
    word = "V A L I D A T O R"
    face = font(40)
    icon_size = 48
    gap = 18
    total = icon_size + gap + draw.textlength(word, font=face)
    x = (WIDTH - total) / 2
    y = 92 + 4 * line + 26
    icon = Image.open(ICON)
    icon.size = max(icon.ico.sizes())
    icon = icon.convert("RGBA").resize((icon_size, icon_size), Image.LANCZOS)
    img.paste(icon, (int(x), int(y - 2)), icon)
    draw.text((x + icon_size + gap, y), word, font=face, fill=YELLOW)

    centered(draw, 352, "Validate and format HTML, CSS, XML and Markdown,", 32, TEXT)
    centered(draw, 396, "side by side with your editor, in any terminal.", 32, TEXT)
    centered(draw, 466, "Windows  Linux  macOS  -  free and open source", 22, DIM)
    centered(draw, 508, "github.com/artstisen/blyant-validator", 30, YELLOW)

    img.save(OUT, optimize=True)
    print("Wrote tools/github-social-preview.png", f"({os.path.getsize(OUT) // 1024} KB)")


if __name__ == "__main__":
    main()
