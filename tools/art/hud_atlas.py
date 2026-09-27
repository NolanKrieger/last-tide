# uv run -q --with pillow python tools/art/hud_atlas.py <cut-dir> <out.png>
# Builds assets/art/hud/hud-atlas.png: the HUD's generated icons and ornaments (cut from the Codex sheets by
# tools/art/cut.py, toned 35% toward the chart's sepia) plus procedural shapes (disc, ring, glow, chart plates,
# ribbon) in ONE texture, so the whole HUD draws from a single atlas in batched rects. The cell layout must match
# game/Hud.cs (HudAtlas): icons are 128 px cells, 8 per row, in NAMES order; shapes sit at fixed rects below.
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageOps

src, out = sys.argv[1], sys.argv[2]
NAMES = ['coin', 'crate', 'sack', 'shot', 'timber', 'sailor', 'cannon', 'sail',
         'repair', 'pump', 'anchor', 'spade', 'bell', 'sun', 'moon', 'lantern',
         'spyglass', 'hourglass', 'm_reef_serpent', 'm_kraken', 'm_ghost_ship', 'm_crocodile', 'm_weed_kraken', 'm_siren',
         'o_corner', 'o_seal', 'o_pin', 'o_rosette']
WIDE = {'o_hand': (512, 384, 256, 128), 'o_divider': (768, 384, 256, 128)}
PAPER = (238, 226, 202); INK = (41, 33, 26)
A = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))

def tone(im, amt=0.35):
    rgb = im.convert('RGB'); a = im.getchannel('A')
    g = ImageOps.grayscale(rgb)
    sep = ImageOps.colorize(g, (38, 28, 18), (250, 240, 216))
    o = Image.blend(rgb, sep, amt); o.putalpha(a); return o

