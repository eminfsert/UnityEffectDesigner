#!/usr/bin/env python3
"""vfxref: measure reference images/videos of an effect, and compare captures against them.

Two modes:

  sheet    Crop the effect out of reference frames (screenshots, a GIF, a frame folder, or a
           video if ffmpeg is installed), write a labelled reference sheet and reference.json:
           per frame the effect's pixel composition (white core / bright color / mid / ink),
           its dominant bright hue and a small palette, plus a merged palette suggestion.

  compare  Put reference frames and capture frames (vfx_capture_timeline PNGs) side by side
           at matching times, measure both the same way, and write compare.png + compare.json
           with the per-column differences.

Examples
  vfxref.py sheet refs_out --inputs f1.png f2.png f3.png --crop 0.21,0.17,0.54,0.58 \\
            --times 0,0.15,0.4,0.7,1.1 --exclude-hue 55-175 --exclude-hue 190-250
  vfxref.py sheet refs_out --gif boom.gif --crop 120,40,520,380
  vfxref.py sheet refs_out --video boom.mp4 --fps 20 --start 1.2 --end 3.0
  vfxref.py compare cmp_out --reference refs_out/reference.json \\
            --capture Library/VFXToolkit/Captures/boom_iter1_... --view three_quarter --background "#4E8A3A"

Pixel classes (of the effect's pixels, background excluded), comparable across very different
framings because they are shares of the effect, not of the frame:
  white   value >= 0.85 and saturation < 0.2    (hot core)
  bright  value >= 0.6 and saturation >= 0.25   (the effect's color)
  ink     value < 0.18                          (black strokes, dark smoke cores)
  mid     everything else                       (translucent parts, shading, smoke)

Needs numpy and Pillow. Every command prints what it wrote; open the PNGs and look.
"""
import argparse
import colorsys
import json
import math
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    import numpy as np
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover
    sys.exit("vfxref needs numpy and Pillow: pip install numpy pillow")


# ----------------------------------------------------------------- measuring

