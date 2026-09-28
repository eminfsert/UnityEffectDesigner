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
| `ring` | ring / shockwave band | `--radius 0.7 --width 0.12 --breaks 5 --gap 0.15` (broken, stylized) |
| `star` | N-point sparkle | `--points 4 --inner 0.08 --glow 0.5` |
| `streak` | elongated spark (for stretched billboards) | `--aspect 4` |
| `slash` | crescent arc for sword trails / swipes | `--arc 150 --width 0.18` |
| `noise` | tileable fractal value noise | `--cells 4 --octaves 5 --seed 7` |
| `smoke` | stylized puff flipbook (grow → erode) | `--frames 16 --grid 4` (use with `texture_sheet_animation.num_tiles_x/y` = 4) |
| `svg` | rasterize an SVG you wrote | `--svg shape.svg` (needs `cairosvg`) |
| `preview` | tinted contact sheet of textures | `preview out.png a.png b.png ...` |

All shape commands take `--size`, `--seed`, and `--steps N` (posterize into N flat bands,
2–4 for cel looks).

**Always run `preview` and look at the image before handing textures over.** Check for
shapes touching the texture edge (visible cut-off on particles), mushy edges when you
wanted crisp ones, and flipbook frames that barely change.

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
- Masks: sRGB on, **Alpha Is Transparency on**, wrap **Clamp**, mipmaps on.
- Noise/data: **sRGB off**, wrap **Repeat**, Alpha Is Transparency off.
- Flipbooks: wrap Clamp; record the grid (e.g. 4x4) in the manifest for the Particle Artist.
