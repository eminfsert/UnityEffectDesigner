---
name: reference-analysis
description: Measuring reference images, GIFs or short videos of an effect with the bundled vfxref.py (crop, reference sheet, per-frame composition of white core / bright color / ink, bright hue, palette) and comparing captures against them side by side. Load when the user gives reference images or clips for an effect, and when reviewing an effect that has references.
user-invocable: false
---

# Reference analysis

References tell you **what the effect is made of and in what order**. Look at them first,
then measure them, so the spec's beats and palette come from numbers rather than guesses,
and the critic can compare the build against the same numbers.

## Where things go

- Reference files: `Assets/VFX/<Id>/Design/refs/` (the user puts them there, or you copy
  pasted images there). References from other games are for study: recreate the style,
  timing and palette; never extract or copy their textures or meshes.
- Analysis output: `Assets/VFX/<Id>/Design/refs/analysis/` (`reference_sheet.png`,
  `reference.json`, `ref_NN.png` crops).

## `scripts/vfxref.py sheet`

Run it with the project's Python (numpy + Pillow, the same as `vfxtex.py`).

```
vfxref.py sheet <out> --inputs f1.png f2.png ... --crop x0,y0,x1,y1 --times 0,0.2,0.45,0.8,1.2
          [--ignore x0,y0,x1,y1]... [--exclude-hue 190-250]...
vfxref.py sheet <out> --gif clip.gif [--every 2] --crop ...
vfxref.py sheet <out> --video clip.mp4 --fps 20 --start 1.0 --end 2.8 --crop ...   (needs ffmpeg)
```

1. **Crop** to the effect with one box for all frames (pixels, or 0–1 fractions of the image).
   Open the images, find where the effect is in every frame, and take the union with a
   small margin. A tight crop matters: coverage and background detection work on it.
2. **Ignore** boxes (relative to the crop) remove what is not the effect: characters, UI,
   name tags.
3. **Background.** Hues that dominate the crop border (grass, sky) are excluded
   automatically, but only up to the background's own brightness, so a bright yellow
   effect over yellow-green grass is kept, and dark smoke over grass is kept. Add
   `--exclude-hue lo-hi` for anything else (a blue sky corner: `190-250`).
4. **Times.** From a GIF or video they are exact. For screenshots give your best estimate
   with `--times`, anchored on what the user said about duration; say in the spec that
   they are estimates.
5. **Open `reference_sheet.png`.** Row 2 ("measured") shows which pixels were counted:
   check that the effect is lit and the background dimmed. Fix the crop, ignore boxes or
   hue ranges if not, and rerun.

`reference.json`, per frame: `coverage` (share of the crop), and shares **of the effect's
own pixels**: `white` (hot core), `bright` (the effect's color), `ink` (near black, not the
background's hue), `smoke` (the background seen through something darker: translucent dark
smoke over grass), `mid` (translucent color, shading, soft edges); `bright_hue` and `bright_sat` of the bright pixels; a small
`palette`. At the top: a merged `palette` and `summary` (`bright_peak`, `ink_peak`,
`hue_path`).

## From measurements to the spec

- **Beats**: the frame where `white + bright` peaks is the impact; where `ink` overtakes
  `bright` the effect turns from fire to smoke/ink; the effect ends where coverage falls
  to a trace.
- **Palette per phase**: take the swatches of each phase (core, primary, cooled color,
  ink, smoke). The `hue_path` is the color sequence to reproduce (e.g. 42° → 33° → 31°:
  white-yellow cooling to orange).
- **Layer inventory**: list every distinct element you can see and the frames it appears
  in (dome, strips, puffs, shards, crescents, dashes, embers...). That gives each layer's
  window. Measurements do not replace looking.
- **Scale** from objects of known size in the frame (characters, doors), with the user's
  confirmation.
- Record the analysis paths and the key numbers in the spec's `references` block, so the
  critic compares against the same data.

## `scripts/vfxref.py compare` (critic)

```
vfxref.py compare <out> --reference <analysis>/reference.json \
          --capture <capture outputFolder> --view three_quarter --background-name <bg> --background "#4E8A3A"
```

- Pairs each reference frame with the capture frame nearest its time (`--time-scale`,
  `--time-offset` if the build is intentionally faster or slower) and writes
  `compare.png` (reference row over capture row) and `compare.json`.
- Measure the capture on the **ground color or a light background**, never on dark when
  the reference has ink: black on a dark background cannot be measured (a warning says so).
- `summary.curve_correlation` near 1 for `bright` and `ink` means the capture changes
  composition in the same order as the reference; `mean_abs_share_difference` and
  `mean_abs_bright_hue_difference` say how far each phase is off.
- Framing differs between a gameplay screenshot and a capture, so compare shares, hues and
  order, not pixel positions or absolute coverage.
- When no `--background` is given, the capture's background is read from the frames'
  bottom corners (the ground, with `ground`); `summary.background_used` says which color was
  used.
- **Characters and UI:** a following camera moves the player through the crop; check the
  "measured" row of every frame and widen `--ignore` boxes until no character is lit.