def fit(im, w, h, pad):
    bb = im.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    im = im.crop(bb)
    s = min((w - 2 * pad) / im.width, (h - 2 * pad) / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    c = Image.new('RGBA', (w, h), (0, 0, 0, 0)); c.paste(im, ((w - im.width) // 2, (h - im.height) // 2)); return c

for i, n in enumerate(NAMES):
    im = Image.open(os.path.join(src, n + '.png')).convert('RGBA')
    A.alpha_composite(fit(tone(im), 128, 128, 8), ((i % 8) * 128, (i // 8) * 128))
for n, (x, y, w, h) in WIDE.items():
    im = Image.open(os.path.join(src, n + '.png')).convert('RGBA')
    A.alpha_composite(fit(tone(im), w, h, 8), (x, y))

SS = 4
def shape(w, h, draw_fn):
    big = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(big), SS)
    return big.resize((w, h), Image.LANCZOS)

# Row at y=512: disc, ring, thin ring, glow.
A.alpha_composite(shape(64, 64, lambda d, s: d.ellipse((2 * s, 2 * s, 62 * s, 62 * s), fill=(255, 255, 255, 255))), (0, 512))
A.alpha_composite(shape(64, 64, lambda d, s: d.ellipse((2 * s, 2 * s, 62 * s, 62 * s), outline=(255, 255, 255, 255), width=6 * s)), (64, 512))
A.alpha_composite(shape(64, 64, lambda d, s: d.ellipse((2 * s, 2 * s, 62 * s, 62 * s), outline=(255, 255, 255, 255), width=3 * s)), (128, 512))
glow = Image.new('RGBA', (128, 128), (255, 255, 255, 0))
gp = glow.load()
for y in range(128):
    for x in range(128):
        r = math.hypot(x - 63.5, y - 63.5) / 63.5
        gp[x, y] = (255, 255, 255, int(255 * max(0.0, 1 - r) ** 2))
A.alpha_composite(glow, (192, 512))

# Chart plates (9-slice, 192 px drawn at half size, margins 40 → 20): parchment a shade lighter than the sea, a
# darker aged rim and an inked double neatline; the second variant carries a latitude-scale strip between the rules.
def plate(graduated):
    w = h = 192
    im = Image.new('RGBA', (w, h), (245, 236, 215, 250))
    px = im.load()
    for y in range(h):
        for x in range(w):
            e = min(x, y, w - 1 - x, h - 1 - y)
            rim = max(0.0, 1 - e / 30.0) ** 1.7
            k = 1 - 0.11 * rim
            r, g, b, a = px[x, y]
            px[x, y] = (int(r * k), int(g * k * 0.985), int(b * k * 0.95), a)
    big = im.resize((w * SS, h * SS), Image.NEAREST)
    d = ImageDraw.Draw(big)
    ink = INK + (240,)
    def rect(i, width, col=ink):
        d.rectangle((i * SS, i * SS, (w - i) * SS - 1, (h - i) * SS - 1), outline=col, width=int(width * SS))
    rect(2, 4.4)
    if graduated:
        o, t = 11, 9
        rect(o, 1.8); rect(o + t, 1.8)
        for k2 in range(0, w, 32):
            for (x0, y0, x1, y1) in [(k2, o, k2 + 16, o + t), (k2, h - o - t, k2 + 16, h - o), (o, k2, o + t, k2 + 16), (w - o - t, k2, w - o, k2 + 16)]:
                x0 = max(x0, o); y0 = max(y0, o); x1 = min(x1, w - o); y1 = min(y1, h - o)
                if x1 > x0 and y1 > y0:
                    d.rectangle((x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1), fill=ink)
    else:
        rect(12, 2.0, INK + (205,))
    return big.resize((w, h), Image.LANCZOS)
RECTS = {}
def put(name, im, x, y):
    A.alpha_composite(im, (x, y)); RECTS[name] = (x, y, im.width, im.height)
put('plate', plate(False), 320, 512)
put('plate_scale', plate(True), 512, 512)

# Ribbon (notices): fill and line layers drawn white so the game tints them. 512x64: swallowtail ends 56 px each.
def ribbon(line):
    w, h = 512, 64
    def f(d, s):
        tail = 56
        body = [(tail * s, 8 * s), ((w - tail) * s, 8 * s), ((w - tail) * s, (h - 12) * s), (tail * s, (h - 12) * s)]
        lend = [(4 * s, 16 * s), ((tail + 2) * s, 16 * s), ((tail + 2) * s, (h - 3) * s), (4 * s, (h - 3) * s), (22 * s, (h + 13) * s // 2)]
        rend = [((w - 4) * s, 16 * s), ((w - tail - 2) * s, 16 * s), ((w - tail - 2) * s, (h - 3) * s), ((w - 4) * s, (h - 3) * s), ((w - 22) * s, (h + 13) * s // 2)]
        if line:
            for poly in (lend, rend):
                d.line(poly + [poly[0]], fill=(255, 255, 255, 255), width=int(2.4 * s), joint='curve')
            d.line(body + [body[0]], fill=(255, 255, 255, 255), width=int(2.6 * s), joint='curve')
            d.line([(tail * s, (h - 12) * s), ((tail - 12) * s, (h - 3) * s)], fill=(255, 255, 255, 255), width=int(2 * s))
            d.line([((w - tail) * s, (h - 12) * s), ((w - tail + 12) * s, (h - 3) * s)], fill=(255, 255, 255, 255), width=int(2 * s))
        else:
            d.polygon(lend, fill=(206, 206, 206, 255)); d.polygon(rend, fill=(206, 206, 206, 255))
            d.polygon(body, fill=(255, 255, 255, 255))
    return shape(w, h, f)
put('ribbon_fill', ribbon(False), 0, 704)
put('ribbon_line', ribbon(True), 0, 768)

# Sprites for the rose and the markers, all pointing right (+X) so the game rotates them: the wind arrow, the heading
# needle, a feather barb, a plan-view hull, a pointer triangle, a water drop. Halo variants are the same shape swollen.
def poly_shape(w, h, pts, grow=0.0, line=False, width=2.0):
    def f(d, s):
        P = [(x * s, y * s) for x, y in pts]
        if line:
            d.line(P + [P[0]], fill=(255, 255, 255, 255), width=int(width * s), joint='curve')
        else:
            d.polygon(P, fill=(255, 255, 255, 255))
            if grow > 0:
                d.line(P + [P[0]], fill=(255, 255, 255, 255), width=int(grow * 2 * s), joint='curve')
    return shape(w, h, f)
arrow = [(8, 19.5), (198, 17.5), (192, 4), (250, 24), (192, 44), (198, 30.5), (8, 28.5)]
put('arrow', poly_shape(256, 48, arrow), 0, 832)
put('arrow_halo', poly_shape(256, 48, arrow, grow=3.5), 256, 832)
needle = [(6, 16), (52, 8.5), (186, 16), (52, 23.5)]
put('needle', poly_shape(192, 32, needle), 512, 832)
put('needle_halo', poly_shape(192, 32, needle, grow=3.0), 512, 864)
put('barb', shape(48, 12, lambda d, s: d.line([(6 * s, 6 * s), (42 * s, 6 * s)], fill=(255, 255, 255, 255), width=5 * s)), 704, 832)
hull = [(3, 8), (14, 3.5), (34, 2.5), (50, 6), (62, 14), (50, 22), (34, 25.5), (14, 24.5), (3, 20)]
put('hull_fill', poly_shape(64, 28, hull), 768, 832)
put('hull_line', poly_shape(64, 28, hull, line=True, width=2.6), 832, 832)
put('tri', poly_shape(32, 32, [(4, 4), (30, 16), (4, 28)]), 896, 832)
def drop(d, s):
    d.ellipse((4 * s, 12 * s, 20 * s, 28 * s), fill=(255, 255, 255, 255))
    d.polygon([(12 * s, 1 * s), (19 * s, 16 * s), (5 * s, 16 * s)], fill=(255, 255, 255, 255))
put('drop', shape(24, 32, drop), 928, 832)
# The ship-card diagram's hull (plan view, bow right) at a size that is only ever drawn smaller.
big_hull = [(6, 20), (30, 9), (80, 5), (130, 9), (164, 22), (188, 36), (164, 50), (130, 63), (80, 67), (30, 63), (6, 52)]
put('hull_big_fill', poly_shape(192, 72, big_hull), 0, 896)
put('hull_big_line', poly_shape(192, 72, big_hull, line=True, width=3.2), 192, 896)
deck = [(22, 26), (40, 18), (84, 15), (128, 19), (152, 30), (164, 36), (152, 42), (128, 53), (84, 57), (40, 54), (22, 46)]
put('deck', poly_shape(192, 72, deck), 384, 896)
# A square sail on its yard for the helm's sail gauge (fill and line layers; drawn scaled to the sail set).
def sail_pts():
    pts = [(7, 4), (57, 4)]
    for i in range(1, 12):
        t = i / 12; pts.append((57 + 3.5 * math.sin(math.pi * t), 4 + 54 * t))
    for i in range(0, 13):
        t = i / 12; pts.append((57 - 50 * t, 58 + 5 * math.sin(math.pi * t)))
    for i in range(11, 0, -1):
        t = i / 12; pts.append((7 - 3.5 * math.sin(math.pi * t), 4 + 54 * t))
    return pts
put('sail_fill', poly_shape(64, 68, sail_pts()), 576, 896)
def sail_line(d, s):
    P = [(x * s, y * s) for x, y in sail_pts()]
    d.line(P + [P[0]], fill=(255, 255, 255, 255), width=int(2.6 * s), joint='curve')
    for sx in (24, 40):
        d.line([(sx * s, 6 * s), (sx * s, 60 * s)], fill=(255, 255, 255, 150), width=int(1.2 * s))
put('sail_line', shape(64, 68, sail_line), 640, 896)

# The UI kit's pieces (ui-screens' theme: assets/art/ui, assets/art/goods), copied in so the HUD and the screens share one
# paper, rim, key cap, ribbon and icon set while the HUD still draws from this single texture.
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'assets', 'art')
def kit(name):
    return Image.open(os.path.join(ROOT, 'ui', name + '.png')).convert('RGBA')
def scaled(im, w, h):
    return im.resize((w, h), Image.LANCZOS)
put('kit_rim', scaled(kit('rim'), 128, 128), 704, 512)
paper = Image.open(os.path.join(ROOT, 'ui', 'paper.png')).convert('RGBA')
put('kit_paper', paper.crop((64, 64, 320, 320)).resize((128, 128), Image.LANCZOS), 832, 512)
put('kit_key', kit('key'), 960, 512)
put('kit_key_hot', kit('key-hot'), 960, 552)
put('kit_corner', fit(kit('corner'), 64, 64, 0), 704, 640)
from PIL import ImageEnhance
def hud_tone(im):
    # The kit's pale illustrations (a cream sail, a pale pump) vanish on cream paper at 19 px: a touch darker and crisper.
    a = im.getchannel('A')
    rgb = ImageEnhance.Contrast(ImageEnhance.Brightness(im.convert('RGB')).enhance(0.78)).enhance(1.25)
    rgb.putalpha(a)
    return rgb
for i, n in enumerate(['station-guns', 'station-sails', 'station-repair', 'station-pumps']):
    put('kit_' + n, fit(hud_tone(kit(n)), 64, 64, 3), 768 + 64 * i, 640)
for i, n in enumerate(['icon-gold', 'icon-crate', 'icon-anchor', 'icon-hourglass']):
    put('kit_' + n, fit(kit(n), 64, 64, 3), 512 + 64 * i, 704)
goods = Image.open(os.path.join(ROOT, 'goods', 'goods.png')).convert('RGBA')
for i, n in enumerate(['provisions', 'munitions', 'timber']):
    put('kit_good_' + n, fit(goods.crop((i * 128, 0, i * 128 + 128, 128)), 64, 64, 3), 768 + 64 * i, 704)
put('kit_ribbon', scaled(kit('ribbon'), 269, 64), 512, 768)

os.makedirs(os.path.dirname(out), exist_ok=True)
A.save(out, optimize=True)
print(out, A.size)
for k, v in RECTS.items(): print(f'    public static readonly Rect2 {k} = new({v[0]}, {v[1]}, {v[2]}, {v[3]});')
