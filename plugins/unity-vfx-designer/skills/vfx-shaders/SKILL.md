---
name: vfx-shaders
description: Writing and checking URP effect shaders for the Effect Designer — the VFXCore.hlsl library, the ready-made "EffectDesigner/Particles/Stylized Unlit" shader and its vertex-stream contract, inline materials in particle recipes, HDR/bloom rules, and the vfx_compile_report loop. Load before writing or changing any shader or material for an effect.
user-invocable: false
---

# VFX shaders (Unity 6 URP)

## Start from what exists

1. **`EffectDesigner/Particles/Stylized Unlit`** (in the VFX Toolkit package) covers most
   particle layers: HDR tint, additive/alpha/premultiplied blending, noise erosion with a
   glowing edge, posterized (cel) alpha, soft particles. Use it before writing a new shader.
2. Write a new shader only when a layer needs something it cannot do (polar/scrolling
   masks on meshes, fresnel shells, distortion, vertex offset...). Build it on
   `VFXCore.hlsl`.

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
- **Glow with color, not with white.** The final color is tint × particle color × texture.
  A white HDR tint pushes every channel past 1, and tonemapping turns the palette white
  (and often yellow-green). This was measured on a gold effect: 83–100% of its bright pixels
  lost their color. Use a **saturated tint in the layer's hue** (gold `#FFB030`, not
  `#FFFFFF`) at **0.5–1.5 stops**, so one channel stays low. White belongs to a small
  core layer only. The capture's `colorStats.washedOut` and its "Washed out" warning
  catch this.
- For `[Toggle(KEYWORD)]` properties set both the float and the keyword.
- Several systems can share one material path. Define it fully once and reference it by
  path elsewhere.

## Calibration notes (measured in a real project, Neutral tonemapping)

Additive `Stylized Unlit` sparks, particle colors `#FFC247`→`#FFF4D6`:

| Tint | Dominant hue | Reads as |
|---|---|---|
| `#FFFFFF` @2 | washed out (77–100% colorless) | white/lemon |
| `#FFB030` @1 | 41–44° | gold / amber |
| `#FFB030` @0 ≈ `#FF8A1A` @0.5 | 32–36° | orange amber, "embers" |

- **Judge color only under the game's volume profile.** In that project the scene's
  ColorAdjustments (saturation +25, contrast +5) raised saturation from ~0.52 to ~0.8 and
  removed blue entirely. The same material looks different in and out of the game.
- Additive layers disappear on bright backgrounds (light-background captures:
  faint cream streaks). If the game has daylight or sand scenes, pair them with an
  alpha-blended darker companion layer.
- **These numbers are specific to that project and that shape.** A different mask size,
  blend mode, overlap density or volume profile moves them. Use the table for a first
  guess, then measure the layer's own `systemColorStats` and adjust.
- **When a channel hits the ceiling, intensity stops controlling hue.** Once the dominant
  channel clips, more intensity only raises the others, and the hue drifts toward
  yellow/white. Set the hue with the particle color or the tint's hue at moderate
  intensity (0–1 stops), and treat intensity as brightness only.
- **Rendered hue runs a few degrees yellower than the hex.** For a red-dominant color the
  hex hue is 60° × (G − B) / (R − B); bright additive layers measured 4–7° above it:
  `#FFB030` (37°) rendered 41–44°, an off-white core `#FFE8A0` (45°) rendered 51°, lemon
  rather than gold. For a gold that must read gold, pick hex hues around 32–40°; the
  paler the color (high B), the less saturation survives.

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
