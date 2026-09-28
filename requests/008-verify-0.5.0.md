# 008: Verify 0.5.0 (meshes, Stylized Shell, capture ground, new textures, vfxref)

Commit 42af4c9. Package and plugin are **0.5.0**. **007 is cancelled**: the user is happy
with CoinPickup; leave it as it is.

The user's next goal: recreate a stylized strong explosion from five reference screenshots
(a toon dome that cools white → yellow → peach → orange, breaks into strips, then orange
puffs + black ink strokes + dark smoke; ~1.5 s; ~3× character height). This request checks
the new building blocks first; 009 is the real `/vfx:create` run.

**Prerequisites (need the user's approval):** update the package and the plugin to 0.5.0,
wait for the recompile to finish (see `unity-adapter`), `/reload-plugins`.

Everything below writes only under `Assets/VFX/_Test008/`.

## 1. Meshes

Call `vfx_make_mesh` three times:
- `{"shape": "dome", "path": "Assets/VFX/_Test008/Meshes/SM_Test008_Dome.asset"}`
- `{"shape": "arc", "path": "Assets/VFX/_Test008/Meshes/SM_Test008_Arc.asset", "arc": 160, "width": 0.18}`
- `{"shape": "ring", "path": "Assets/VFX/_Test008/Meshes/SM_Test008_Ring.asset"}`

Report vertex/triangle counts and bounds (dome: center y 0.25, size 1 × 0.5 × 1). Call the
dome again unchanged: `created` false and the GUID in its `.meta` unchanged.

## 2. Shader

`vfx_compile_report` on `Packages/com.effectdesigner.vfxtoolkit/Shaders/StylizedShell.shader`
with `expected_properties`:
`{"_RampColor0": "HDRColor", "_RampColor1": "HDRColor", "_RampColor2": "HDRColor", "_RampColor3": "HDRColor", "_RampStops": "Vector", "_RampHard": "Range", "_RimColor": "HDRColor", "_BackTint": "Color", "_ErosionMap": "Texture", "_Erosion": "Range"}`.
It has never been compiled: send any errors verbatim (file and line).

## 3. Stripes texture

With the plugin's `vfxtex.py`:
`stripes Assets/VFX/_Test008/Textures/T_Test008_Stripes.png --bands 8 --size 256`, then
import it as data (sRGB off, wrap Repeat, alpha is transparency off). Also run
`swirl`, `shard`, `flame` and `puff` into `Assets/VFX/_Test008/Textures/` and
`preview` them all (tint `F2A15A`); open the preview and say whether they look usable.

## 4. A shell dome, on the ground

Apply this recipe:

```json
{
  "name": "VFX_Test008",
  "save_prefab": "Assets/VFX/_Test008/VFX_Test008.prefab",
  "systems": [
    {
      "name": "Dome",
      "main": { "loop": false, "duration": 0.6, "start_lifetime": 0.55, "start_speed": 0, "start_size": 3.5,
                "start_color": "#FFFFFF", "max_particles": 2, "simulation_space": "world" },
      "emission": { "rate_over_time": 0, "bursts": [ { "time": 0, "count": 1 } ] },
      "shape": { "enabled": false },
      "size_over_lifetime": { "size": { "ease": "ease_out_back", "from": 0.6, "to": 1 } },
      "custom_data": { "custom1": { "x": { "ease": "linear", "from": 0, "to": 1 },
                                    "y": [[0, 0], [0.45, 0], [0.85, 0.9], [1, 1]] } },
      "renderer": {
        "render_mode": "mesh",
        "mesh": "Assets/VFX/_Test008/Meshes/SM_Test008_Dome.asset",
        "alignment": "local",
        "vertex_streams": ["Position", "Normal", "Color", "UV", "Custom1XY"],
        "material": {
          "path": "Assets/VFX/_Test008/M_Test008_Dome.mat",
          "shader": "EffectDesigner/Particles/Stylized Shell",
          "blend": "alpha",
          "properties": {
            "_RampColor0": { "color": "#FFF6D8", "intensity": 0.5 },
            "_RampColor1": "#FFD86A",
            "_RampColor2": "#F7A860",
            "_RampColor3": "#E07A35",
            "_RampStops": [0.15, 0.35, 0.6, 0],
            "_RampHard": 1,
            "_RimColor": { "color": "#FF8A2A", "intensity": 0.5 },
            "_RimWidth": 0.3,
            "_RimSoftness": 0.05,
            "_BackTint": "#4A3A3AC0",
            "_ErosionMap": "Assets/VFX/_Test008/Textures/T_Test008_Stripes.png",
            "_EdgeWidth": 0.03,
            "_EdgeColor": "#FF7A2A"
          }
        }
      }
    }
  ]
}
```

Capture it: `times [0, 0.017, 0.05, 0.1, 0.15, 0.25, 0.35, 0.45, 0.55]`,
`views ["three_quarter", "side"]`, `backgrounds ["dark", "light"]`, `ground "#4E8A3A"`,
`label "test008_dome"`. Check and report:
- The dome stands on the green ground, upright (not turned toward the camera), about 3.5 m
  across, popping from 0.6 to full size in the first frames.
- Its fill steps white-yellow → yellow → peach → orange over the life (hard bands), with an
  orange rim on the silhouette.
- From ~0.25 s it breaks into vertical strips (gaps open first), and it is gone by the end.
- A note about the ground; `colorStatsByBackground` coverage is the dome only (the ground
  counts as background: coverage should be well below 100%).
- `systemColorStats.Dome` bright hue per time. Send the contact sheet path, and your honest
  look: does it read like the reference's dome (frames 1–2)? What is off?

If something does not work (upright, rim, strips, ground), try one targeted change and say
what it was, but do not start tuning the look: that is 009's job.

## 5. vfxref on the references

Ask the user to copy the five reference screenshots into
`Assets/VFX/StrongExplosion/Design/refs/` as `ref_01.png` … `ref_05.png` (in time order).
Then run:

```
vfxref.py sheet Assets/VFX/StrongExplosion/Design/refs/analysis \
  --inputs ref_01.png ref_02.png ref_03.png ref_04.png ref_05.png \
  --crop 0.215,0.173,0.54,0.585 --times 0,0.2,0.45,0.8,1.2 \
  --ignore 0.84,0.75,1,1 --exclude-hue 190-250
```

(paths relative to the refs folder; the crop assumes the same 2556×1179 screenshots). The
development session measured, per frame, white / bright / ink shares and bright hue:
`7/44/1 h42 · 4/37/0 h33 · 2/20/13 h31 · 0/1/27 h32 · 0/2/16 h35`. Report yours (they
should match within a few points), and open `reference_sheet.png`: in the "measured" row
the dome must be fully lit and the grass dimmed.

Then run `vfxref.py compare` of this reference against the `test008_dome` capture
(`--view three_quarter --background-name light` and `--background-name dark`): it
should run, warn on the dark one (ink vs dark background), and give numbers; they will
not match (a single dome is not the explosion). This only checks the tool.

## What to send back

Pass/fail per section with the numbers, compile errors verbatim, the contact sheet paths,
and anything wrong or missing in the instructions. Keep `Assets/VFX/_Test008/` until the
user decides.
