# uv run -q --with pillow --with numpy python tools/art/paper.py assets/art/paper/paper.png [size=1024] [seed=11]
# A seamless paper DATA texture for the sea and fog shaders (procedural, no AI): every layer is periodic by
# construction (FFT-filtered noise, fibres and spots drawn with wrap-around), so the tile repeats without a seam.
#   R = tone: slow mottling and cloudy formation of hand-made rag paper (128 = neutral)
#   G = fibres: short dark and light rag fibres (128 = none; <128 dark fibre, >128 pale fibre)
#   B = foxing: rust spots (0 = none)
#   A = grain: fine tooth of the sheet (128 = neutral)
import sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 1024
rng = np.random.default_rng(int(sys.argv[3]) if len(sys.argv) > 3 else 11)

def periodic_noise(beta, fmin, fmax):
    """Tileable noise with a 1/f^beta spectrum between fmin and fmax cycles per tile, normalised to [-1, 1]."""
    fy = np.fft.fftfreq(N)[:, None] * N
    fx = np.fft.rfftfreq(N)[None, :] * N
    f = np.sqrt(fx * fx + fy * fy)
    amp = np.where((f >= fmin) & (f <= fmax), 1.0 / np.maximum(f, 1e-6) ** beta, 0.0)
    phase = rng.uniform(0, 2 * np.pi, amp.shape)
    spec = amp * np.exp(1j * phase)
    n = np.fft.irfft2(spec, s=(N, N))
    n -= n.mean()
    return n / (np.abs(n).max() + 1e-9)

# Tone: broad mottling plus the cloudy "formation" of the sheet.
tone = 0.65 * periodic_noise(1.8, 1, 24) + 0.35 * periodic_noise(1.2, 12, 120)
tone /= np.abs(tone).max()
# Grain: fine isotropic tooth.
grain = periodic_noise(0.4, 90, 512)

# Fibres: short, slightly curved strokes, most dark and faint, some pale; drawn at 2x and wrapped.
S = 2
fib = Image.new('L', (N * S, N * S), 128)
d = ImageDraw.Draw(fib)
for i in range(2600):
    x, y = rng.uniform(0, N), rng.uniform(0, N)
    ang = rng.uniform(0, math.pi)
    length = rng.uniform(5, 34) * (1.6 if rng.random() < 0.08 else 1.0)
    bend = rng.uniform(-0.5, 0.5)
    dark = rng.random() < 0.72
    val = int(128 - rng.uniform(18, 60)) if dark else int(128 + rng.uniform(15, 45))
    pts = []
    for k in range(9):
        t = k / 8 - 0.5
        a = ang + bend * t * 2
        pts.append((x + math.cos(a) * t * length, y + math.sin(a) * t * length))
    for ox in (-N, 0, N):
        for oy in (-N, 0, N):
            if min(p[0] + ox for p in pts) > N + 2 or max(p[0] + ox for p in pts) < -2: continue
            if min(p[1] + oy for p in pts) > N + 2 or max(p[1] + oy for p in pts) < -2: continue
            d.line([((p[0] + ox) * S, (p[1] + oy) * S) for p in pts], fill=val, width=1 if rng.random() < 0.7 else 2)
fib = fib.resize((N, N), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.35))
fibres = (np.asarray(fib, np.float32) - 128) / 128.0

# Foxing: a few soft, ragged rust blooms (no hard cores) with clusters of pin-prick specks, wrapped.
fox = np.zeros((N, N), np.float32)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
edge_noise = periodic_noise(1.6, 6, 60)
for i in range(11):
    cx, cy = rng.uniform(0, N), rng.uniform(0, N)
    r = rng.uniform(7, 30)
    dx = np.minimum(np.abs(xx - cx), N - np.abs(xx - cx))
    dy = np.minimum(np.abs(yy - cy), N - np.abs(yy - cy))
    dist = np.sqrt(dx * dx + dy * dy) / r
    ragged = dist + 0.45 * edge_noise
    bloom = np.clip(1.0 - ragged, 0, 1)
    spot = (bloom ** 0.8) * rng.uniform(0.25, 0.55) + np.clip(1 - np.abs(ragged - 0.85) * 6, 0, 1) * 0.25   # tide-line rim
    fox = np.maximum(fox, np.clip(spot, 0, 1))
    for k in range(rng.integers(0, 9)):
        sx, sy = int(cx + rng.normal(0, r * 1.2)) % N, int(cy + rng.normal(0, r * 1.2)) % N
        fox[sy, sx] = max(fox[sy, sx], rng.uniform(0.35, 0.7))
for i in range(120):
    cx, cy = rng.integers(0, N), rng.integers(0, N)
    fox[cy, cx] = max(fox[cy, cx], rng.uniform(0.25, 0.6))
fox = np.asarray(Image.fromarray((fox * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6)), np.float32) / 255

def to8(v, centre=True):
    return np.clip((v * 127 + 128) if centre else v * 255, 0, 255).astype(np.uint8)

rgba = np.stack([to8(tone), to8(fibres), to8(fox, centre=False), np.clip(to8(grain), 8, 247)], axis=-1)   # alpha never 0: the importer's alpha-border fix must not touch it
Image.fromarray(rgba, 'RGBA').save(out, optimize=True)
# Seam check: the wrap-around difference must look like any neighbouring-pixel difference.
a = rgba.astype(np.int32)
inner = np.abs(np.diff(a[:, :, :], axis=1)).mean()
seam = np.abs(a[:, 0] - a[:, -1]).mean()
print(f'{out} {N}x{N}; mean neighbour diff {inner:.2f}, seam diff {seam:.2f}')
