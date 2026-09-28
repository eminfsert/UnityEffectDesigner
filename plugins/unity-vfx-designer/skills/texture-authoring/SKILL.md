---
name: texture-authoring
description: Making the 2D textures an effect needs (particle shape masks, rings, stars, slashes, streaks, noise, stylized smoke flipbooks, SVG drawings) with the bundled vfxtex.py generator or hand-written SVG, and importing them correctly into Unity. Load when an effect's manifest asks for textures that the starter set does not cover.
user-invocable: false
---

# Texture authoring

## Conventions

- **Masks are white RGB with the shape in alpha.** Color comes from the material tint ×
  particle color, so one mask serves every palette and element variant.
- **Noise and data textures** are grayscale, linear (not sRGB), and repeat-wrapped.
- Sizes are powers of two: 128 for small glows and sparks, 256 for most shapes, 512 only
  for large hero shapes (shockwave rings, magic circles).
- Put textures in `Assets/VFX/<EffectId>/Textures/` named `T_<EffectId>_<Name>.png`.
- Previews go to `Assets/VFX/<EffectId>/Design/previews/`; any one-off script you write
  (e.g. a custom shape the generator lacks) goes to `Assets/VFX/<EffectId>/Design/tools/`,
  so the next round can rerun it with changed numbers.
- Keep every shape inside radius ~0.9 of the texture (5% margin): anything reaching the
  border is visibly cut off on a particle quad. The generator warns when a mask touches it.

## Starter textures (use them first)

The VFX Toolkit package ships:
- `Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_SoftGlow.png`: glow with a hot core
- `Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Star4.png`: 4-point sparkle
- `Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Noise.png`: tileable noise for erosion

Make a new texture only when the layer's shape language needs it.

## Generator: `scripts/vfxtex.py`

Run it with the project's Python (`python --version`, on Windows also `py -3`). It needs
`numpy` and `Pillow`. If they are missing, ask the user before installing anything; use the
C# fallback below instead of blocking.

| Command | Makes | Useful options |
|---|---|---|
| `glow` | soft glow with core | `--core 0.25 --falloff 2 --halo 0.75` |
| `ring` | ring / shockwave band | `--radius 0.7 --width 0.12 --softness 0.02 --breaks 5 --gap 0.15`; keep radius + width/2 + softness ≤ 0.9 |
| `star` | N-point sparkle / flower | `--points 4 --inner 0.3 --sharp 2 --glow 0.3` (see below) |
| `streak` | elongated spark (for stretched billboards) | `--aspect 4` |
| `slash` | crescent arc for sword trails / swipes | `--arc 150 --width 0.18` |
| `noise` | tileable fractal value noise | `--cells 4 --octaves 5 --seed 7` |
| `smoke` | stylized puff flipbook (grow → erode) | `--frames 16 --grid 4` (use with `texture_sheet_animation.num_tiles_x/y` = 4) |
| `swirl` | ink curl: spiral stroke, thick middle, sharp ends | `--turns 1.3 --width 0.14 --seed 2` |
| `shard` | jagged debris / dark crack (crooked spine with sawtooth spikes) | `--spikes 7 --inner 0.12 --seed 3` |
| `flame` | stylized flame tongues rising from a base | `--tongues 3 --width 0.16 --seed 1` |
| `puff` | toon puff: a round body with bumps around its edge (a cartoon cloud), **dark strokes in RGB** where bumps meet the body (tint colors the puff, strokes stay dark) | `--lobes 6 --strokes 3 --seed 5` |
| `stripes` | erosion mask (grayscale, repeat) that breaks a shell into strips along u: borders open first, each strip goes at its own time; `--arch 1` opens rounded arches from the ground up instead of straight slits | `--bands 7 --arch 1 --noise 0.15 --seed 4` (odd band counts keep a dome's front and back gaps from lining up) |
| `wisp` | smoke tendril: thin S-curved stroke, thick near its start, fading to a hair | `--curls 1.5 --width 0.18 --seed 3` |
| `svg` | rasterize an SVG you wrote | `--svg shape.svg` (needs `cairosvg`) |
| `preview` | alpha row + tinted on dark + tinted on light (+ on the game's ground with `--ground`); data textures (noise, stripes) are shown as gray values | `preview out.png a.png b.png ... --tint 40C8FF --ground 4E8A3A` |

All shape commands take `--size`, `--seed`, and `--steps N` (posterize into N flat bands,
2–4 for cel looks). Posterized textures are rendered at 4× and downsampled, so the band
edges stay anti-aliased.

`star` outline: tips at `--outer` (default 0.9), valleys at `--inner` × outer.
`--sharp` shapes the rays: 3 with a low `--inner` = thin lens/leaf-shaped rays meeting at
the center (glints), 2 = classic sparkle with concave sides, 1 = straight-edged star,
0.6 with `--inner 0.55` = puffy flower/cartoon star.
`--rotation` turns it (0 = a tip points up), `--glow 0` removes the halo.

**Always run `preview` and look at the preview image before handing textures over.** Opening a
mask itself shows a blank white square (the RGB is white; the shape is only in alpha). In the
preview check the silhouette (alpha row), edges touching the border, mushy edges when you
wanted crisp ones, flipbook frames that barely change, and whether the shape still reads on
the light row.

## SVG for custom shapes

For runes, magic-circle glyphs, stylized flames and leaves, write SVG: white fills,
transparent background, shapes inside a 5% margin. Rasterize with `vfxtex.py svg`. If
`cairosvg` is unavailable and the project has Unity's Vector Graphics package, save the
`.svg` into Assets and import it as a texture. Otherwise build the shape from the generator
primitives.

## C# fallback (no Python)

Generate the PNG inside Unity with MCP for Unity's `execute_code`: build a `Texture2D`,
fill pixels with the same math as `vfxtex.py`, `EncodeToPNG()` and
`File.WriteAllBytes(path)`, then `AssetDatabase.ImportAsset(path)`. Keep it to one texture
per call.

## Import settings

After writing a PNG under `Assets/`, set its import settings with MCP for Unity
(`manage_texture` → `set_import_settings`) or by editing the `.meta` through the asset
tools:
- Masks: sRGB on, alpha source **from input**, **Alpha Is Transparency on**, wrap **Clamp**,
  mipmaps on (particles shrink; without mipmaps small ones shimmer).
- Noise/data: **sRGB off**, wrap **Repeat**, Alpha Is Transparency off, mipmaps on.
- `stripes` masks are data like noise: sRGB off; wrap **U Repeat** (u wraps around a
  dome) and **V Clamp** (the arches must not wrap from the top back to the base).
- `puff` carries shading in RGB: keep sRGB on; the material tint multiplies RGB, so the
  strokes stay dark whatever the tint.
- Flipbooks: wrap Clamp, mipmaps on; record the grid (e.g. 4x4) in the manifest for the
  Particle Artist.

Use the key names from the tool's own parameter description (for example MCP for Unity
takes `alpha_source: "from_input"`), not guessed ones, and confirm by reading the texture's
`.meta` afterwards: unknown keys can be ignored silently.
