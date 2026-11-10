"""Draws the icon of blyant-validator.exe in the style of Blyant's icon (Blyant's tools/icon.py): a checkmark in
the colors of the Default scheme on a dark terminal square. The checkmark is shaded as the pencil of Blyant's icon:
three bands, light at the top, cyan in the middle and dark at the bottom. It is drawn big, then made smaller for each
size of the .ico (16 to 256 pixels; the small sizes are drawn simpler, in one color and a little thicker).

    python tools/icon.py [directory]

writes src/Blyant.Validator.Cli/BlyantValidator.ico, and BlyantValidator.svg next to it (the detailed icon as SVG,
for a web site or print). With a directory given, it writes them there, with the sizes as PNG and a preview of them
all. Needs Pillow (pip install pillow): only to draw the icon, not to build the program.
"""
import math
import os
import sys
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
OUT = sys.argv[1] if len(sys.argv) > 1 else None
if OUT:
    os.makedirs(OUT, exist_ok=True)
S = 1024  # The size it is drawn in

# Windows Terminal's "Campbell" colors, which the Default scheme is seen in on Windows (as in Blyant's icon)
BG = (16, 20, 24, 255)
BG_EDGE = (52, 62, 70, 255)
CYAN = (97, 214, 214, 255)
CYAN_LIGHT = (160, 236, 236, 255)
CYAN_DARK = (40, 150, 160, 255)

# The checkmark: its middle line, from the end of the short stroke, through the corner, to the end of the long one
CHECK = [(0, 0), (190, 190), (540, -200)]
M = 40  # The margin around the square
EDGE = 18  # The width of the edge of the square


def normal(a, b):
    """The normal at the left of the line from a to b (upwards for both strokes of the checkmark), of length 1"""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    return dy / length, -dx / length


def band(points, d0, d1):
    """The part of a stroke along the line between two distances from it (d0 > d1, upwards positive): a polygon"""
    p0, corner, p2 = points
    n1, n2 = normal(p0, corner), normal(corner, p2)
    mx, my = n1[0] + n2[0], n1[1] + n2[1]
    m_len = math.hypot(mx, my)
    mx, my = mx / m_len, my / m_len
    miter = 1 / (mx * n1[0] + my * n1[1])  # How far the corner of an edge is, for a distance of 1

    def at(d):
        return [(p0[0] + n1[0] * d, p0[1] + n1[1] * d),
                (corner[0] + mx * miter * d, corner[1] + my * miter * d),
                (p2[0] + n2[0] * d, p2[1] + n2[1] * d)]

    return at(d0) + list(reversed(at(d1)))


def bands(simple):
    """The checkmark as polygons and their colors, put in the middle of the square"""
    width = 230 if simple else 170
    third = width / 3
    parts = [(band(CHECK, width / 2, -width / 2), CYAN)] if simple else [
        (band(CHECK, width / 2, third / 2), CYAN_LIGHT),
        (band(CHECK, third / 2, -third / 2), CYAN),
        (band(CHECK, -third / 2, -width / 2), CYAN_DARK),
    ]
    xs = [x for polygon, _ in parts for x, _ in polygon]
    ys = [y for polygon, _ in parts for _, y in polygon]
    dx = S / 2 - (min(xs) + max(xs)) / 2
    dy = S / 2 - (min(ys) + max(ys)) / 2
    return [([(x + dx, y + dy) for x, y in polygon], color) for polygon, color in parts]


def icon(simple):
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # The terminal: a dark rounded square
    d.rounded_rectangle([M, M, S - M, S - M], radius=190, fill=BG, outline=BG_EDGE, width=0 if simple else EDGE)
    for polygon, color in bands(simple):
        d.polygon(polygon, fill=color)
    return img


def hexcolor(color):
    return '#%02x%02x%02x' % color[:3]


def svg():
    """The detailed icon as SVG (for a web site or print): the same shapes as the big drawing"""
    shapes = ['<polygon points="%s" fill="%s"/>' % (' '.join('%.1f,%.1f' % p for p in polygon), hexcolor(color))
              for polygon, color in bands(False)]
    return '\n'.join([
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" width="%d" height="%d">' % (S, S, S, S),
        '<title>Blyant Validator</title>',
        # The terminal: the edge is drawn inside the square, as in the PNG
        '<rect x="%g" y="%g" width="%g" height="%g" rx="%g" fill="%s" stroke="%s" stroke-width="%d"/>' % (
            M + EDGE / 2, M + EDGE / 2, S - 2 * M - EDGE, S - 2 * M - EDGE, 190 - EDGE / 2, hexcolor(BG),
            hexcolor(BG_EDGE), EDGE),
        *shapes,
        '</svg>',
        ''])


big = icon(False)
small = icon(True)
sizes = [256, 128, 64, 48, 32, 24, 16]
frames = [(small if size <= 32 else big).resize((size, size), Image.LANCZOS) for size in sizes]
target = OUT or os.path.join(ROOT, 'src', 'Blyant.Validator.Cli')
ico = os.path.join(target, 'BlyantValidator.ico')
frames[0].save(ico, sizes=[(s, s) for s in sizes], append_images=frames[1:])
print('Written:', os.path.normpath(ico))
vector = os.path.join(target, 'BlyantValidator.svg')
with open(vector, 'w', encoding='utf-8', newline='\n') as file:
    file.write(svg())
print('Written:', os.path.normpath(vector))
if not OUT:
    sys.exit(0)
big.save(os.path.join(OUT, 'blyant-validator-1024.png'))
for f in frames:
    f.save(os.path.join(OUT, 'blyant-validator-%d.png' % f.size[0]))

# A preview of all the sizes side by side, on a light and a dark background, at their real size and enlarged
sheet = Image.new('RGBA', (1400, 760), (255, 255, 255, 255))
d = ImageDraw.Draw(sheet)
d.rectangle([0, 380, 1400, 760], fill=(32, 32, 32, 255))
for row, y in ((0, 40), (1, 420)):
    x = 20
    for f in frames:
        sheet.alpha_composite(f, (x, y))
        x += f.size[0] + 16
    # The small ones enlarged 4 times, as pixels
    x = 900
    for f in frames[-3:]:
        e = f.resize((f.size[0] * 4, f.size[1] * 4), Image.NEAREST)
        sheet.alpha_composite(e, (x, y + 160))
        x += e.size[0] + 16
sheet.save(os.path.join(OUT, 'preview.png'))
