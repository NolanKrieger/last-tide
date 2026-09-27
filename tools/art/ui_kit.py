# uv run -q --with pillow --with numpy python tools/art/ui_kit.py <art-cut-dir> <art-raw-dir>
# Builds the UI kit into assets/art/ui (+ title/goods/parts/people): procedural hand-inked 9-slice pieces (page rim,
# cards, buttons, ribbon tabs, key caps, checkboxes, ruled slider, wash, focus brackets), the tileable paper graded to
# the game's palette, the cartouche/ribbon/key art from Codex, and the icon atlases. Deterministic: rerun freely.
import sys, os, math, glob, subprocess, tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

CUT, RAW = sys.argv[1], sys.argv[2]
UI = 'assets/art/ui'
for d in ('assets/art/ui', 'assets/art/title', 'assets/art/goods', 'assets/art/parts', 'assets/art/people'):
    os.makedirs(d, exist_ok=True)

PAPER = (238, 226, 202); SHADE = (218, 202, 170); INK = (41, 33, 26); RED = (158, 41, 31)
rng = np.random.default_rng(1723)
SS = 4  # supersampling for procedural drawing

def save(im, path):
    im.save(path, optimize=True)
    print('wrote', path, im.size)

def smooth_noise(n, scale, seed):
    r = np.random.default_rng(seed)
    k = max(2, n // scale + 2)
    pts = r.random(k)
    x = np.linspace(0, k - 1.001, n)
    i = x.astype(int); f = x - i; f = f * f * (3 - 2 * f)
    return pts[i] * (1 - f) + pts[i + 1] * f

def periodic_noise(n, period, seed, amp=1.0):
    """1D noise that repeats every `period` samples (sum of sines with random phases)."""
    r = np.random.default_rng(seed)
    x = np.arange(n)
    v = np.zeros(n)
    for h in (1, 2, 3, 5, 8):
        v += r.random() * np.sin(2 * np.pi * h * x / period + r.random() * 6.28) / h
    v = (v - v.min()) / max(1e-6, v.max() - v.min())
    return v * amp

def down(im):
    return im.resize((im.width // SS, im.height // SS), Image.LANCZOS)

# ---------------------------------------------------------------- paper (tileable, graded to Ink.Paper)
def make_paper():
    src = Image.open(os.path.join(RAW, 'paper.png')).convert('RGB').resize((512, 512), Image.LANCZOS)
    a = np.asarray(src).astype(np.float32)
    mean = a.reshape(-1, 3).mean(0)
    var = (a - mean) * 0.75                      # keep the fibres, calm the stains
    a = np.clip(np.array(PAPER, np.float32) + var, 0, 255)
    rolled = np.roll(np.roll(a, 256, 0), 256, 1)
    y, x = np.mgrid[0:512, 0:512] / 511.0
    w = np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) * 2   # 0 at edges, 1 at centre
    w = np.clip(w * 1.6, 0, 1)[..., None]
    out = a * w + rolled * (1 - w)
    save(Image.fromarray(out.astype(np.uint8)), f'{UI}/paper.png')
    # A darker leaf for wells and the logbook's aged page.
    dark = np.clip(out * np.array([0.93, 0.905, 0.85]), 0, 255)
    save(Image.fromarray(dark.astype(np.uint8)), f'{UI}/paper-aged.png')

# ---------------------------------------------------------------- page rim (9-slice, centre transparent)
def make_rim():
    S = 256; M = 64
    rgba = np.zeros((S, S, 4), np.float32)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # distance to the nearest edge, perturbed along each edge so the sheet's edge is deckled
    per = S - 2 * M
    def edge_noise(seed):
        n = periodic_noise(S, per, seed)
        return 1.0 + 5.0 * n
    nt, nb, nl, nr = edge_noise(1), edge_noise(2), edge_noise(3), edge_noise(4)
    dt = yy - nt[xx.astype(int)]
    db = (S - 1 - yy) - nb[xx.astype(int)]
    dl = xx - nl[yy.astype(int)]
    dr = (S - 1 - xx) - nr[yy.astype(int)]
    d = np.minimum(np.minimum(dt, db), np.minimum(dl, dr))
    inside = d > 0
    # aged edge: a burnt sepia rim that fades inward, blotchy
    # blotchy ageing, periodic over the 9-slice's tiled middle (128 px) so the tiled edges have no seams
    per2 = S - 2 * M
    yy2, xx2 = np.mgrid[0:S, 0:S].astype(np.float32) * (2 * np.pi / per2)
    r2 = np.random.default_rng(77)
    blot = np.zeros((S, S), np.float32)
    for fx, fy in ((1, 0), (0, 1), (1, 1), (2, 1), (1, 2), (3, 2), (2, 3)):
        blot += r2.random() * np.sin(fx * xx2 + fy * yy2 + r2.random() * 6.28)
    blot = (blot - blot.min()) / (blot.max() - blot.min())
    fade = np.clip(1 - d / 38.0, 0, 1) ** 1.6 * (0.55 + 0.45 * blot)
    col = np.array([176, 140, 92], np.float32)
    edgecol = np.array([214, 196, 160], np.float32)
    alpha_fringe = np.clip(d / 1.2, 0, 1) * inside
    # within 9 px of the deckle the sheet is opaque (covers the straight edge of the paper fill beneath)
    solid = np.clip((10 - d) / 2.0, 0, 1) * alpha_fringe
    c = edgecol * solid[..., None] + col * (1 - solid[..., None])
    a = np.maximum(solid, fade * 0.8) * alpha_fringe
    rgba[..., :3] = c; rgba[..., 3] = a * 255
    im = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), 'RGBA')
    # double ink rule, printed (straight so it tiles), inset from the sheet edge
    big = Image.new('RGBA', (S * SS, S * SS), (0, 0, 0, 0)); dr_ = ImageDraw.Draw(big)
    for inset, w, al in ((18, 2.2, 235), (24, 1.1, 200)):
        o = inset * SS; ww = max(1, int(w * SS))
        dr_.rectangle([o, o, S * SS - 1 - o, S * SS - 1 - o], outline=INK + (al,), width=ww)
    im = Image.alpha_composite(im, down(big))
    save(im, f'{UI}/rim.png')