def rgb_to_hsv(a):
    """a: float array (..., 3) in 0-1 -> hue degrees, saturation, value."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = (mx == r) & m
    gg = (mx == g) & m & ~rr
    bb = m & ~rr & ~gg
    h[rr] = ((g - b)[rr] / d[rr]) % 6
    h[gg] = (b - r)[gg] / d[gg] + 2
    h[bb] = (r - g)[bb] / d[bb] + 4
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h * 60, s, mx


def hue_in(h, lo, hi):
    return (h >= lo) & (h <= hi) if lo <= hi else (h >= lo) | (h <= hi)


def parse_ranges(ranges):
    out = []
    for r in ranges or []:
        lo, hi = r.split("-")
        out.append((float(lo), float(hi)))
    return out


def auto_background_hues(a, h, s, v):
    """Hue ranges that dominate the crop's border (grass, sky...), assumed to be background."""
    border = np.zeros(h.shape, bool)
    k = max(2, min(h.shape) // 20)
    border[:k, :] = border[-k:, :] = border[:, :k] = border[:, -k:] = True
    sel = border & (s > 0.2) & (v > 0.2)
    if sel.sum() < 50:
        return []
    hist, edges = np.histogram(h[sel], bins=36, range=(0, 360))
    ranges = []
    for i in np.argsort(-hist)[:3]:
        if hist[i] < 0.15 * sel.sum():
            continue
        c = (edges[i] + edges[i + 1]) / 2
        near = sel & hue_in(h, (c - 20) % 360, (c + 20) % 360)
        # Only as bright as the background itself: a bright yellow effect next to yellow-green
        # grass must not be removed with it.
        vmax = float(np.percentile(v[near], 98)) + 0.05 if near.any() else 1.0
        ranges.append(((c - 20) % 360, (c + 20) % 360, vmax))
    return ranges


def effect_mask(a, exclude_hues=None, background_rgb=None, auto=True):
    """Pixels that belong to the effect. Background is either a known flat color (captures) or
    hue ranges (references over grass/sky), given or detected from the crop border."""
    h, s, v = rgb_to_hsv(a)
    mask = np.ones(h.shape, bool)
    used = []
    if background_rgb is not None:
        bg = np.array(background_rgb, float)
        mask &= np.abs(a - bg).max(-1) > 16 / 255
    ranges = list(exclude_hues or [])
    if background_rgb is None and auto:
        ranges += auto_background_hues(a, h, s, v)
    for r in ranges:
        lo, hi = r[0], r[1]
        vmax = r[2] if len(r) > 2 else 1.01
        # Keep dark pixels even in a background hue: dark smoke over grass stays greenish.
        mask &= ~(hue_in(h, lo, hi) & (s > 0.2) & (v > 0.25) & (v <= vmax))
        used.append([round(lo), round(hi)] + ([round(vmax, 2)] if len(r) > 2 else []))
    return mask, (h, s, v), used


def kmeans(x, k, iters=25, seed=0):
    if len(x) == 0:
        return np.zeros((0, 3)), np.zeros(0)
    k = min(k, len(x))
    rng = np.random.default_rng(seed)
    c = x[rng.choice(len(x), k, replace=False)]
    for _ in range(iters):
        lab = ((x[:, None, :] - c[None]) ** 2).sum(-1).argmin(1)
        c = np.array([x[lab == j].mean(0) if (lab == j).any() else c[j] for j in range(k)])
    return c, np.bincount(lab, minlength=k) / len(x)


def hexcode(rgb):
    return "#" + "".join(f"{int(round(min(max(x, 0), 1) * 255)):02X}" for x in rgb)


def swatch(rgb, share):
    hh, ss, vv = colorsys.rgb_to_hsv(*[float(x) for x in rgb])
    return {"hex": hexcode(rgb), "share": round(float(share), 3), "hue": round(hh * 360), "sat": round(ss, 2), "value": round(vv, 2)}


def circular_mean(deg, weights=None):
    if len(deg) == 0:
        return None
    r = np.radians(deg)
    w = np.ones_like(r) if weights is None else weights
    x, y = (np.cos(r) * w).sum(), (np.sin(r) * w).sum()
    if abs(x) < 1e-9 and abs(y) < 1e-9:
        return None
    return round(float(math.degrees(math.atan2(y, x)) % 360), 1)


def measure(a, mask, hsv, palette_size=5):
    h, s, v = hsv
    n = int(mask.sum())
    out = {"coverage": round(n / mask.size, 4)}
    if n < 20:
        out.update({"white": 0, "bright": 0, "ink": 0, "mid": 0, "bright_hue": None, "palette": []})
        return out
    white = mask & (v >= 0.85) & (s < 0.2)
    bright = mask & (v >= 0.6) & (s >= 0.25)
    ink = mask & (v < 0.18)
    mid = mask & ~white & ~bright & ~ink
    out.update({k: round(float(m.sum()) / n, 3) for k, m in (("white", white), ("bright", bright), ("ink", ink), ("mid", mid))})
    out["bright_hue"] = circular_mean(h[bright], s[bright]) if bright.any() else None
    out["bright_sat"] = round(float(s[bright].mean()), 2) if bright.any() else None
    px = a[mask]
    if len(px) > 20000:
        px = px[np.random.default_rng(1).choice(len(px), 20000, replace=False)]
    c, w = kmeans(px, palette_size)
    out["palette"] = [swatch(c[j], w[j]) for j in np.argsort(-w)]
    return out


# ----------------------------------------------------------------- frames in

def parse_crop(text, size):
    if not text:
        return None
    vals = [float(x) for x in text.split(",")]
    if len(vals) != 4:
        sys.exit("--crop needs x0,y0,x1,y1 (pixels, or fractions 0-1 of the image size)")
    if all(0 <= x <= 1 for x in vals):
        w, h = size
        vals = [vals[0] * w, vals[1] * h, vals[2] * w, vals[3] * h]
    return tuple(int(round(x)) for x in vals)


def load_frames(a):
    """-> list of (PIL image RGB, time or None, source label)."""
    frames = []
    if a.inputs:
        for p in a.inputs:
            frames.append((Image.open(p).convert("RGB"), None, Path(p).name))
    if a.folder:
        for p in sorted(Path(a.folder).iterdir()):
            if p.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp"):
                frames.append((Image.open(p).convert("RGB"), None, p.name))
    if a.gif:
        im = Image.open(a.gif)
        t = 0.0
        i = 0
        while True:
            dur = im.info.get("duration", 100) / 1000.0
            if i % a.every == 0:
                frames.append((im.convert("RGB"), round(t, 3), f"{Path(a.gif).name}#{i}"))
            t += dur
            i += 1
            try:
                im.seek(i)
            except EOFError:
                break
    if a.video:
        if not shutil.which("ffmpeg"):
            sys.exit("--video needs ffmpeg on PATH (ask the user before installing it). Alternatives: export "
                     "the clip as a GIF or as numbered PNG frames and use --gif / --folder.")
        tmp = Path(tempfile.mkdtemp(prefix="vfxref_"))
        cmd = ["ffmpeg", "-loglevel", "error", "-ss", str(a.start or 0), "-i", str(a.video)]
        if a.end:
            cmd += ["-t", str(a.end - (a.start or 0))]
        cmd += ["-vf", f"fps={a.fps}", str(tmp / "f_%04d.png")]
        subprocess.run(cmd, check=True)
        for i, p in enumerate(sorted(tmp.glob("f_*.png"))):
            frames.append((Image.open(p).convert("RGB"), round(i / a.fps, 3), f"{Path(a.video).name}@{(a.start or 0) + i / a.fps:.2f}s"))
    if not frames:
        sys.exit("No frames: give --inputs, --folder, --gif or --video.")
    if a.times:
        times = [float(x) for x in a.times.split(",")]
        if len(times) != len(frames):
            sys.exit(f"--times has {len(times)} values for {len(frames)} frames.")
        frames = [(im, t, src) for (im, _, src), t in zip(frames, times)]
    return frames


# ----------------------------------------------------------------- sheets

def draw_sheet(rows, labels, row_titles, out, cell=300):
    """Grid: each row a list of PIL images (None = empty) sharing column positions; text lines
    under each column."""
    title_w = 90 if row_titles else 0
    ncol = max(len(r) for r in rows)
    heights = []
    for row in rows:
        ims = [im for im in row if im is not None]
        heights.append(int(cell * max(im.size[1] / im.size[0] for im in ims)) if ims else 0)
    W = title_w + ncol * (cell + 4)
    H = sum(h + 4 for h in heights) + 64
    sheet = Image.new("RGB", (W, H), (24, 24, 28))
    d = ImageDraw.Draw(sheet)
    y = 0
    for r, row in enumerate(rows):
        if row_titles:
            d.text((4, y + heights[r] // 2), row_titles[r], fill=(230, 230, 230))
        for c, im in enumerate(row):
            if im is None:
                continue
            fit = im.resize((cell, max(1, int(round(cell * im.size[1] / im.size[0])))), Image.LANCZOS)
            sheet.paste(fit, (title_w + c * (cell + 4), y))
        y += heights[r] + 4
    for c, lines in enumerate(labels):
        for i, line in enumerate(lines[:4]):
            d.text((title_w + c * (cell + 4) + 2, y + i * 14), line, fill=(230, 230, 230))
    sheet.save(out)
    return out


def mask_overlay(crop, mask):
    """The crop with non-effect pixels dimmed to gray: shows what was measured."""
    arr = np.asarray(crop).astype(float)
    gray = arr.mean(-1, keepdims=True) * 0.25 + 20
    shown = np.where(mask[..., None], arr, gray)
    return Image.fromarray(np.clip(shown, 0, 255).astype(np.uint8))


def palette_strip(palette, w, h=26):
    im = Image.new("RGB", (w, h), (24, 24, 28))
    d = ImageDraw.Draw(im)
    x = 0
    for sw in palette:
        if x >= w:
            break
        ww = max(4, int(round(sw["share"] * w)))
        d.rectangle([x, 0, min(w - 1, x + ww), h], fill=sw["hex"])
        x += ww
    return im


def fmt(m):
    hue = "-" if m.get("bright_hue") is None else f"{m['bright_hue']:.0f}"
    return f"W{m['white']*100:.0f} B{m['bright']*100:.0f} I{m['ink']*100:.0f} h{hue}"


# ----------------------------------------------------------------- commands

def cmd_sheet(a):
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    frames = load_frames(a)
    exclude = parse_ranges(a.exclude_hue)
    results = []
    crops = []
    overlays = []
    all_px = []
    for i, (im, t, src) in enumerate(frames):
        box = parse_crop(a.crop, im.size)
        crop = im.crop(box) if box else im
        arr = np.asarray(crop).astype(float) / 255
        mask, hsv, used = effect_mask(arr, exclude, auto=not a.no_auto_background)
        for box_text in a.ignore or []:
            ib = parse_crop(box_text, crop.size)
            mask[ib[1]:ib[3], ib[0]:ib[2]] = False
        overlays.append(mask_overlay(crop, mask))
        m = measure(arr, mask, hsv)
        m.update({"index": i, "time": t, "source": src, "excluded_hues": used})
        results.append(m)
        crop_path = out / f"ref_{i:02d}.png"
        crop.save(crop_path)
        m["crop"] = str(crop_path)
        crops.append(crop)
        px = arr[mask]
        if len(px):
            all_px.append(px[np.random.default_rng(i).choice(len(px), min(4000, len(px)), replace=False)])

    merged = []
    if all_px:
        c, w = kmeans(np.concatenate(all_px), a.palette)
        merged = [swatch(c[j], w[j]) for j in np.argsort(-w)]

    # Phases: where the bright share peaks and where ink takes over, as hints for the spec's beats.
    def at(idx):
        return results[idx]["time"] if results[idx]["time"] is not None else f"frame {idx}"
    bright_peak = int(np.argmax([r["bright"] + r["white"] for r in results]))
    ink_peak = int(np.argmax([r["ink"] for r in results]))
    summary = {
        "frames": len(results),
        "bright_peak": at(bright_peak),
        "ink_peak": at(ink_peak),
        "hue_path": [r["bright_hue"] for r in results],
        "note": "Shares are of the effect's own pixels. bright_hue is the saturation-weighted hue of its bright pixels.",
    }
    doc = {"tool": "vfxref", "crop": a.crop, "frames": results, "palette": merged, "summary": summary}
    (out / "reference.json").write_text(json.dumps(doc, indent=2))

    labels = [[f"{r['time'] if r['time'] is not None else '#' + str(r['index'])}  {r['source'][:22]}", fmt(r)] for r in results]
    rows = [crops, overlays, [palette_strip(r["palette"], 300) for r in results]]
    draw_sheet(rows, labels, ["reference", "measured", "palette"], out / "reference_sheet.png")
    print(out / "reference_sheet.png")
    print(out / "reference.json")


def capture_frames(folder, view, background):
    """{time: path} for one view/background of a vfx_capture_timeline output folder."""
    found = {}
    for p in Path(folder).glob("*.png"):
        stem = p.stem
        if "_t" not in stem or stem == "contact_sheet":
            continue
        prefix, t = stem.rsplit("_t", 1)
        if view and not prefix.startswith(view.replace(" ", "_") + "_"):
            continue
        if background and not prefix.endswith("_" + background.lstrip("#").lower()):
            continue
        try:
            found[float(t)] = p
        except ValueError:
            pass
    return dict(sorted(found.items()))


def cmd_compare(a):
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    ref = json.loads(Path(a.reference).read_text())
    cap = capture_frames(a.capture, a.view, a.background_name)
    if not cap:
        sys.exit(f"No capture frames for view '{a.view}' / background '{a.background_name}' in {a.capture}.")
    bg = None
    if a.background:
        hx = a.background.lstrip("#")
        bg = [int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    cap_times = list(cap.keys())
    warnings = []
    if bg is not None and max(bg) < 0.2 and max(r["ink"] for r in ref["frames"]) > 0.05:
        warnings.append("The reference has ink (black) layers but the capture background is dark: ink cannot be told "
                        "apart from it and measures as missing. Compare on the ground color or a light background.")
    cols_ref, cols_cap, labels, rows_out = [], [], [], []
    for i, r in enumerate(ref["frames"]):
        t = r["time"] if r["time"] is not None else i * a.spacing
        t = t * a.time_scale + a.time_offset
        nearest = min(cap_times, key=lambda c: abs(c - t))
        im = Image.open(cap[nearest]).convert("RGB")
        arr = np.asarray(im).astype(float) / 255
        if bg is None:
            corner = np.concatenate([arr[:4, :4].reshape(-1, 3), arr[-4:, -4:].reshape(-1, 3)]).mean(0)
            use_bg = corner
        else:
            use_bg = bg
        mask, hsv, _ = effect_mask(arr, background_rgb=use_bg, auto=False)
        m = measure(arr, mask, hsv)
        d = {k: round(m[k] - r[k], 3) for k in ("white", "bright", "ink", "mid")}
        hue_diff = None
        if m.get("bright_hue") is not None and r.get("bright_hue") is not None:
            hue_diff = round(((m["bright_hue"] - r["bright_hue"] + 180) % 360) - 180, 1)
        rows_out.append({"reference_time": r["time"], "capture_time": nearest, "reference": {k: r[k] for k in ("white", "bright", "ink", "mid", "bright_hue")},
                         "capture": {k: m[k] for k in ("white", "bright", "ink", "mid", "bright_hue", "coverage")},
                         "difference": d, "bright_hue_difference": hue_diff})
        cols_ref.append(Image.open(r["crop"]).convert("RGB") if Path(r["crop"]).exists() else None)
        cols_cap.append(im)
        labels.append([f"ref {r['time']}  cap {nearest:.3f}", "ref " + fmt(r), "cap " + fmt(m),
                       f"dhue {hue_diff if hue_diff is not None else '-'}"])

    def curve_corr(key):
        x = np.array([row["reference"][key] for row in rows_out], float)
        y = np.array([row["capture"][key] for row in rows_out], float)
        if x.std() < 1e-6 or y.std() < 1e-6:
            return None
        return round(float(np.corrcoef(x, y)[0, 1]), 2)

    hue_diffs = [abs(r["bright_hue_difference"]) for r in rows_out if r["bright_hue_difference"] is not None]
    summary = {
        "mean_abs_bright_hue_difference": round(float(np.mean(hue_diffs)), 1) if hue_diffs else None,
        "mean_abs_share_difference": {k: round(float(np.mean([abs(r["difference"][k]) for r in rows_out])), 3) for k in ("white", "bright", "ink", "mid")},
        "curve_correlation": {k: curve_corr(k) for k in ("white", "bright", "ink")},
        "note": "Correlation near 1 = the capture's composition changes over time like the reference (same sequence). "
                "Share differences are of the effect's own pixels; hue differences are in degrees (+ = capture more toward green/yellow).",
    }
    (out / "compare.json").write_text(json.dumps({"columns": rows_out, "summary": summary, "warnings": warnings}, indent=2))
    for w in warnings:
        print("warning: " + w, file=sys.stderr)
    draw_sheet([cols_ref, cols_cap], labels, ["reference", "capture"], out / "compare.png")
    print(out / "compare.png")
    print(out / "compare.json")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("sheet", help="analyze reference frames")
    s.add_argument("out")
    s.add_argument("--inputs", nargs="+", help="image files in time order")
    s.add_argument("--folder", help="folder of frames (sorted by name)")
    s.add_argument("--gif")
    s.add_argument("--every", type=int, default=1, help="with --gif: keep every Nth frame")
    s.add_argument("--video")
    s.add_argument("--fps", type=float, default=15)
    s.add_argument("--start", type=float)
    s.add_argument("--end", type=float)
    s.add_argument("--times", help="comma-separated seconds per frame, if known or estimated")
    s.add_argument("--crop", help="x0,y0,x1,y1 in pixels or 0-1 fractions; the same box for every frame")
    s.add_argument("--exclude-hue", action="append", help="background hue range in degrees, e.g. 55-175 (grass); repeatable")
    s.add_argument("--ignore", action="append", help="x0,y0,x1,y1 box inside the crop (pixels or fractions) left out of the measurement, e.g. a character or UI; repeatable")
    s.add_argument("--no-auto-background", action="store_true", help="do not guess background hues from the crop border")
    s.add_argument("--palette", type=int, default=6)

    c = sub.add_parser("compare", help="reference vs capture, side by side")
    c.add_argument("out")
    c.add_argument("--reference", required=True, help="reference.json from 'sheet'")
    c.add_argument("--capture", required=True, help="vfx_capture_timeline output folder")
    c.add_argument("--view", default="three_quarter")
    c.add_argument("--background-name", dest="background_name", default=None, help="background name in the frame file names (dark, light, 4e8a3a...)")
    c.add_argument("--background", help="background/ground color of the capture, hex; default: from the frame corners")
    c.add_argument("--time-offset", type=float, default=0.0, help="added to reference times (after scaling)")
    c.add_argument("--time-scale", type=float, default=1.0)
    c.add_argument("--spacing", type=float, default=0.25, help="assumed seconds between reference frames without times")

    a = p.parse_args(argv)
    {"sheet": cmd_sheet, "compare": cmd_compare}[a.cmd](a)


if __name__ == "__main__":
    main()
