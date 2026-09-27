# uv run -q --with pillow --with numpy python tools/art/noise.py assets/art/paper/noise.png [size=256] [seed=5]
# A tileable RGBA noise texture for the world shaders' decoration (cheaper than value noise in ALU):
# four independent smooth periodic noises, one per channel, at 4 / 8 / 16 / 32 cycles per tile, each in [0, 255].
import sys
import numpy as np
from PIL import Image
out = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 256
rng = np.random.default_rng(int(sys.argv[3]) if len(sys.argv) > 3 else 5)
def band(f0):
    fy = np.fft.fftfreq(N)[:, None] * N
    fx = np.fft.rfftfreq(N)[None, :] * N
    f = np.sqrt(fx * fx + fy * fy)
    amp = np.exp(-((f - f0) / (f0 * 0.6)) ** 2) * (f > 0)
    spec = amp * np.exp(1j * rng.uniform(0, 2 * np.pi, amp.shape))
    n = np.fft.irfft2(spec, s=(N, N))
    n = (n - n.min()) / (n.max() - n.min())
    # equalise so each channel spans the range evenly (thresholds in the shader behave predictably)
    ranks = n.flatten().argsort().argsort().reshape(n.shape) / (N * N - 1)
    return (0.5 * n + 0.5 * ranks)
chans = [band(f) for f in (4, 8, 16, 32)]
img = np.stack([(c * 255).clip(0, 255).astype(np.uint8) for c in chans], -1)
img[..., 3] = np.maximum(img[..., 3], 1)   # alpha never 0: the importer's alpha-border fix must not touch it
Image.fromarray(img, 'RGBA').save(out, optimize=True)
a = img.astype(int)
print(out, N, 'seam', np.abs(a[:, 0] - a[:, -1]).mean().round(2), 'inner', np.abs(np.diff(a, axis=1)).mean().round(2))
