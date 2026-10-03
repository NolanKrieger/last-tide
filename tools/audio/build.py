#!/usr/bin/env python3
"""
Last Tide audio: every sound is synthesized here (no third-party samples, no licences to track).
Run:  uv run --with numpy python3 tools/audio/build.py        (writes assets/audio/*.wav + manifest.json)
Loops are seamless (crossfaded tails); one-shots are short. 16-bit PCM mono.
"""
import json, math, os, struct, wave
import numpy as np

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "audio")
SR_AMB = 22050
SR_SFX = 44100
rng = np.random.default_rng(20260923)

LOOPS = {"wind_low", "wind_high", "waves", "creak", "rain", "harbour", "pumps", "dig", "water_rush", "siren_song"}

def finish(data, sr):
    """One-shots start and end in silence: no DC offset, a 3 ms fade in and a 40 ms fade out. Several cues (kraken,
    ghost, weed, eruption, gust) used to stop mid-waveform at 3-9% of full scale, a click at the end of every play."""
    data = data - np.mean(data)
    fi = int(0.003 * sr); fo = min(int(0.04 * sr), len(data) // 4)
    data[:fi] *= np.linspace(0, 1, fi)
    data[-fo:] *= np.linspace(1, 0, fo)
    return data

def write(name, data, sr):
    data = np.array(data, dtype=np.float64)
    if name not in LOOPS:
        data = finish(data, sr)
    peak = np.max(np.abs(data)) or 1.0
    data = data / peak * 0.9
    pcm = (data * 32767).astype("<i2")
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    return {"file": name + ".wav", "rate": sr, "seconds": round(len(data) / sr, 3)}

def seamless(x, fade):
    """Crossfade the last `fade` samples into the first so the loop point is silent."""
    n = len(x); f = min(fade, n // 4)
    ramp = np.linspace(0, 1, f)
    head = x[:f] * ramp + x[-f:] * (1 - ramp)
    return np.concatenate([head, x[f:n - f]])

def noise(n, kind="white"):
    w = rng.standard_normal(n)
    if kind == "white":
        return w
    # pink / brown by leaky integration
    out = np.zeros(n); acc = 0.0
    a = 0.02 if kind == "pink" else 0.005
    for i in range(n):
        acc += a * (w[i] - acc)
        out[i] = acc
    return out / (np.max(np.abs(out)) or 1)

def lowpass(x, sr, cutoff):
    dt = 1.0 / sr; rc = 1.0 / (2 * math.pi * cutoff); a = dt / (rc + dt)
    y = np.zeros_like(x); acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc); y[i] = acc
    return y

def highpass(x, sr, cutoff):
    return x - lowpass(x, sr, cutoff)

def bandpass(x, sr, lo, hi):
    return highpass(lowpass(x, sr, hi), sr, lo)

def env(n, attack, decay, sr, curve=3.0):
    t = np.arange(n) / sr
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = np.exp(-curve * np.clip((t - attack) / max(decay, 1e-4), 0, None))
    return a * d

def fit(x, n):
    """Pads or trims to n samples."""
    return x[:n] if len(x) >= n else np.concatenate([x, np.zeros(n - len(x))])

def lfo(n, sr, hz, phase=0.0):
    return 0.5 + 0.5 * np.sin(2 * math.pi * hz * np.arange(n) / sr + phase)

def tone(n, sr, hz, vib=0.0, vib_hz=5.0):
    t = np.arange(n) / sr
    return np.sin(2 * math.pi * hz * t + vib * np.sin(2 * math.pi * vib_hz * t))

manifest = {}

# ---------- ambience loops ----------
def wind(strength):
    sr = SR_AMB; n = sr * 12
    base = bandpass(noise(n, "pink"), sr, 80, 900 + 1400 * strength)
    gust = 0.55 + 0.45 * (0.6 * lfo(n, sr, 0.13) + 0.4 * lfo(n, sr, 0.31, 1.7))
    whistle = bandpass(noise(n), sr, 1500 + 800 * strength, 2600 + 1200 * strength) * (0.15 + 0.5 * strength) * lfo(n, sr, 0.21, 0.4) ** 2
    return seamless((base + whistle) * gust, sr // 2)

manifest["wind_low"] = write("wind_low", wind(0.2), SR_AMB)
manifest["wind_high"] = write("wind_high", wind(1.0), SR_AMB)

def waves():
    sr = SR_AMB; n = sr * 14
    out = np.zeros(n)
    t = 0.0
    while t < 14:
        start = int(t * sr); length = int(sr * rng.uniform(2.5, 4.5))
        seg = bandpass(noise(length, "pink"), sr, 120, 1800) * env(length, 0.9, 2.2, sr, 2.0)
        end = min(n, start + length)
        out[start:end] += seg[: end - start]
        t += rng.uniform(1.6, 3.2)
    return seamless(out + 0.15 * bandpass(noise(n, "brown"), sr, 40, 300), sr // 2)

manifest["waves"] = write("waves", waves(), SR_AMB)

def creak():
    sr = SR_AMB; n = sr * 9
    out = np.zeros(n)
    for _ in range(14):
        start = int(rng.uniform(0, 8.2) * sr); length = int(sr * rng.uniform(0.25, 0.7))
        f0 = rng.uniform(90, 220)
        t = np.arange(length) / sr
        sweep = f0 * (1 + 0.6 * t / (length / sr))
        ph = 2 * math.pi * np.cumsum(sweep) / sr
        grain = (np.sin(ph) + 0.5 * np.sin(2.01 * ph) + 0.25 * np.sign(np.sin(3 * ph))) * env(length, 0.05, 0.5, sr, 2.5)
        grain *= 0.6 + 0.4 * np.sin(2 * math.pi * 23 * t)
        out[start:start + length] += grain[: n - start]
    return seamless(out, sr // 4)

manifest["creak"] = write("creak", creak(), SR_AMB)

def rain():
    sr = SR_AMB; n = sr * 10
    body = highpass(noise(n), sr, 2500) * 0.6
    drops = np.zeros(n)
    for _ in range(900):
        i = int(rng.uniform(0, n - 400)); length = 300
        drops[i:i + length] += highpass(noise(length), sr, 3500) * env(length, 0.001, 0.02, sr) * rng.uniform(0.3, 1.0)
    return seamless(body + drops, sr // 4)

manifest["rain"] = write("rain", rain(), SR_AMB)

def harbour():
    sr = SR_AMB; n = sr * 16
    murmur = bandpass(noise(n, "pink"), sr, 200, 1200) * (0.5 + 0.5 * lfo(n, sr, 0.17))
    knocks = np.zeros(n)
    for _ in range(40):
        i = int(rng.uniform(0, n - 3000)); length = 2500
        knocks[i:i + length] += lowpass(noise(length), sr, 500) * env(length, 0.002, 0.08, sr) * rng.uniform(0.2, 0.7)
    return seamless(murmur * 0.7 + knocks, sr // 2)

manifest["harbour"] = write("harbour", harbour(), SR_AMB)

def gulls():
    sr = SR_AMB; n = sr * 18
    out = np.zeros(n)
    for _ in range(9):
        start = int(rng.uniform(0, 16.5) * sr)
        for k in range(rng.integers(1, 4)):
            s = start + int(k * rng.uniform(0.25, 0.45) * sr); length = int(sr * rng.uniform(0.25, 0.5))
            t = np.arange(length) / sr
            f = rng.uniform(1100, 1600) * (1 + 0.35 * np.sin(math.pi * t / (length / sr)))
            ph = 2 * math.pi * np.cumsum(f) / sr
            cry = (np.sin(ph) + 0.4 * np.sin(2 * ph) + 0.2 * np.sin(3 * ph)) * env(length, 0.03, 0.3, sr, 2.0)
            end = min(n, s + length)
            out[s:end] += cry[: end - s] * rng.uniform(0.3, 0.8)
    return seamless(out, sr // 4)

gulls()  # cut 2026-09-29 (Nolan): not written; still drawn so the shared rng, and every sound after it, stays identical

def pumps():
    sr = SR_AMB; n = int(sr * 2.4)
    out = np.zeros(n)
    for k in range(2):
        s = int(k * 1.2 * sr)
        length = int(0.5 * sr)
        out[s:s + length] += lowpass(noise(length), sr, 400) * env(length, 0.01, 0.25, sr)          # the stroke
        s2 = s + int(0.35 * sr)
        out[s2:s2 + length] += bandpass(noise(length), sr, 600, 3000) * env(length, 0.02, 0.35, sr) * 0.6   # the gush
    return seamless(out, sr // 8)

manifest["pumps"] = write("pumps", pumps(), SR_AMB)

def dig():
    sr = SR_AMB; n = int(sr * 1.6)
    out = np.zeros(n)
    for k in range(2):
        s = int(k * 0.8 * sr); length = int(0.45 * sr)
        out[s:s + length] += (bandpass(noise(length), sr, 300, 2500) * env(length, 0.005, 0.12, sr)
                              + 0.5 * lowpass(noise(length), sr, 250) * env(length, 0.05, 0.3, sr))
    return seamless(out, sr // 8)

manifest["dig"] = write("dig", dig(), SR_AMB)

def water_rush():
    sr = SR_AMB; n = sr * 6
    return seamless(bandpass(noise(n), sr, 300, 2500) * (0.6 + 0.4 * lfo(n, sr, 0.9)), sr // 4)

manifest["water_rush"] = write("water_rush", water_rush(), SR_AMB)

def song():
    sr = SR_AMB; n = sr * 12
    t = np.arange(n) / sr
    notes = [440, 523.25, 587.33, 659.25, 587.33, 523.25]
    out = np.zeros(n)
    seg = n // len(notes)
    for i, f in enumerate(notes):
        s = i * seg; length = seg + sr // 2
        length = min(length, n - s)
        tt = np.arange(length) / sr
        v = np.sin(2 * math.pi * f * tt + 0.012 * f * np.sin(2 * math.pi * 5.5 * tt) / f)
        v = np.sin(2 * math.pi * f * tt + 0.9 * np.sin(2 * math.pi * 5.5 * tt)) + 0.4 * np.sin(2 * math.pi * 2 * f * tt) + 0.2 * np.sin(2 * math.pi * 3 * f * tt)
        out[s:s + length] += v * env(length, 0.6, 1.4, sr, 1.5)
    return seamless(out, sr)

manifest["siren_song"] = write("siren_song", song(), SR_AMB)

# ---------- one-shots ----------
def cannon():
    sr = SR_SFX; n = int(sr * 1.6)
    t = np.arange(n) / sr
    thump = np.sin(2 * math.pi * (55 * np.exp(-t * 3) + 30) * t) * env(n, 0.002, 0.35, sr, 4)
    blast = lowpass(noise(n), sr, 1800) * env(n, 0.001, 0.25, sr, 5)
    tail = lowpass(noise(n), sr, 500) * env(n, 0.05, 0.9, sr, 3) * 0.5
    return thump * 1.2 + blast + tail

manifest["cannon"] = write("cannon", cannon(), SR_SFX)

def splinter():
    sr = SR_SFX; n = int(sr * 0.7)
    out = bandpass(noise(n), sr, 800, 6000) * env(n, 0.001, 0.12, sr, 4)
    for _ in range(14):
        i = int(rng.uniform(0, n * 0.4)); length = 900
        out[i:i + length] += highpass(noise(length), sr, 2000) * env(length, 0.001, 0.01, sr) * rng.uniform(0.4, 1.0)
    thud = np.sin(2 * math.pi * 90 * np.arange(n) / sr) * env(n, 0.002, 0.12, sr, 4)
    return out + 0.8 * thud

manifest["hit"] = write("hit", splinter(), SR_SFX)

def splash():
    sr = SR_SFX; n = int(sr * 0.9)
    return bandpass(noise(n), sr, 400, 4000) * env(n, 0.01, 0.35, sr, 3) + 0.3 * lowpass(noise(n), sr, 300) * env(n, 0.005, 0.15, sr, 4)

manifest["splash"] = write("splash", splash(), SR_SFX)

def ram():
    sr = SR_SFX; n = int(sr * 1.4)
    t = np.arange(n) / sr
    crunch = lowpass(noise(n), sr, 900) * env(n, 0.002, 0.4, sr, 3)
    groan = np.sin(2 * math.pi * (70 - 30 * t) * t) * env(n, 0.01, 0.8, sr, 2.5)
    return crunch + 0.8 * groan + 0.6 * fit(splinter(), n)

manifest["ram"] = write("ram", ram(), SR_SFX)

def bell(f0=880, seconds=2.2, partials=((1, 1.0), (2.0, 0.6), (2.76, 0.4), (4.07, 0.25), (5.4, 0.15))):
    sr = SR_SFX; n = int(sr * seconds)
    t = np.arange(n) / sr
    out = np.zeros(n)
    for ratio, amp in partials:
        out += amp * np.sin(2 * math.pi * f0 * ratio * t) * np.exp(-t * (1.6 + ratio))
    return out * env(n, 0.001, seconds, sr, 1.0)

manifest["bell"] = write("bell", bell(), SR_SFX)
manifest["bell_low"] = write("bell_low", bell(440, 3.0), SR_SFX)

def coins():
    sr = SR_SFX; n = int(sr * 1.2)
    out = np.zeros(n)
    for k in range(7):
        s = int(k * 0.11 * sr + rng.uniform(0, 0.03) * sr); length = int(0.35 * sr)
        f = rng.uniform(2400, 4200)
        t = np.arange(length) / sr
        out[s:s + length] += (np.sin(2 * math.pi * f * t) + 0.5 * np.sin(2 * math.pi * f * 2.3 * t)) * np.exp(-t * 14) * rng.uniform(0.5, 1.0)
    return out

manifest["coins"] = write("coins", coins(), SR_SFX)

def sail(up):
    sr = SR_SFX; n = int(sr * 0.9)
    flap = np.zeros(n)
    for k in range(4 if up else 3):
        s = int(k * 0.16 * sr); length = int(0.2 * sr)
        flap[s:s + length] += lowpass(noise(length), sr, 1200) * env(length, 0.003, 0.08, sr, 4) * (1.0 - 0.15 * k)
    rope = bandpass(noise(n), sr, 1500, 5000) * env(n, 0.05, 0.5, sr, 2.5) * 0.35
    return flap + rope

manifest["sail_up"] = write("sail_up", sail(True), SR_SFX)
manifest["sail_down"] = write("sail_down", sail(False), SR_SFX)

def thunder():
    sr = SR_SFX; n = int(sr * 4.0)
    crack = highpass(noise(n), sr, 800) * env(n, 0.002, 0.15, sr, 5)
    roll = lowpass(noise(n), sr, 220) * env(n, 0.2, 3.0, sr, 1.8) * (0.7 + 0.3 * lfo(n, sr, 2.3))
    return crack * 0.6 + roll

manifest["thunder"] = write("thunder", thunder(), SR_SFX)

def drums():
    sr = SR_SFX; n = int(sr * 2.4)
    out = np.zeros(n)
    pattern = [0, 0.25, 0.5, 0.625, 0.75, 1.0, 1.25, 1.5, 1.625, 1.75, 2.0]
    for i, b in enumerate(pattern):
        s = int(b * sr); length = int(0.25 * sr)
        t = np.arange(length) / sr
        hit = np.sin(2 * math.pi * (140 * np.exp(-t * 12) + 60) * t) * env(length, 0.002, 0.18, sr, 4) + 0.5 * lowpass(noise(length), sr, 900) * env(length, 0.001, 0.05, sr, 5)
        out[s:s + length] += hit * (1.0 if i % 4 == 0 else 0.7)
    return out

manifest["drums"] = write("drums", drums(), SR_SFX)

def sink():
    sr = SR_SFX; n = int(sr * 3.5)
    t = np.arange(n) / sr
    bubbles = np.zeros(n)
    for _ in range(60):
        i = int(rng.uniform(0, n - 3000)); length = 2500
        f = rng.uniform(300, 900)
        tt = np.arange(length) / sr
        bubbles[i:i + length] += np.sin(2 * math.pi * f * (1 + 0.5 * tt) * tt) * env(length, 0.003, 0.04, sr, 4) * rng.uniform(0.2, 0.6)
    rush = bandpass(noise(n), sr, 200, 2000) * env(n, 0.3, 2.5, sr, 1.5)
    groan = np.sin(2 * math.pi * (60 - 20 * t / 3.5) * t) * env(n, 0.5, 2.5, sr, 1.5) * 0.7
    return rush + bubbles + groan

manifest["sink"] = write("sink", sink(), SR_SFX)

def monster(kind):
    sr = SR_SFX
    if kind == "serpent":
        n = int(sr * 1.4); t = np.arange(n) / sr
        hiss = highpass(noise(n), sr, 1800) * env(n, 0.05, 0.8, sr, 2.0)
        growl = np.sin(2 * math.pi * (110 + 40 * np.sin(2 * math.pi * 6 * t)) * t) * env(n, 0.1, 0.9, sr, 2) * 0.6
        return hiss * 0.5 + growl
    if kind == "kraken":
        n = int(sr * 3.0); t = np.arange(n) / sr
        deep = np.sin(2 * math.pi * (38 + 6 * np.sin(2 * math.pi * 0.7 * t)) * t) + 0.5 * np.sin(2 * math.pi * 57 * t)
        return deep * env(n, 0.4, 2.2, sr, 1.5) + 0.3 * lowpass(noise(n), sr, 200) * env(n, 0.3, 2.5, sr, 1.5)
    if kind == "ghost":
        n = int(sr * 3.5); t = np.arange(n) / sr
        return (np.sin(2 * math.pi * 220 * t + 3 * np.sin(2 * math.pi * 0.4 * t)) + 0.6 * np.sin(2 * math.pi * 330.5 * t)) * env(n, 0.8, 2.4, sr, 1.5) * (0.6 + 0.4 * lfo(n, sr, 3.1))
    if kind == "crocodile":
        n = int(sr * 1.2); t = np.arange(n) / sr
        growl = np.sign(np.sin(2 * math.pi * (70 + 20 * np.sin(2 * math.pi * 9 * t)) * t)) * env(n, 0.05, 0.7, sr, 2.5)
        return lowpass(growl, sr, 600) + 0.5 * fit(splash(), n)
    if kind == "weed":
        n = int(sr * 2.0)
        return bandpass(noise(n), sr, 150, 1200) * env(n, 0.3, 1.5, sr, 1.5) * (0.5 + 0.5 * lfo(n, sr, 4.2))
    if kind == "eruption":
        n = int(sr * 3.0)
        return lowpass(noise(n), sr, 120) * env(n, 0.5, 2.5, sr, 1.5) + 0.4 * lowpass(noise(n), sr, 800) * env(n, 0.02, 0.5, sr, 3)
    raise ValueError(kind)

for kind in ["serpent", "kraken", "ghost", "crocodile", "weed", "eruption"]:
    manifest["monster_" + kind] = write("monster_" + kind, monster(kind), SR_SFX)

def quill():
    sr = SR_SFX; n = int(sr * 0.18)
    return bandpass(noise(n), sr, 2000, 7000) * env(n, 0.002, 0.08, sr, 4)

manifest["quill"] = write("quill", quill(), SR_SFX)

def click():
    sr = SR_SFX; n = int(sr * 0.08)
    return lowpass(noise(n), sr, 3000) * env(n, 0.001, 0.03, sr, 5)

manifest["click"] = write("click", click(), SR_SFX)

def gust():
    sr = SR_SFX; n = int(sr * 2.5)
    return bandpass(noise(n), sr, 300, 2500) * env(n, 0.6, 1.5, sr, 1.5)

manifest["gust"] = write("gust", gust(), SR_SFX)

def siren_call():
    """The Siren's cue as she rises onto her rock: one sung call gliding up a fifth, with a breath under it.
    Generated last so every sound above keeps its random stream."""
    sr = SR_SFX; n = int(sr * 2.4); t = np.arange(n) / sr
    f = 523.25 * (1 + 0.5 * np.clip((t - 0.35) / 0.9, 0, 1) ** 1.5)          # C5 gliding to G5
    phase = 2 * np.pi * np.cumsum(f) / sr + 0.9 * np.sin(2 * np.pi * 5.2 * t) * np.clip(t / 0.8, 0, 1)
    voice = np.sin(phase) + 0.35 * np.sin(2 * phase) + 0.15 * np.sin(3 * phase)
    breath = bandpass(noise(n), sr, 900, 4000) * 0.12
    return (voice + breath) * env(n, 0.25, 1.6, sr, 1.8)

manifest["monster_siren"] = write("monster_siren", siren_call(), SR_SFX)

with open(os.path.join(OUT, "manifest.json"), "w") as f:
    json.dump(manifest, f, indent=2)
total = sum(os.path.getsize(os.path.join(OUT, v["file"])) for v in manifest.values())
print(f"{len(manifest)} sounds, {total / 1e6:.1f} MB")