# ---------------------------------------------------------------- hand-inked boxes (cards, buttons, keys)
def wobbly_rect(draw, x0, y0, x1, y1, width, colour, seed, ss=SS, overshoot=0.0, jitter=0.35):
    r = np.random.default_rng(seed)
    def line(p, q):
        n = 12
        pts = []
        for i in range(n + 1):
            t = i / n
            px = p[0] + (q[0] - p[0]) * t; py = p[1] + (q[1] - p[1]) * t
            j = (r.random() - 0.5) * jitter * ss * (0 if i in (0, n) else 1)
            if p[0] == q[0]: px += j
            else: py += j
            pts.append((px, py))
        draw.line(pts, fill=colour, width=int(width * ss), joint='curve')
    ov = overshoot * ss
    line((x0 - ov, y0), (x1 + ov, y0)); line((x1, y0 - ov), (x1, y1 + ov))
    line((x1 + ov, y1), (x0 - ov, y1)); line((x0, y1 + ov), (x0, y0 - ov))

def box(w, h, fill, border, bw, seed, overshoot=0.0, bottom=0.0, radius=2):
    big = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
    m = 2 * SS
    if fill is not None:
        d.rounded_rectangle([m, m, w * SS - 1 - m, h * SS - 1 - m], radius=radius * SS, fill=fill)
    if border is not None:
        wobbly_rect(d, m, m, w * SS - 1 - m, h * SS - 1 - m, bw, border, seed, overshoot=overshoot)
        if bottom > 0:
            d.line([(m + 2 * SS, h * SS - 1 - m), (w * SS - 1 - m - 2 * SS, h * SS - 1 - m)], fill=border, width=int(bottom * SS))
    return down(big)

