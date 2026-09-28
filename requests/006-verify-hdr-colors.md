# 006: Verify [HDR] material colors (0.4.3) and re-measure CoinPickup

Commit 9bc5584. Package and plugin are **0.4.3**. Fixes the 005 finding: `[HDR]` color
properties now receive the linear value (sRGB→linear of the hex, × 2^intensity); plain
Color properties still get the hex.

**Prerequisites (need the user's approval):** update the Unity package and the plugin to
0.4.3 as before; `/reload-plugins`.

## 1. The conversion itself

Apply to the `_Test004` prefab (inline materials are rewritten):

```json
{
  "target": "Assets/VFX/_Test004/VFX_Test004.prefab",
  "systems": [
    {
      "name": "Flash",
      "renderer": {
        "material": {
          "path": "Assets/VFX/_Test004/M_Test004_Flash.mat",
          "shader": "EffectDesigner/Particles/Stylized Unlit",
          "blend": "alpha",
          "properties": {
            "_BaseMap": "Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Star4.png",
            "_TintColor": { "color": "#5A3000", "intensity": 0 }
          }
        }
      },
      "main": { "start_color": "#FFFFFF" }
    }
  ]
}
```

- `material.GetColor("_TintColor")` ≈ (0.102, 0.030, 0, 1).
- Capture with `post_processing: false`, `backgrounds ["dark"]`, `system_frames: true`:
  Flash alone, fully opaque pixels render ≈ (90, 48, 0) (was 160, 120, 0).
- The Inspector's HDR color field shows `#5A3000` (intensity 0).
- `postProcessing.tonemapping` reads "off (post_processing false)" and a note says so.
- Then set `"_TintColor": { "color": "#FFFFFF", "intensity": 1 }`: GetColor ≈ (2, 2, 2).

## 2. Clamp note with peak size

Same capture as 005 test005_a: the Flash note now also gives the distance for its peak
size from the settings (~1.49 m → ~4.7 m) next to the sampled one (~3.9 m).

## 3. CoinPickup after the fix

Re-apply CoinPickup's inline materials unchanged (its last recipes in `Design/recipes/`,
material parts only, or the full last recipe as a patch). This is within `Assets/VFX/**`,
but it changes the look of an approved effect, so ask the user first.

Then capture with the manifest's block and `system_frames: true` and report per layer
(hue, saturation, washedOut at peak and end), especially:
- DarkRing and DarkStar alone: are they dark now (value well below the main layers)?
- Ring alone vs composite hue (the 005 gap came from the mustard DarkRing).
- Readability on light and sand: coverage vs dark, any "Low contrast" warning.
- Your honest look: better or worse than before, and what the critic would now change.

Do not tune CoinPickup further in this request; just measure and report.

## What to send back

Pass/fail with the numbers, and the CoinPickup table before (004/005) → after.
