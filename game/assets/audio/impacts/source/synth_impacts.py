"""
Synthesizes the placeholder impact sounds: <material>_<light|heavy>_<n>.wav in ../ (game/assets/audio/impacts).
Plain Python + numpy:  python synth_impacts.py

Each material is a small recipe: metal and glass are banks of inharmonic resonant partials (modal synthesis),
rock and concrete are filtered noise bursts over a short body thump, rubber is a pitch-dropping low thud, plastic
is a couple of hollow mid resonances. "heavy" variants are lower, longer and louder-bodied than "light" ones, and
every variant is jittered from a seed so repeats don't sound identical. Replace any file with a recording of
the same name to upgrade it; ImpactSounds picks them up by name.
"""
import os, wave, zlib
import numpy as np

SR = 22050
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VARIANTS = 4

def env(n, decay, attack=0.002):
    t = np.arange(n) / SR
    a = np.clip(t / attack, 0, 1)
    return a * np.exp(-t / decay)

def noise(n, rng):
    return rng.standard_normal(n)

def onepole_lp(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y

def bandpass(x, lo, hi):
    return onepole_lp(x, hi) - onepole_lp(x, lo)

def partials(n, freqs, decays, amps, rng, detune=0.0):
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f, d, a in zip(freqs, decays, amps):
        f *= 1 + rng.uniform(-detune, detune)
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / d)
    return out

def thump(n, f0, f1, decay):
    t = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-t / (decay * 0.5))
    phase = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(phase) * env(n, decay)

def metal(heavy, rng):
    # Dull, heavy and subtle: a low, thick plate or beam knocked rather than a bell struck. A low inharmonic
    # fundamental whose upper partials die within tens of milliseconds, all through a low-pass, over a soft
    # thump, with only a muffled hint of the strike.
    n = int(SR * (0.55 if heavy else 0.32))
    base = rng.uniform(95, 140) if heavy else rng.uniform(170, 240)
    ratios = [1.0, 2.1, 3.9, 6.2]
    x = partials(n, [base * r for r in ratios], [0.22, 0.09, 0.04, 0.02] if heavy else [0.13, 0.06, 0.03, 0.015],
                 [1.0, 0.35, 0.12, 0.05], rng, detune=0.04)
    body = thump(n, 150 if heavy else 240, 60 if heavy else 110, 0.07 if heavy else 0.04)
    strike = onepole_lp(noise(n, rng), 1400) * env(n, 0.008) * 0.25
    return onepole_lp(x * 0.9 + body * 0.8 + strike, 1100 if heavy else 1600)

def glass(heavy, rng):
    n = int(SR * (0.8 if heavy else 0.5))
    base = rng.uniform(1800, 2400) if heavy else rng.uniform(2600, 3400)
    x = partials(n, [base, base * 1.58, base * 2.41, base * 3.7], [0.35, 0.25, 0.15, 0.08], [1.0, 0.7, 0.5, 0.3], rng, 0.02)
    tick = bandpass(noise(n, rng), 3000, 9000) * env(n, 0.006)
    return x * 0.8 + tick

def rock(heavy, rng):
    n = int(SR * (0.35 if heavy else 0.18))
    grit = bandpass(noise(n, rng), 600 if heavy else 1200, 4000 if heavy else 7000) * env(n, 0.05 if heavy else 0.025)
    # a few crunchy sub-impacts
    for _ in range(rng.integers(2, 5)):
        off = int(rng.uniform(0.005, 0.05) * SR)
        m = n - off
        grit[off:] += bandpass(noise(m, rng), 900, 5000) * env(m, 0.012) * rng.uniform(0.3, 0.7)
    body = thump(n, 220 if heavy else 400, 110 if heavy else 250, 0.06 if heavy else 0.03)
    return grit * 1.2 + body * (0.9 if heavy else 0.5)

def concrete(heavy, rng):
    n = int(SR * (0.4 if heavy else 0.2))
    knock = onepole_lp(noise(n, rng), 900 if heavy else 1600) * env(n, 0.06 if heavy else 0.03) * 2.5
    body = thump(n, 160 if heavy else 260, 70 if heavy else 150, 0.09 if heavy else 0.04)
    return knock + body * (1.2 if heavy else 0.7)

def rubber(heavy, rng):
    n = int(SR * (0.3 if heavy else 0.16))
    body = thump(n, rng.uniform(150, 190) if heavy else rng.uniform(230, 280), 55 if heavy else 110, 0.08 if heavy else 0.04)
    slap = onepole_lp(noise(n, rng), 1200) * env(n, 0.01) * 0.6
    return body * 1.3 + slap

def plastic(heavy, rng):
    n = int(SR * (0.3 if heavy else 0.18))
    base = rng.uniform(420, 560) if heavy else rng.uniform(700, 900)
    x = partials(n, [base, base * 1.9, base * 3.1], [0.07, 0.05, 0.03] if heavy else [0.05, 0.035, 0.02], [1.0, 0.5, 0.3], rng, 0.04)
    click = bandpass(noise(n, rng), 1500, 6000) * env(n, 0.006) * 0.7
    return x + click + (thump(n, 200, 120, 0.05) * 0.5 if heavy else 0)

RECIPES = {"metal": metal, "glass": glass, "rock": rock, "concrete": concrete, "rubber": rubber, "plastic": plastic}

# Peak level per material; metal sits under the rest so a clattering pile of scrap doesn't dominate the mix.
LEVEL = {"metal": 0.5}

def write(path, x, level=0.9):
    x = x / (np.max(np.abs(x)) + 1e-9) * level
    fade = min(len(x), int(SR * 0.01))
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (x * 32767).astype("<i2").tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data)

if __name__ == "__main__":
    count = 0
    for name, fn in RECIPES.items():
        for heavy in (False, True):
            for v in range(VARIANTS):
                rng = np.random.default_rng(zlib.crc32(f"{name}_{heavy}_{v}".encode()))
                write(os.path.join(OUT, f"{name}_{'heavy' if heavy else 'light'}_{v}.wav"), fn(heavy, rng), LEVEL.get(name, 0.9))
                count += 1
    print("wrote", count, "files to", OUT)
