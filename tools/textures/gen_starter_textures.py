#!/usr/bin/env python3
"""Generate the VFX Toolkit's starter textures (procedural, reproducible).

White RGB with the shape in alpha, so color comes from the material tint and particle
colors. Output: unity-package/com.effectdesigner.vfxtoolkit/Textures/.

  T_VFX_SoftGlow.png  radial glow with a hot core (sparks, embers, pops)
  T_VFX_Star4.png     stylized 4-point sparkle with a glowing core (flashes, glints)
  T_VFX_Noise.png     tileable fractal value noise in RGB and A (erosion, distortion)

Requires numpy and Pillow.
"""
from pathlib import Path

import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parents[2] / "unity-package/com.effectdesigner.vfxtoolkit/Textures"


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def grid(size):
    c = (np.arange(size) + 0.5) / size * 2 - 1
    return np.meshgrid(c, c)


def save_alpha(alpha, name):
    alpha = np.clip(alpha, 0, 1)
    rgba = np.zeros(alpha.shape + (4,), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = np.round(alpha * 255).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(OUT / name)


def soft_glow(size=128):
    x, y = grid(size)
    r = np.sqrt(x * x + y * y)
    halo = (1 - smoothstep(0.0, 1.0, r)) ** 2
    core = 1 - smoothstep(0.0, 0.25, r)
    return np.maximum(halo * 0.75, core)


def star4(size=256):
    x, y = grid(size)
    r = np.sqrt(x * x + y * y)
    # Two thin tapered rays per axis: width shrinks toward the tip -> stylized sparkle.
    def ray(a, b):
        width = 0.10 * (1 - np.clip(np.abs(a), 0, 1)) ** 1.5 + 0.004
        return (1 - smoothstep(0.0, width, np.abs(b))) * (1 - smoothstep(0.6, 1.0, np.abs(a)))
    rays = np.maximum(ray(x, y), ray(y, x))
    core = 1 - smoothstep(0.0, 0.22, r)
    halo = (1 - smoothstep(0.0, 0.6, r)) ** 2 * 0.5
    return np.clip(np.maximum.reduce([rays, core, halo]), 0, 1)


def tileable_noise(size=256, octaves=5, seed=7):
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size))
    amplitude, weight = 1.0, 0.0
    for o in range(octaves):
        cells = 4 * 2 ** o
        lattice = rng.random((cells, cells))
        # Wrap the lattice so the texture tiles.
        coords = np.arange(size) / size * cells
        i0 = np.floor(coords).astype(int) % cells
        i1 = (i0 + 1) % cells
        f = coords - np.floor(coords)
        f = f * f * (3 - 2 * f)
        fx, fy = np.meshgrid(f, f)
        a = lattice[np.ix_(i0, i0)]
        b = lattice[np.ix_(i0, i1)]
        c = lattice[np.ix_(i1, i0)]
        d = lattice[np.ix_(i1, i1)]
        layer = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
        total += layer * amplitude
        weight += amplitude
        amplitude *= 0.5
    total /= weight
    total = (total - total.min()) / (total.max() - total.min())
    return total


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    save_alpha(soft_glow(), "T_VFX_SoftGlow.png")
    save_alpha(star4(), "T_VFX_Star4.png")
    n = tileable_noise()
    gray = np.round(n * 255).astype(np.uint8)
    Image.fromarray(np.dstack([gray, gray, gray, gray]), "RGBA").save(OUT / "T_VFX_Noise.png")
    print(f"wrote textures to {OUT}")


if __name__ == "__main__":
    main()