def make_boxes():
    save(box(96, 96, SHADE + (70,), INK + (215,), 1.3, 11, overshoot=3), f'{UI}/card.png')
    save(box(96, 96, (226, 212, 184, 150), None, 0, 12), f'{UI}/well.png')
    save(box(72, 44, (232, 219, 192, 255), INK + (230,), 1.5, 21), f'{UI}/btn.png')
    save(box(72, 44, (240, 222, 170, 255), INK + (255,), 1.8, 22), f'{UI}/btn-hover.png')
    save(box(72, 44, (62, 50, 38, 255), INK + (255,), 1.8, 23), f'{UI}/btn-press.png')
    save(box(72, 44, (232, 222, 200, 120), INK + (70,), 1.2, 24), f'{UI}/btn-off.png')
    save(box(44, 40, (246, 239, 222, 255), INK + (235,), 1.4, 31, bottom=3.2), f'{UI}/key.png')
    save(box(44, 40, (250, 232, 214, 255), RED + (255,), 1.8, 32, bottom=3.2), f'{UI}/key-hot.png')
    # focus: red corner brackets, centre transparent — drawn over any state so keyboard focus is always visible
    big = Image.new('RGBA', (48 * SS, 40 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
    L = 9 * SS; t = int(2.2 * SS); m = 1 * SS
    W, H = 48 * SS - 1, 40 * SS - 1
    for (cx, cy, sx, sy) in ((m, m, 1, 1), (W - m, m, -1, 1), (m, H - m, 1, -1), (W - m, H - m, -1, -1)):
        d.line([(cx, cy + sy * L), (cx, cy), (cx + sx * L, cy)], fill=RED + (255,), width=t, joint='curve')
    save(down(big), f'{UI}/focus.png')

# ---------------------------------------------------------------- ribbon tabs (forked ends)
def ribbon_tab(w, h, fill, edge, seed, fold):
    big = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
    n = 11 * SS; m = 2 * SS
    W, H = w * SS - 1, h * SS - 1
    poly = [(m, m), (W - m, m), (W - m - n, H / 2), (W - m, H - m), (m, H - m), (m + n, H / 2)]
    d.polygon(poly, fill=fill)
    # folds: a darker band near each end, as if the silk turns under
    d.polygon([(m, m), (m + 16 * SS, m), (m + 16 * SS, H - m), (m, H - m), (m + n, H / 2)], fill=fold)
    d.polygon([(W - m, m), (W - m - 16 * SS, m), (W - m - 16 * SS, H - m), (W - m, H - m), (W - m - n, H / 2)], fill=fold)
    d.polygon(poly, fill=None)
    d.line(poly + [poly[0]], fill=edge, width=int(1.6 * SS), joint='curve')
    d.line([(m + 16 * SS, m + 2 * SS), (m + 16 * SS, H - m - 2 * SS)], fill=edge[:3] + (90,), width=SS)
    d.line([(W - m - 16 * SS, m + 2 * SS), (W - m - 16 * SS, H - m - 2 * SS)], fill=edge[:3] + (90,), width=SS)
    return down(big)

def make_tabs():
    save(ribbon_tab(128, 40, (222, 204, 166, 255), INK + (220,), 41, (206, 186, 146, 255)), f'{UI}/tab.png')
    save(ribbon_tab(128, 40, (236, 216, 172, 255), INK + (255,), 42, (220, 198, 152, 255)), f'{UI}/tab-hover.png')
    save(ribbon_tab(128, 40, (150, 44, 34, 255), INK + (255,), 43, (118, 32, 24, 255)), f'{UI}/tab-on.png')
    save(ribbon_tab(128, 40, (58, 74, 92, 255), INK + (255,), 44, (44, 58, 74, 255)), f'{UI}/tab-cast.png')

# ---------------------------------------------------------------- checkboxes
def make_checks():
    for name, on, col in (('check-off', False, INK), ('check-on', True, INK), ('check-off-dis', False, INK[:3]), ('check-on-dis', True, INK[:3])):
        dis = name.endswith('dis')
        big = Image.new('RGBA', (26 * SS, 26 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
        a = 110 if dis else 235
        d.rounded_rectangle([3 * SS, 4 * SS, 21 * SS, 22 * SS], radius=2 * SS, fill=(246, 239, 222, 200 if not dis else 110))
        wobbly_rect(d, 3 * SS, 4 * SS, 21 * SS, 22 * SS, 1.6, INK + (a,), 51, overshoot=1.5)
        if on:
            # a brush tick that overshoots the box, thick at the heel, tapering at the tip
            pts = [(6, 13), (10, 18), (23, 3)]
            for k in range(8):
                t = k / 7
                w = (3.6 - 2.2 * t) * SS
                p0 = (pts[0][0] + (pts[1][0] - pts[0][0]) * min(1, t * 2), pts[0][1] + (pts[1][1] - pts[0][1]) * min(1, t * 2))
            d.line([(6 * SS, 13 * SS), (10 * SS, 18.5 * SS)], fill=(RED if not dis else INK) + (a,), width=int(3.4 * SS))
            d.line([(10 * SS, 18.5 * SS), (16 * SS, 10 * SS), (23.5 * SS, 2.5 * SS)], fill=(RED if not dis else INK) + (a,), width=int(2.6 * SS), joint='curve')
        save(down(big), f'{UI}/{name}.png')

# ---------------------------------------------------------------- slider: a ruled line with ticks, a wash when filled
def make_slider():
    for name, filled in (('slider', False), ('slider-fill', True), ('slider-fill-hi', True)):
        w, h = 64, 14
        big = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
        y0, y1 = 4 * SS, 10 * SS
        if filled:
            col = (132, 96, 58, 210) if name == 'slider-fill' else (158, 41, 31, 200)
            d.rectangle([0, y0, w * SS, y1], fill=col)
        d.line([(0, y0), (w * SS, y0)], fill=INK + (230,), width=int(1.3 * SS))
        d.line([(0, y1), (w * SS, y1)], fill=INK + (230,), width=int(1.3 * SS))
        for x in range(0, w, 8):
            d.line([(x * SS + 4 * SS, y1), (x * SS + 4 * SS, y1 + 3 * SS)], fill=INK + (160,), width=SS)
        save(down(big), f'{UI}/{name}.png')
    seal = Image.open(os.path.join(CUT, 'seals-00.png')).convert('RGBA')
    g = seal.resize((30, 30), Image.LANCZOS)
    save(g, f'{UI}/grabber.png')
    hi = Image.fromarray(np.clip(np.asarray(g).astype(np.float32) * np.array([1.12, 1.12, 1.12, 1]), 0, 255).astype(np.uint8), 'RGBA')
    save(hi, f'{UI}/grabber-hi.png')
    a = np.asarray(g).astype(np.float32); lum = a[..., :3].mean(-1, keepdims=True)
    grey = np.concatenate([lum * 0.9 + 40, lum * 0.9 + 36, lum * 0.9 + 30, a[..., 3:] * 0.6], -1)
    save(Image.fromarray(np.clip(grey, 0, 255).astype(np.uint8), 'RGBA'), f'{UI}/grabber-off.png')

# ---------------------------------------------------------------- watercolour wash (menu hover) and brush underline (focus)
def make_wash():
    w, h = 160, 56
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    nx = (xx - w / 2) / (w / 2 - 6); ny = (yy - h / 2) / (h / 2 - 6)
    r = np.sqrt(nx ** 4 + ny ** 2)  # a squarish ellipse, stretches well horizontally
    wob = np.asarray(Image.fromarray((rng.random((8, 20)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
    edge = 0.92 + 0.12 * wob
    inside = np.clip((edge - r) * 12, 0, 1)
    pool = np.clip(1 - np.abs(edge - r) * 7, 0, 1) * 0.35    # pigment pools at the edge
    a = inside * (0.32 + 0.12 * wob) + pool * inside
    col = np.zeros((h, w, 4), np.float32)
    col[..., 0], col[..., 1], col[..., 2] = 214, 176, 104
    col[..., 3] = np.clip(a, 0, 1) * 255
    save(Image.fromarray(col.astype(np.uint8), 'RGBA'), f'{UI}/wash.png')
    red = col.copy(); red[..., 0], red[..., 1], red[..., 2] = 176, 70, 52
    save(Image.fromarray(red.astype(np.uint8), 'RGBA'), f'{UI}/wash-red.png')
    # brush underline: a tapered stroke
    W, H = 160, 20
    big = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
    for i in range(W * SS):
        t = i / (W * SS)
        th = (0.4 + 2.6 * math.sin(math.pi * t) ** 0.6) * SS
        y = (H * 0.55 + 1.2 * math.sin(t * 5.5)) * SS
        d.line([(i, y - th / 2), (i, y + th / 2)], fill=RED + (235,))
    save(down(big), f'{UI}/underline.png')

# ---------------------------------------------------------------- Codex pieces keyed
def key(raw, out, mx, pad=6):
    subprocess.run(['bash', 'tools/art/key.sh', raw, out, str(mx), str(pad)], check=True)

def make_codex_pieces():
    key(os.path.join(RAW, 'cartouche.png'), f'{UI}/cartouche.png', 1024)
    key(os.path.join(RAW, 'ribbon.png'), f'{UI}/ribbon.png', 768)
    key(os.path.join(RAW, 'keyart.png'), 'assets/art/title/keyart.png', 1024)
    def cp(src, dst, mx=None):
        im = Image.open(os.path.join(CUT, src)).convert('RGBA')
        if mx and max(im.size) > mx:
            s = mx / max(im.size); im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
        save(im, dst)
    for i, n in enumerate(('calm', 'rough', 'tempest')): cp(f'presets-{i:02d}.png', f'assets/art/title/preset-{n}.png', 320)
    for i, n in enumerate(('lookout', 'marines', 'quartermaster', 'deckhand', 'keeper')): cp(f'people-{i:02d}.png', f'assets/art/people/{n}.png', 200)
    cp('stations-04.png', 'assets/art/people/sailor.png', 128)
    for i, n in enumerate(('guns', 'sails', 'repair', 'pumps')): cp(f'stations-{i:02d}.png', f'{UI}/station-{n}.png', 128)
    for i, n in enumerate(('crown', 'anchor', 'pennant', 'cove')): cp(f'crests-{i:02d}.png', f'{UI}/crest-{n}.png', 160)
    cp('seals-01.png', f'{UI}/seal-best.png', 220); cp('seals-02.png', f'{UI}/seal-star.png', 128)
    for i, n in enumerate(('whale', 'serpent', 'hippocamp', 'fish')): cp(f'monsters-{i:02d}.png', f'{UI}/monster-{n}.png', 320)
    cp('logdraw-00.png', f'{UI}/log-sinking.png', 320); cp('logdraw-01.png', f'{UI}/log-blot.png', 200); cp('logdraw-02.png', f'{UI}/log-drip.png', 128)
    for i, n in enumerate(('gold', 'crate', 'tankard', 'bottle', 'anchor', 'hammer', 'quill', 'hourglass', 'map')): cp(f'misc-{i:02d}.png', f'{UI}/icon-{n}.png', 128)
    cp('ornaments-00.png', f'{UI}/corner.png', 256); cp('ornaments-01.png', f'{UI}/divider.png', 512)
    cp('ornaments-02.png', f'{UI}/fleuron.png', 96); cp('ornaments-03.png', f'{UI}/star.png', 128)

def atlas(files, cols, cell, out):
    rows = (len(files) + cols - 1) // cols
    sheet = Image.new('RGBA', (cols * cell, rows * cell), (0, 0, 0, 0))
    for k, f in enumerate(files):
        im = Image.open(f).convert('RGBA')
        s = min((cell - 16) / im.width, (cell - 16) / im.height)
        im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
        x = (k % cols) * cell + (cell - im.width) // 2; y = (k // cols) * cell + (cell - im.height) // 2
        sheet.paste(im, (x, y), im)
    save(sheet, out)

def make_atlases():
    goods = [os.path.join(CUT, f'goods-{s}-{i:02d}.png') for s in 'abc' for i in range(9)]
    atlas(goods, 8, 128, 'assets/art/goods/goods.png')
    atlas([os.path.join(CUT, f'parts-{i:02d}.png') for i in range(9)], 3, 128, 'assets/art/parts/parts.png')
    ach = [os.path.join(CUT, f'ach-a-{i:02d}.png') for i in range(9)] + [os.path.join(CUT, f'ach-b-{i:02d}.png') for i in range(8)]
    atlas(ach, 6, 128, f'{UI}/achievements.png')

def make_pips():
    for name, on in (('pip-on', True), ('pip-off', False)):
        big = Image.new('RGBA', (18 * SS, 18 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(big)
        d.ellipse([2.5 * SS, 2.5 * SS, 15.5 * SS, 15.5 * SS], fill=(RED + (255,)) if on else (246, 239, 222, 200), outline=INK + (235,), width=int(1.6 * SS))
        if on: d.ellipse([6 * SS, 5.5 * SS, 9 * SS, 8.5 * SS], fill=(214, 120, 96, 220))
        save(down(big), f'{UI}/{name}.png')
    # a card whose border is red ink: the chosen preset / the selected ledger detail
    save(box(96, 96, (240, 222, 178, 150), RED + (240,), 2.0, 13, overshoot=3), f'{UI}/card-on.png')
    save(box(96, 96, (236, 220, 184, 110), INK + (240,), 1.6, 14, overshoot=3), f'{UI}/card-hover.png')

def make_locked_atlas():
    im = Image.open(f'{UI}/achievements.png').convert('RGBA')
    a = np.asarray(im).astype(np.float32)
    lum = (0.3 * a[..., 0] + 0.59 * a[..., 1] + 0.11 * a[..., 2])[..., None]
    grey = lum * 0.55 + np.array(PAPER, np.float32) * 0.45
    out = np.concatenate([grey, a[..., 3:] * 0.7], -1)
    save(Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA'), f'{UI}/achievements-locked.png')

make_paper(); make_rim(); make_boxes(); make_tabs(); make_checks(); make_slider(); make_wash(); make_pips()
make_codex_pieces(); make_atlases(); make_locked_atlas()
