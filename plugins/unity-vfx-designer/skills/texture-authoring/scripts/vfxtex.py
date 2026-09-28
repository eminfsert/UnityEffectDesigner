#!/usr/bin/env python3
"""vfxtex: procedural textures for stylized VFX (particle masks, noise, flipbooks).

Output convention: white RGB with the shape in alpha ("mask" textures), so color comes from
the material tint and particle color. Noise is grayscale in RGB and alpha. Requires numpy and
Pillow (pip install numpy pillow). SVG rasterization additionally needs cairosvg.

Examples
  vfxtex.py glow   out.png --size 128 --core 0.25 --falloff 2
  vfxtex.py ring   out.png --radius 0.7 --width 0.12 --breaks 5 --seed 3
  vfxtex.py star   out.png --points 4 --inner 0.3 --sharp 2 --glow 0.3
  vfxtex.py streak out.png --size 256 --aspect 4
  vfxtex.py slash  out.png --arc 150 --width 0.18
  vfxtex.py noise  out.png --size 256 --octaves 5 --cells 4 --seed 7
  vfxtex.py smoke  out.png --frames 16 --grid 4 --steps 3 --seed 2
  vfxtex.py svg    out.png --svg shape.svg --size 256
  vfxtex.py preview out.png a.png b.png ... --tint 40C8FF   (look at this, not the mask itself)

Every command prints the written path. Masks are white RGB, so opened directly they look like
blank white squares: always look at a preview (alpha row, tinted on dark, tinted on light).
--steps N posterizes into flat cel bands; it renders at 4x and downsamples, so band edges stay
anti-aliased. Keep shapes inside radius ~0.9 so nothing is cut at the quad border (a warning is
printed if a mask touches the border).
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
    return np.maximum(halo * a.halo, core)


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
    return band


def star(a):
    """Polar star: the outline swings between the tips (radius --outer) and the valleys between
    them (--inner, as a fraction of --outer). --sharp > 1 makes concave, needle-like rays;
    1 roughly straight edges; < 1 a puffy, rounded star."""
    x, y = grid(a.size)
    r = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x) + math.radians(a.rotation) + math.pi / 2  # first tip points up
    k = a.points
    phase = np.abs(((ang * k / (2 * math.pi)) % 1.0) - 0.5) * 2  # 1 on a tip, 0 midway between tips
    inner = np.clip(a.inner, 0.02, 1.0)
    outline = a.outer * (inner + (1 - inner) * phase ** a.sharp)
    shape = 1 - smoothstep(outline - a.softness, outline + a.softness, r)
    halo = (1 - smoothstep(0.0, a.outer, r)) ** 2 * a.glow
    return np.clip(np.maximum(shape, halo), 0, 1)


def streak(a):
    x, y = grid(a.size, a.aspect)
    along = 1 - smoothstep(0.3, 1.0, np.abs(x))
    across = 1 - smoothstep(0.0, 0.9 * (1 - np.abs(x) ** 2) + 0.05, np.abs(y))
    return along * across


def slash(a):
    x, y = grid(a.size)
    r = np.sqrt(x * x + (y + 0.3) ** 2)
    ang = np.degrees(np.arctan2(y + 0.3, x))  # arc opens downward from the center-top
    t = np.clip((ang - (90 - a.arc / 2)) / a.arc, 0, 1)  # 0..1 along the arc
    inside = (ang > 90 - a.arc / 2) & (ang < 90 + a.arc / 2)
    width = a.width * np.sin(t * math.pi) ** 0.7  # thick middle, sharp tips
    band = (1 - smoothstep(0, width + 1e-4, np.abs(r - 0.75))) * inside
    return band


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
        frames.append(alpha)
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
    """Three rows per texture: raw alpha (gray), tinted on dark, tinted on light (alpha blended),
    so shape, edges and readability on bright ground can all be judged from one image."""
    tint = np.array([int(a.tint.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4)], float) / 255
    dark = np.array([22, 22, 28], float) / 255
    light = np.array([200, 204, 210], float) / 255
    tile = a.tile
    columns = []
    for p in a.inputs:
        im = Image.open(p).convert("RGBA")
        w, h = im.size
        im = im.resize((tile, max(1, round(tile * h / w))), Image.LANCZOS)
        arr = np.asarray(im).astype(float) / 255
        alpha = arr[..., 3:4]
        color = tint * arr[..., :3]
        rows = [np.repeat(alpha, 3, axis=2),
                dark * (1 - alpha) + color * alpha,
                light * (1 - alpha) + color * alpha]
        gap = np.ones((4, tile, 3)) * 0.5
        columns.append(np.concatenate([rows[0], gap, rows[1], gap, rows[2]], axis=0))
    height = max(c.shape[0] for c in columns)
    columns = [np.pad(c, ((0, height - c.shape[0]), (0, 4), (0, 0)), constant_values=0.5) for c in columns]
    sheet = np.concatenate(columns, axis=1)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.round(np.clip(sheet, 0, 1) * 255).astype(np.uint8), "RGB").save(a.out)
    print(a.out)


def render(fn, a, supersample=4):
    """Runs a shape; with --steps it renders at 4x, posterizes, then box-downsamples so the flat
    bands keep smooth (anti-aliased) edges instead of stair-stepped ones."""
    if a.steps < 2:
        return fn(a)
    size = a.size
    a.size = size * supersample
    big = posterize(fn(a), a.steps)
    a.size = size
    h, w = big.shape
    im = Image.fromarray(big.astype(np.float32), "F").resize((round(w / supersample), round(h / supersample)), Image.BOX)
    return np.asarray(im)


def warn_if_cut(alpha, name):
    border = np.concatenate([alpha[0], alpha[-1], alpha[:, 0], alpha[:, -1]])
    if border.max() > 8 / 255:
        print(f"warning: {name} touches the texture border and will look cut off on a particle; "
              "keep the shape inside radius ~0.9 (smaller --radius/--outer/--width, or less --softness).",
              file=sys.stderr)


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
    sp.add_argument("--softness", type=float, default=0.02, help="edge blur on each side of the band")
    sp.add_argument("--breaks", type=int, default=0)
    sp.add_argument("--gap", type=float, default=0.15)
    sp = sub.add_parser("star"); common(sp)
    sp.add_argument("--points", type=int, default=4)
    sp.add_argument("--inner", type=float, default=0.3, help="valley radius as a fraction of --outer (0.1 needles, 0.6 chunky)")
    sp.add_argument("--outer", type=float, default=0.9, help="tip radius; keep <= 0.9")
    sp.add_argument("--sharp", type=float, default=2.0, help=">1 concave needle rays, 1 straight edges, <1 puffy")
    sp.add_argument("--softness", type=float, default=0.012)
    sp.add_argument("--rotation", type=float, default=0.0, help="degrees; 0 = a tip points up")
    sp.add_argument("--glow", type=float, default=0.3, help="soft halo strength behind the star")
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
    sp.add_argument("--tint", default="FFB43C", help="hex color the masks are tinted with")
    sp.add_argument("--tile", type=int, default=256, help="tile width in pixels")

    a = p.parse_args(argv)
    if a.cmd == "preview":
        return preview(a)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    fn = {"glow": glow, "ring": ring, "star": star, "streak": streak, "slash": slash,
          "noise": noise, "smoke": smoke, "svg": svg}[a.cmd]
    shape = render(fn, a)
    if a.cmd not in ("noise", "smoke", "streak"):
        warn_if_cut(shape, a.cmd)
    (save_gray if a.cmd == "noise" else save_mask)(shape, a.out)


if __name__ == "__main__":
    main()
