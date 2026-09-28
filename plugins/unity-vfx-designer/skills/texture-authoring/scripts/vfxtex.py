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
  vfxtex.py swirl  out.png --turns 1.3 --width 0.14 --seed 2      (ink curl stroke)
  vfxtex.py shard  out.png --spikes 7 --seed 3                     (jagged debris / crack burst)
  vfxtex.py flame  out.png --tongues 3 --seed 1                    (stylized flame tongues)
  vfxtex.py puff   out.png --lobes 4 --strokes 3 --seed 5          (toon puff, dark inner strokes in RGB)
  vfxtex.py stripes out.png --bands 8 --seed 4                     (erosion mask: breaks a shell into strips)
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


def save_rgba(alpha, rgb, path):
    """Mask with shading in RGB (e.g. dark strokes): tint x RGB colors it, alpha is the shape."""
    alpha = np.clip(alpha, 0, 1)
    rgba = np.zeros(alpha.shape + (4,), np.uint8)
    rgba[..., :3] = np.round(np.clip(rgb, 0, 1) * 255).astype(np.uint8)[..., None]
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
    them (--inner, as a fraction of --outer). --sharp > 1 makes concave sides (thin lens-shaped rays);
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
    # Keep the width a power of two (e.g. --aspect 3 -> 4): odd sizes lose compression and mipmaps.
    pow2 = 2 ** max(0, round(math.log2(max(a.aspect, 1e-3))))
    if pow2 != a.aspect:
        print(f"note: --aspect {a.aspect} rounded to {pow2} so the width stays a power of two", file=sys.stderr)
        a.aspect = pow2
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


def strokes_alpha(x, y, paths, softness):
    """Union of tapered strokes. paths: list of (points (N,2) in -1..1, half-widths (N,))."""
    alpha = np.zeros_like(x)
    for pts, widths in paths:
        for i in range(len(pts) - 1):
            p0, p1 = pts[i], pts[i + 1]
            d = p1 - p0
            L2 = float(d @ d) + 1e-12
            t = np.clip(((x - p0[0]) * d[0] + (y - p0[1]) * d[1]) / L2, 0, 1)
            px, py = p0[0] + t * d[0], p0[1] + t * d[1]
            dist = np.sqrt((x - px) ** 2 + (y - py) ** 2)
            w = widths[i] + (widths[i + 1] - widths[i]) * t
            alpha = np.maximum(alpha, 1 - smoothstep(w - softness, w + softness, dist))
    return alpha


def swirl(a):
    """Ink curl: a spiral stroke that thickens in the middle and tapers to sharp ends."""
    x, y = grid(a.size)
    rng = np.random.default_rng(a.seed)
    n = 160
    t = np.linspace(0, 1, n)
    ang = rng.random() * 2 * math.pi + t * a.turns * 2 * math.pi
    r = 0.12 + (0.78 - 0.12) * t ** 0.9
    pts = np.stack([np.cos(ang) * r, np.sin(ang) * r], 1)
    widths = a.width * 0.5 * np.sin(t * math.pi) ** 0.8
    return strokes_alpha(x, y, [(pts, widths)], a.softness)


def shard(a):
    """Jagged debris / dark crack: a crooked spine with uneven sawtooth spikes on both sides,
    rasterized at 4x for clean sharp points."""
    from PIL import ImageDraw
    rng = np.random.default_rng(a.seed)
    size = a.size
    big = size * 4
    n = a.spikes + 2
    # Crooked spine across the texture, inside a 10% margin.
    t = np.linspace(0, 1, n)
    ang = rng.random() * math.pi
    d = np.array([math.cos(ang), math.sin(ang)])
    perp = np.array([-d[1], d[0]])
    spine = [(-0.75 + 1.5 * ti) * d + perp * (rng.random() - 0.5) * 0.35 for ti in t]
    left, right = [], []
    for i, pnt in enumerate(spine):
        w = a.inner * (0.5 + rng.random()) * math.sin(t[i] * math.pi) ** 0.5
        tooth = (i % 2 == 0)
        # Teeth reach well out from a thin spine, so the points stay sharp after downsampling.
        left.append(pnt + perp * (w * (1.0 if tooth else 0.35) + (0.15 + rng.random() * 0.35 if tooth else 0)))
        right.append(pnt - perp * (w * (1.0 if not tooth else 0.35) + (0.15 + rng.random() * 0.35 if not tooth else 0)))
    poly = left + right[::-1]
    poly = [((q[0] * 0.9 + 1) / 2 * big, (q[1] * 0.9 + 1) / 2 * big) for q in poly]
    im = Image.new("L", (big, big), 0)
    ImageDraw.Draw(im).polygon(poly, fill=255)
    return np.asarray(im.resize((size, size), Image.BOX)).astype(float) / 255


