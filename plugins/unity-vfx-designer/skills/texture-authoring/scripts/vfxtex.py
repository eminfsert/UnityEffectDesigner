#!/usr/bin/env python3
"""vfxtex: procedural textures for stylized VFX (particle masks, noise, flipbooks).

Output convention: white RGB with the shape in alpha ("mask" textures), so color comes from
the material tint and particle color. Noise is grayscale in RGB and alpha. Requires numpy and
Pillow (pip install numpy pillow). SVG rasterization additionally needs cairosvg.

Examples
  vfxtex.py glow   out.png --size 128 --core 0.25 --falloff 2
  vfxtex.py ring   out.png --radius 0.7 --width 0.12 --breaks 5 --seed 3
  vfxtex.py star   out.png --points 4 --inner 0.08 --glow 0.5
  vfxtex.py streak out.png --size 256 --aspect 4
  vfxtex.py slash  out.png --arc 150 --width 0.18
  vfxtex.py noise  out.png --size 256 --octaves 5 --cells 4 --seed 7
  vfxtex.py smoke  out.png --frames 16 --grid 4 --steps 3 --seed 2
  vfxtex.py svg    out.png --svg shape.svg --size 256
  vfxtex.py preview out.png a.png b.png ...        (tinted contact sheet to look at)

Every command prints the written path; open it and look before handing it over.
"""
import argparse
import math
import sys
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.exit("vfxtex needs numpy and Pillow: pip install numpy pillow")


# ----------------------------------------------------------------- helpers

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def grid(size, aspect=1.0):
    """Centered coordinates in [-1, 1] (x stretched by aspect for non-square outputs)."""
    w = int(round(size * aspect))
    x = (np.arange(w) + 0.5) / w * 2 - 1
    y = (np.arange(size) + 0.5) / size * 2 - 1
    return np.meshgrid(x, y)


def posterize(a, steps):
    if steps < 2:
        return a
    return np.floor(np.clip(a, 0, 1) * (steps - 0.001)) / (steps - 1)


