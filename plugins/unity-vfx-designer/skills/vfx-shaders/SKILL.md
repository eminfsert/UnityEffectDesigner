---
name: vfx-shaders
description: Writing and checking URP effect shaders for the Effect Designer — the VFXCore.hlsl library, the ready-made "EffectDesigner/Particles/Stylized Unlit" and "Stylized Shell" shaders and their vertex-stream contracts, inline materials in particle recipes, HDR/bloom rules, and the vfx_compile_report loop. Load before writing or changing any shader or material for an effect.
user-invocable: false
---

# VFX shaders (Unity 6 URP)

## Start from what exists

1. **`EffectDesigner/Particles/Stylized Unlit`** (in the VFX Toolkit package) covers most
   particle layers: HDR tint, additive/alpha/premultiplied blending, noise erosion with a
   glowing edge, posterized (cel) alpha, soft particles. Use it before writing a new shader.
2. **`EffectDesigner/Particles/Stylized Shell`** is for mesh particles (domes, spheres,
   shockwave rings and walls, arc slashes from `vfx_make_mesh`): a flat toon fill that steps
   through a 4-color ramp over the particle's life, a hard rim on the silhouette, darker back
   faces, and a dissolve that can break the shell into strips.
3. Write a new shader only when a layer needs something neither can do (polar/scrolling
   masks, distortion, vertex offset...). Build it on `VFXCore.hlsl`.

## Stylized Unlit: properties and contract

| Property | Type | Use |
|---|---|---|
| `_BaseMap` | Texture | Shape/color texture (RGBA). Usually a grayscale mask from the Texture Artist |
| `_TintColor` | HDR Color | Color × intensity. **This is where glow comes from** (bloom threshold) |
| `_NoiseMap`, `_NoiseScroll` | Texture, Vector | Erosion noise (R) and its scroll speed (xy) |
| `_Erosion` | Range 0–1 | Base erosion; per-particle erosion adds from Custom1.x |
| `_Softness` | Range 0–0.5 | 0 = hard, cel-style dissolve edge |
| `_EdgeWidth`, `_EdgeColor` | Range, HDR Color | Burning rim during erosion |
| `_PosterizeSteps` | Range 0–8 | Flat alpha bands (2–4 for toon looks; 0 = off) |
| `_SoftParticles` + keyword `_SOFTPARTICLES_ON`, `_SoftParticleDistance` | Toggle, Float | Fade at geometry intersections (needs URP Depth Texture) |
| `_SrcBlend`, `_DstBlend` | Enum | Set through the material `blend` preset |

Vertex streams when using per-particle erosion:
`"vertex_streams": ["Position", "Color", "UV", "Custom1X"]` with
`"custom_data": {"custom1": {"x": {"ease": "ease_in_quad", "from": 0, "to": 1}}}`:
the particle dissolves over its life.

## Stylized Shell: properties and contract

| Property | Type | Use |
|---|---|---|
| `_RampColor0`..`_RampColor3` | HDR Color | Color sequence over the ramp position: hot → cooled (e.g. near-white, yellow, peach, orange) |
| `_RampStops` | Vector | Where colors 1, 2, 3 start (x, y, z in 0–1, ascending) |
| `_RampHard` | Range 0–1 | 1 = flat cel bands (each segment holds its color), 0 = smooth blend |
| `_RampOffset` | Range 0–1 | Ramp position without custom data (static color) |
| `_Opacity` | Range 0–1 | Fill opacity; below 1 the inside shows through (translucent shell) |
| `_RimColor` | HDR Color | Silhouette rim color; its alpha is the rim's opacity (a rim can stay opaque on a translucent fill) |
| `_RimWidth`, `_RimSoftness` | Range | Rim band width (0–1 of the silhouette falloff) and edge hardness (small = toon) |
| `_BackTint` | Color | Multiplies back faces (the inside of a dome): dark and a little transparent reads as a hollow shell |
| `_ErosionMap`, `_ErosionScroll` | Texture, Vector | Dissolve mask (R) on the mesh UVs; a `vfxtex stripes` mask breaks a dome into vertical strips |
| `_Erosion`, `_Softness`, `_EdgeWidth`, `_EdgeColor` | | As in Stylized Unlit; per-particle erosion adds Custom1.y. At erosion 0 nothing is cut and no edge is drawn (both shaders) |
| `_SrcBlend`, `_DstBlend`, `_ZWrite` | Enum | `blend` preset. The shader draws the inside (back faces) in a first pass and the outside in a second, so a translucent shell sorts itself. Keep ZWrite off unless the shell must hide what is behind it; eroded-away pixels never write depth |