def flame(a):
    """Stylized flame tongues: curved tapered strokes rising from a common base."""
    x, y = grid(a.size)
    rng = np.random.default_rng(a.seed)
    paths = []
    n = 60
    t = np.linspace(0, 1, n)
    for i in range(a.tongues):
        spread = (i - (a.tongues - 1) / 2) / max(1, a.tongues - 1)
        height = 0.9 - 0.35 * abs(spread) - 0.15 * rng.random()
        bend = (rng.random() - 0.5) * 0.5 + spread * 0.35
        px = spread * 0.35 * (1 - t) + bend * np.sin(t * math.pi * 0.9) + spread * 0.1
        py = 0.8 - t * (0.8 + height)        # from the bottom (y = 0.8) upward (image y grows down)
        widths = a.width * (1 - t) ** 0.9 * (0.6 + 0.4 * np.sin(np.clip(t * 3, 0, math.pi / 2)))
        paths.append((np.stack([px, py], 1), widths))
    return strokes_alpha(x, y, paths, a.softness)


def puff(a):
    """Toon puff: union of round lobes. Returns (alpha, rgb) with dark inner strokes in RGB, so the
    tint colors the puff and the strokes stay dark."""
    x, y = grid(a.size)
    rng = np.random.default_rng(a.seed)
    alpha = np.zeros_like(x)
    lobes = []
    for i in range(a.lobes):
        ang = i / a.lobes * 2 * math.pi + rng.random() * 0.6
        dist = 0.18 + 0.12 * rng.random()
        rad = 0.34 + 0.12 * rng.random()
        cx, cy = math.cos(ang) * dist, math.sin(ang) * dist
        lobes.append((cx, cy, rad))
        alpha = np.maximum(alpha, 1 - smoothstep(rad - a.softness, rad + a.softness, np.sqrt((x - cx) ** 2 + (y - cy) ** 2)))
    alpha = np.maximum(alpha, 1 - smoothstep(0.3 - a.softness, 0.3 + a.softness, np.sqrt(x * x + y * y)))
    # Inner strokes: where one lobe's edge crosses another lobe, like the lines inside a cartoon cloud.
    lobe_alpha = [1 - smoothstep(rad - a.softness, rad + a.softness, np.sqrt((x - cx) ** 2 + (y - cy) ** 2)) for cx, cy, rad in lobes]
    ink = np.zeros_like(x)
    for i, (cx, cy, rad) in enumerate(lobes[: a.strokes]):
        dist = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        line = 1 - smoothstep(a.width * 0.5 - a.softness, a.width * 0.5 + a.softness, np.abs(dist - rad))
        others = np.maximum.reduce([la for j, la in enumerate(lobe_alpha) if j != i]) if len(lobes) > 1 else np.zeros_like(x)
        # Only the part inside the other lobes and away from the middle: short arcs where lobes
        # overlap near the outline, with soft ends.
        inside = smoothstep(0.4, 1.0, others)
        rc = np.sqrt(x * x + y * y)
        ring_zone = smoothstep(0.2, 0.3, rc)                  # keep the strokes off the puff's middle
        # One arc per lobe: the side of its edge facing the puff's centre.
        toward = -(np.arctan2(cy, cx))
        facing = np.cos(np.arctan2(y - cy, x - cx) + toward + math.pi)
        arc = smoothstep(-0.1, 0.25, facing)
        ink = np.maximum(ink, line * inside * ring_zone * arc)
    ink *= alpha
    rgb = 1 - ink
    return alpha, rgb