def save_mask(alpha, path):
    alpha = np.clip(alpha, 0, 1)
    rgba = np.zeros(alpha.shape + (4,), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = np.round(alpha * 255).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(path)
    print(path)


def save_gray(value, path):
    g = np.round(np.clip(value, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(np.dstack([g, g, g, g]), "RGBA").save(path)
    print(path)


def value_noise(size, cells, octaves, seed, persistence=0.5):
    """Tileable fractal value noise in [0, 1]."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size))
    amp, weight = 1.0, 0.0
    for o in range(octaves):
        n = cells * 2 ** o
        lattice = rng.random((n, n))
        coords = np.arange(size) / size * n
        i0 = np.floor(coords).astype(int) % n
        i1 = (i0 + 1) % n
        f = coords - np.floor(coords)
        f = f * f * (3 - 2 * f)
        fx, fy = np.meshgrid(f, f)
        a, b = lattice[np.ix_(i0, i0)], lattice[np.ix_(i0, i1)]
        c, d = lattice[np.ix_(i1, i0)], lattice[np.ix_(i1, i1)]
        total += ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) * amp
        weight += amp
        amp *= persistence
    total /= weight
    return (total - total.min()) / (total.max() - total.min() + 1e-9)


# ----------------------------------------------------------------- shapes

def glow(a):
    x, y = grid(a.size)
    r = np.sqrt(x * x + y * y)
    halo = (1 - smoothstep(0.0, 1.0, r)) ** a.falloff
    core = 1 - smoothstep(0.0, max(a.core, 1e-3), r)
    return posterize(np.maximum(halo * a.halo, core), a.steps)


def ring(a):
    x, y = grid(a.size)
    r = np.sqrt(x * x + y * y)
    half = a.width / 2
    band = (1 - smoothstep(half - a.softness, half + a.softness, np.abs(r - a.radius)))
    if a.breaks > 0:
        rng = np.random.default_rng(a.seed)
        angle = (np.arctan2(y, x) / (2 * math.pi) + 0.5) * a.breaks
        offsets = rng.random(a.breaks) * 0.3
        seg = np.floor(angle).astype(int) % a.breaks
        frac = angle - np.floor(angle)
        gap = a.gap
        keep = smoothstep(offsets[seg], offsets[seg] + 0.03, frac) * (1 - smoothstep(1 - gap - 0.03, 1 - gap, frac))
        band *= keep
    return posterize(band, a.steps)


def star(a):
    x, y = grid(a.size)
    r = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    k = a.points
    # Distance to the nearest ray, tapering toward the tip.
    phase = np.abs(((ang * k / (2 * math.pi)) % 1.0) - 0.5) * 2  # 1 on a ray, 0 between rays
    ray_width = (a.inner + 0.002) * (1 - np.clip(r, 0, 1)) ** 1.5
    across = (1 - phase) * r * math.pi / k * 2  # approx. perpendicular distance
    rays = (1 - smoothstep(0, ray_width + 1e-4, across)) * (1 - smoothstep(0.6, 1.0, r))
    core = 1 - smoothstep(0.0, 0.2, r)
    halo = (1 - smoothstep(0.0, 0.6, r)) ** 2 * a.glow
    return posterize(np.clip(np.maximum.reduce([rays, core, halo]), 0, 1), a.steps)


def streak(a):
    x, y = grid(a.size, a.aspect)
    along = 1 - smoothstep(0.3, 1.0, np.abs(x))
    across = 1 - smoothstep(0.0, 0.9 * (1 - np.abs(x) ** 2) + 0.05, np.abs(y))
    return posterize(along * across, a.steps)


def slash(a):
    x, y = grid(a.size)
    r = np.sqrt(x * x + (y + 0.3) ** 2)
    ang = np.degrees(np.arctan2(y + 0.3, x))  # arc opens downward from the center-top
    t = np.clip((ang - (90 - a.arc / 2)) / a.arc, 0, 1)  # 0..1 along the arc
    inside = (ang > 90 - a.arc / 2) & (ang < 90 + a.arc / 2)
    width = a.width * np.sin(t * math.pi) ** 0.7  # thick middle, sharp tips
    band = (1 - smoothstep(0, width + 1e-4, np.abs(r - 0.75))) * inside
    return posterize(band, a.steps)


def noise(a):
    return value_noise(a.size, a.cells, a.octaves, a.seed)


def smoke(a):
    """Stylized smoke-puff flipbook: a lumpy disc that grows and erodes away, posterized."""
    frames = []
    size = a.size
    shape_noise = value_noise(size, 3, 4, a.seed)
    detail = value_noise(size, 5, 3, a.seed + 1)
    x, y = grid(size)
    for i in range(a.frames):
        t = i / max(1, a.frames - 1)
        # Grow fast then slow (ease-out), like a puff expanding.
        grow = 0.35 + 0.6 * (1 - (1 - t) ** 2)
        r = np.sqrt(x * x + y * y) / grow
        # Lumpy silhouette; drifting detail so frames differ.
        drift = np.roll(detail, int(t * size * 0.25), axis=0)
        density = (1 - smoothstep(0.35, 1.0, r + (shape_noise - 0.5) * 0.6)) * (0.55 + 0.45 * drift)
        # Erodes from the thin parts inward over the second half of the life.
        erosion = smoothstep(0.25, 1.0, t) * 0.95
        alpha = smoothstep(erosion, erosion + 0.12, density)
        frames.append(posterize(alpha, a.steps))
    g = a.grid
    atlas = np.zeros((size * g, size * g))
    for i, f in enumerate(frames[: g * g]):
        row, col = divmod(i, g)
        atlas[row * size:(row + 1) * size, col * size:(col + 1) * size] = f
    return atlas


def svg(a):
    try:
        import cairosvg
    except ImportError:
        sys.exit("svg needs cairosvg (pip install cairosvg); or rasterize the SVG another way.")
    import io
    png = cairosvg.svg2png(url=str(a.svg), output_width=a.size, output_height=a.size)
    im = Image.open(io.BytesIO(png)).convert("RGBA")
    # Keep the drawing's alpha as the mask; luminance multiplies in for soft interiors.
    arr = np.asarray(im).astype(float) / 255
    lum = arr[..., :3].max(axis=2)
    return arr[..., 3] * lum


def preview(a):
    tint = np.array([255, 180, 60, 255], float)
    tiles = []
    for p in a.inputs:
        im = Image.open(p).convert("RGBA").resize((160, 160))
        arr = np.asarray(im).astype(float) / 255
        alpha = arr[..., 3:4]
        bg = np.array([22, 22, 28], float) / 255
        rgb = bg * (1 - alpha) + (tint[:3] / 255) * arr[..., :3] * alpha
        tiles.append(np.round(rgb * 255).astype(np.uint8))
    sheet = np.concatenate(tiles, axis=1) if tiles else np.zeros((160, 160, 3), np.uint8)
    Image.fromarray(sheet, "RGB").save(a.out)
    print(a.out)


# ----------------------------------------------------------------- cli

def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    def common(sp, size=256):
        sp.add_argument("out", type=Path)
        sp.add_argument("--size", type=int, default=size)
        sp.add_argument("--steps", type=int, default=0, help="posterize into N flat bands (2-4 for cel looks)")
        sp.add_argument("--seed", type=int, default=1)

    sp = sub.add_parser("glow"); common(sp, 128)
    sp.add_argument("--core", type=float, default=0.25); sp.add_argument("--falloff", type=float, default=2.0)
    sp.add_argument("--halo", type=float, default=0.75)
    sp = sub.add_parser("ring"); common(sp)
    sp.add_argument("--radius", type=float, default=0.7); sp.add_argument("--width", type=float, default=0.12)
    sp.add_argument("--softness", type=float, default=0.02); sp.add_argument("--breaks", type=int, default=0)
    sp.add_argument("--gap", type=float, default=0.15)
    sp = sub.add_parser("star"); common(sp)
    sp.add_argument("--points", type=int, default=4); sp.add_argument("--inner", type=float, default=0.08)
    sp.add_argument("--glow", type=float, default=0.5)
    sp = sub.add_parser("streak"); common(sp, 128)
    sp.add_argument("--aspect", type=float, default=4.0)
    sp = sub.add_parser("slash"); common(sp)
    sp.add_argument("--arc", type=float, default=150.0); sp.add_argument("--width", type=float, default=0.18)
    sp = sub.add_parser("noise"); common(sp)
    sp.add_argument("--cells", type=int, default=4); sp.add_argument("--octaves", type=int, default=5)
    sp = sub.add_parser("smoke"); common(sp, 128)
    sp.add_argument("--frames", type=int, default=16); sp.add_argument("--grid", type=int, default=4)
    sp = sub.add_parser("svg"); common(sp)
    sp.add_argument("--svg", type=Path, required=True)
    sp = sub.add_parser("preview")
    sp.add_argument("out", type=Path); sp.add_argument("inputs", nargs="+", type=Path)

    a = p.parse_args(argv)
    if a.cmd == "preview":
        return preview(a)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    shape = {"glow": glow, "ring": ring, "star": star, "streak": streak, "slash": slash,
             "noise": noise, "smoke": smoke, "svg": svg}[a.cmd](a)
    (save_gray if a.cmd == "noise" else save_mask)(shape, a.out)


if __name__ == "__main__":
    main()