Arc meshes have normals tilted toward both edges, so the rim traces the blade's outline
seen face on; for a flat ink blade set `_RimWidth` 0 and `_BackTint` white (both sides the
same).

Vertex streams: `["Position", "Normal", "Color", "UV", "Custom1XY"]` (Normal is required
for the rim), with custom data driving the look over life:

```json
"custom_data": { "custom1": {
  "x": { "ease": "linear", "from": 0, "to": 1 },
  "y": [[0, 0], [0.45, 0], [0.8, 0.9], [1, 1]] } }
```

x = ramp position (the color sequence), y = erosion (strips opening, then the shell gone).
The ramp colors are the palette's heat sequence; particle color stays white (see
`particle-recipes`: the hue lives in one place).

## Materials inside a particle recipe

`renderer.material` (and `trail_material`) accept an inline object; the material asset is
created, or patched if it exists:

```json
"renderer": {
  "render_mode": "stretch",
  "material": {
    "path": "Assets/VFX/ArcaneNova/Materials/M_Sparks.mat",
    "shader": "EffectDesigner/Particles/Stylized Unlit",
    "blend": "additive",
    "properties": {
      "_BaseMap": "Assets/VFX/ArcaneNova/Textures/T_Spark.png",
      "_TintColor": { "color": "#FFC247", "intensity": 2.5 },
      "_PosterizeSteps": 3
    },
    "keywords": { "_SOFTPARTICLES_ON": false }
  }
}
```

- `blend`: `additive`, `alpha`, `premultiplied`, `multiply`, `soft_additive`.
- Property names are the shader's (`_TintColor`), with "did you mean" suggestions if wrong.
- **HDR belongs here.** Material colors keep their intensity. `intensity` is in stops of
  **linear light**, like Unity's HDR color picker: +1 doubles what the GPU sees, 2.5 → ×5.7.
  Particle colors are 8-bit and only carry hue and alpha variation.
- **Textures with tiling or offset:** `"_NoiseMap": {"texture": "Assets/...png", "tiling": [2, 1], "offset": [0, 0.5]}`.
- **Always give hex colors; the tool handles color spaces.** In a Linear project Unity
  linearizes plain Color properties itself but passes `[HDR]` Color properties to the GPU
  as stored, so the tool (≥ 0.4.3) writes the linear value into `[HDR]` properties. Do not
  set `[HDR]` colors with `manage_material` or `execute_code` using a hex's 0–1 values:
  they would render lighter and yellower (`#5A3000` renders as `#A07800`). Toolkit
  ≤ 0.4.2 had exactly that bug: re-apply the inline materials of effects built with it.
- **Glow with color, not with white.** The final color is tint × particle color × texture.
  A white HDR tint pushes every channel past 1, and tonemapping turns the palette white
  (and often yellow-green). This was measured on a gold effect: 83–100% of its bright pixels
  lost their color (measured in the 8-bit captures before 0.5.4, which clipped harder than the
  game; the principle holds). Use a **saturated tint in the layer's hue** (gold `#FFB030`, not
  `#FFFFFF`) at **0.5–1.5 stops**, so one channel stays low. White belongs to a small
  core layer only. The capture's `colorStats.washedOut` and its "Washed out" warning
  catch this.
- For `[Toggle(KEYWORD)]` properties set both the float and the keyword.
- Several systems can share one material path. Define it fully once and reference it by
  path elsewhere.

## Color notes (measured in a real project, Neutral tonemapping)