def stripes(a):
    """Erosion mask that breaks a shell into strips along u (repeat-wrapped). Band borders are 0, so
    gaps always open first; each strip's centre has its own level, so strips go one by one. With
    --arch the gaps are wide at the bottom (v = 0, the dome's base) and close in a rounded top, like
    arches standing on the ground. Use an odd --bands on a dome so front and back gaps do not line up."""
    size = a.size
    rng = np.random.default_rng(a.seed)
    u = (np.arange(size) + 0.5) / size
    v = 1 - (np.arange(size) + 0.5) / size                   # image row 0 is the texture's top (v = 1)
    U, V = np.meshgrid(u, v)
    band = np.floor(U * a.bands).astype(int) % a.bands
    frac = U * a.bands - np.floor(U * a.bands)
    order = 0.45 + 0.55 * rng.random(a.bands)                 # when each strip goes
    # Plain strips: 0 at the borders, rising to the strip's own level in the middle.
    straight = order[band] * np.sin(frac * math.pi) ** 0.35
    # Arches: the mask is the distance from the bottom of each border, measured on an ellipse, so a
    # rising erosion threshold opens a rounded arch that grows from the ground up and widens.
    d = np.minimum(frac, 1 - frac) / 0.5                     # 0 at a border, 1 mid-strip
    arches = np.sqrt((d * 1.0) ** 2 + (V * 0.85) ** 2) * (0.75 + 0.25 * order[band])
    mask = (1 - a.arch) * straight + a.arch * np.clip(arches, 0, 1)
    n = value_noise(size, 4, 3, a.seed + 7)
    return np.clip(mask * (1 - a.noise) + n * a.noise * mask, 0, 1)


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
    ground = np.array([int(a.ground.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4)], float) / 255 if a.ground else None
    tile = a.tile
    columns = []
    for p in a.inputs:
        im = Image.open(p).convert("RGBA")
        # Data textures (noise, stripes: the same gray in RGB and alpha) are shown as values. Decide on
        # the original pixels: resizing an RGBA image premultiplies and changes RGB where alpha is low.
        src = np.asarray(im).astype(float) / 255
        data = src[..., 3].min() > 0.99 and np.allclose(src[..., 0], src[..., 1], atol=0.02) or np.allclose(src[..., :3], src[..., 3:4], atol=0.02)
        w, h = im.size
        im = im.resize((tile, max(1, round(tile * h / w))), Image.LANCZOS)
        arr = np.asarray(im).astype(float) / 255
        alpha = arr[..., 3:4]
        color = tint * arr[..., :3]
        rgb = arr[..., :3]
        if data:
            # Show the values, not a tinted shape. The resized alpha channel holds them unchanged
            # (RGB was premultiplied by the resize); fully opaque data keeps its values in RGB.
            gray = rgb.mean(-1, keepdims=True) if src[..., 3].min() > 0.99 else alpha
            alpha = np.ones_like(alpha)
            color = np.repeat(gray, 3, axis=2)
        rows = [color if data else np.repeat(alpha, 3, axis=2),
                dark * (1 - alpha) + color * alpha,
                light * (1 - alpha) + color * alpha]
        if ground is not None:
            rows.append(ground * (1 - alpha) + color * alpha)
        gap = np.ones((4, tile, 3)) * 0.5
        parts = []
        for r in rows:
            parts += [r, gap]
        columns.append(np.concatenate(parts[:-1], axis=0))
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
    sp.add_argument("--sharp", type=float, default=2.0, help=">1 concave sides (3 + low --inner: thin lens/leaf rays), 1 straight edges, <1 puffy")
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
    sp = sub.add_parser("swirl"); common(sp)
    sp.add_argument("--turns", type=float, default=1.3); sp.add_argument("--width", type=float, default=0.14)
    sp.add_argument("--softness", type=float, default=0.012)
    sp = sub.add_parser("shard"); common(sp)
    sp.add_argument("--spikes", type=int, default=7); sp.add_argument("--inner", type=float, default=0.12)
    sp.add_argument("--softness", type=float, default=0.008)
    sp = sub.add_parser("flame"); common(sp)
    sp.add_argument("--tongues", type=int, default=3); sp.add_argument("--width", type=float, default=0.16)
    sp.add_argument("--softness", type=float, default=0.012)
    sp = sub.add_parser("puff"); common(sp)
    sp.add_argument("--lobes", type=int, default=4); sp.add_argument("--strokes", type=int, default=2)
    sp.add_argument("--width", type=float, default=0.07); sp.add_argument("--softness", type=float, default=0.012)
    sp = sub.add_parser("stripes"); common(sp)
    sp.add_argument("--bands", type=int, default=7); sp.add_argument("--noise", type=float, default=0.15)
    sp.add_argument("--arch", type=float, default=0.0, help="0..1: gaps wide at the bottom, rounded at the top (arches on the ground)")
    sp = sub.add_parser("svg"); common(sp)
    sp.add_argument("--svg", type=Path, required=True)
    sp = sub.add_parser("preview")
    sp.add_argument("out", type=Path); sp.add_argument("inputs", nargs="+", type=Path)
    sp.add_argument("--tint", default="FFB43C", help="hex color the masks are tinted with")
    sp.add_argument("--tile", type=int, default=256, help="tile width in pixels")
    sp.add_argument("--ground", help="hex color of the game's ground: adds a fourth row tinted on it")

    a = p.parse_args(argv)
    if a.cmd == "preview":
        return preview(a)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    fn = {"glow": glow, "ring": ring, "star": star, "streak": streak, "slash": slash,
          "noise": noise, "smoke": smoke, "svg": svg, "swirl": swirl, "shard": shard,
          "flame": flame, "puff": puff, "stripes": stripes}[a.cmd]
    if a.cmd == "puff":
        alpha, rgb = fn(a)
        warn_if_cut(alpha, a.cmd)
        return save_rgba(alpha, rgb, a.out)
    shape = render(fn, a)
    if a.cmd not in ("noise", "smoke", "streak", "stripes"):
        warn_if_cut(shape, a.cmd)
    (save_gray if a.cmd in ("noise", "stripes") else save_mask)(shape, a.out)


if __name__ == "__main__":
    main()