Hue numbers measured before toolkit 0.4.3 were taken with over-bright `[HDR]` tints (see
above) and have been removed; re-measure with `systemColorStats` rather than relying on a
table.

- **Judge color only under the game's volume profile.** In that project the scene's
  ColorAdjustments (saturation +25, contrast +5) raised saturation from ~0.52 to ~0.8 and
  removed blue entirely: a saturated orange lost its blue channel and measured saturation
  1.0. Predict with a capture, not from the hex. The same material looks different in and out of the game.
- Additive layers disappear on bright backgrounds (light-background captures:
  faint cream streaks). If the game has daylight or sand scenes, pair them with an
  alpha-blended darker companion layer, and check it alone (`systemColorStats`,
  `system_frames`): a dark companion must measure dark.
- **High intensity drifts toward white.** Tonemapping compresses the brightest channel
  first, so as intensity rises the others catch up and the hue drifts toward yellow/white
  (it does not hard-clip in the game: the scene renders HDR). Set the hue with the tint's
  hue (particle color white) at moderate intensity (0–1 stops), and treat intensity as
  brightness and bloom. Captures before toolkit 0.5.4 rendered in 8 bits and clipped every
  channel at 1 *before* tonemapping, which exaggerated this: re-measure older numbers.
- Tint and particle color multiply in linear light, so two mid-saturated colors multiply
  into a more saturated, darker one (`#FFB030` × `#FFB030` is deep orange). Keep one of
  them near white when the other carries the hue.
- The hex hue of a red-dominant color is 60° × (G − B) / (R − B). Layers overlapping
  (a companion underneath, bloom from neighbours) shift the composite's hue; judge each
  layer alone first.

## Writing a new shader

```hlsl
Shader "EffectDesigner/Effects/<EffectId>_<Layer>"
{
    Properties { /* names from manifest.yaml; HDR colors marked [HDR] */ }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)   // every material property except textures, for the SRP Batcher
                ...
            CBUFFER_END
            #include "Packages/com.effectdesigner.vfxtoolkit/Shaders/VFXCore.hlsl"
            ...
            ENDHLSL
        }
    }
}
```

`VFXCore.hlsl`:

| Function | Purpose |
|---|---|
| `VFXTime()` | Seconds; equals the capture time during `vfx_capture_timeline`. **Use it instead of `_Time.y`** or captures of scrolling/pulsing shaders are not reproducible |
| `VFXScrollUV(uv, speed)` | Panning UVs |
| `VFXPolarUV(uv, center)` | (angle 0–1, radius) for rings, swirls, shockwaves |
| `VFXErode(mask, erosion, softness, edgeWidth, out edge)` | Dissolve with a rim |
| `VFXPosterize(value, steps)` | Flat bands for cel looks |
| `VFXSoftParticle(screenPos, distance)` | Depth fade (Depth Texture required) |
| `VFXFresnel(normalWS, viewDirWS, power)` | Rim for mesh shells |

Rules:
- Keep the property names and types the Systems Architect fixed in `manifest.yaml`.
- Distortion/refraction samples `_CameraOpaqueTexture` (URP asset: Opaque Texture on).
  Soft particles need Depth Texture on.
- Keep keywords few (`shader_feature_local`).

## Check every shader: `vfx_compile_report`

After **every** shader write, call:

```json
{ "paths": ["Assets/VFX/ArcaneNova/Shaders"],
  "expected_properties": { "_BaseMap": "Texture", "_TintColor": "HDRColor", "_Erosion": "Float" } }
```

- `paths`: shader files, folders, or an `.hlsl` include (every shader that includes it is
  checked).
- Errors come with `file` and `line`. Fix them all and call it again.
- `expected_properties` checks the manifest contract. Types: `Color`, `HDRColor`, `Float`
  (also accepts Range), `Range`, `Int`, `Vector`, `Texture`. Missing properties and type
  mismatches are listed.
- A shader is done only when `shadersWithErrors` and `contractViolations` are both 0 and
  a `vfx_capture_timeline` of an effect using it looks right.
