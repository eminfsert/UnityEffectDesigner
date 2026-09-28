# 004: Verify toolkit 0.4.1 and plugin 0.4.1 (fixes from 003)

Commits: 0733ad4 (toolkit code, vfxtex.py) and b46786a (skills/agents, `_deg` guard,
versions). The package and the plugin are both **0.4.1** now.

**Prerequisites (need the user's approval, they change the project / Claude Code setup):**
1. Update the Unity package to b46786a (Package Manager, or remove the
   `com.effectdesigner.vfxtoolkit` entry from `Packages/packages-lock.json` so the git
   dependency re-resolves). Reconnect the MCP client afterwards.
2. Update the plugin:
   ```
   claude plugin marketplace update unity-effect-designer
   claude plugin update vfx@unity-effect-designer
   ```
   then `/reload-plugins` (or restart). If the update does not pick up 0.4.1, that is a
   finding: say what the cache folder is called and which commands you ran.

Everything below writes only under `Assets/VFX/_Test004/` (plus captures in `Library/`).

## 1. Versions and compile

- Console clean after the package update (no compile errors from the toolkit)?
- Every tool result below should carry `"toolkitVersion": "0.4.1"`.
- The installed plugin is 0.4.1, and its `texture-authoring/scripts/vfxtex.py` has
  `star --sharp` (run `vfxtex.py star --help`).

## 2. Off-scene prefab build, overwrite guard, late-burst warning

Apply this recipe (no `target`, so it must be built in a preview scene):

```json
{
  "name": "VFX_Test004",
  "save_prefab": "Assets/VFX/_Test004/VFX_Test004.prefab",
  "systems": [
    {
      "name": "Flash",
      "main": { "loop": false, "duration": 0.2, "start_lifetime": 0.15, "start_speed": 0,
                "start_size": 1.5, "start_color": "#FFE8A0", "max_particles": 5 },
      "emission": { "rate_over_time": 0, "bursts": [ { "time": 0, "count": 1 } ] },
      "shape": { "enabled": false },
      "size_over_lifetime": { "size": { "ease": "spike", "from": 0, "to": 1 } },
      "renderer": {
        "material": {
          "path": "Assets/VFX/_Test004/M_Test004_Flash.mat",
          "shader": "EffectDesigner/Particles/Stylized Unlit",
          "blend": "additive",
          "properties": {
            "_BaseMap": "Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Star4.png",
            "_TintColor": { "color": "#FFB030", "intensity": 1 }
          }
        }
      }
    },
    {
      "name": "Sparks",
      "main": { "loop": false, "duration": 0.1, "start_lifetime": [0.3, 0.5], "start_speed": [4, 8],
                "start_size": [0.05, 0.1], "start_color": "#FFC247", "max_particles": 60 },
      "emission": { "rate_over_time": 0,
                    "bursts": [ { "time": 0.08, "count": 20 }, { "time": 0.095, "count": 20 } ] },
      "shape": { "shape_type": "sphere", "radius": 0.1 },
      "velocity_over_lifetime": { "speed_modifier": { "ease": "ease_out_expo", "from": 1, "to": 0.05 } },
      "size_over_lifetime": { "size": { "ease": "pop", "from": 0, "to": 1 } },
      "renderer": {
        "render_mode": "stretch", "velocity_scale": 0.05, "length_scale": 1,
        "material": {
          "path": "Assets/VFX/_Test004/M_Test004_Sparks.mat",
          "shader": "EffectDesigner/Particles/Stylized Unlit",
          "blend": "additive",
          "properties": {
            "_BaseMap": "Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_SoftGlow.png",
            "_TintColor": { "color": "#FFB030", "intensity": 0.5 }
          }
        }
      }
    }
  ]
}
```

Check:
- The open scene is **not** dirtied and has no new `VFX_Test004` object; the prefab exists
  with children Flash, Sparks; `root` and `prefab` in the result are the prefab path.
- `warnings` has one late-burst warning for Sparks' burst at 0.095 (duration 0.1), and none
  for 0.08.
- Send the same recipe again unchanged: it must fail with the "already exists … overwrite"
  error and change nothing. Then send it with `"overwrite": true` added at the top level:
  it must succeed, and the prefab's GUID (its `.meta`) must be unchanged.

## 3. Validation: `_deg` guard and `order`

Dry run, must return an error naming `angle` as already in degrees, and change nothing:

```json
{ "target": "Assets/VFX/_Test004/VFX_Test004.prefab", "dry_run": true,
  "systems": [ { "name": "Sparks", "shape": { "shape_type": "cone", "angle_deg": 25 } } ] }
```

Then apply; Sparks must become the first child:

```json
{ "target": "Assets/VFX/_Test004/VFX_Test004.prefab",
  "systems": [ { "name": "Sparks", "order": 0 } ] }
```

## 4. Capture: new result fields and framing reuse

Capture `Assets/VFX/_Test004/VFX_Test004.prefab` with
`times [0, 0.017, 0.05, 0.1, 0.15, 0.2, 0.3, 0.45, 0.6]`, `views ["three_quarter", "side"]`,
`backgrounds ["dark", "light"]`, `label "test004_a"`.

- `colorStatsByBackground` has `dark` and `light`, 9 entries each; `colorStats` equals the
  `dark` list.
- `systemColorStats` has `Flash` and `Sparks`, 9 entries each. Flash alone should read gold
  (hue ~35–50), Sparks entries before its first burst (t < 0.08) should be empty (coverage 0, no render).
- Flash visible in the t = 0 column (spike now starts at 85%). Sparks: the second burst
  (0.095) should be missing from `systemParticleCounts` (max ~20 alive, not 40), which is
  what the warning predicted.
- `warnings` vs `notes`: expect a "Low contrast on 'light'" warning (additive on light);
  the downscaled-sheet message, if any, is now in `notes`.
- How long did it take compared with the same capture with `"system_color_stats": false`?

Capture again with `label "test004_b"` and `view_framing` = the first result's
`viewFraming` (pass the array as is). Both views must have exactly the same `viewFraming`
values as the first capture, a note must say the placement was reused, and the two contact
sheets should line up pixel for pixel.

## 5. A real effect

Capture the CoinPickup prefab from 003 with its manifest's capture block. Report
`systemColorStats` per layer (hue and saturation at the peak and at the end of each layer's
window) and whether they match what the 003 critic concluded by eye.

## 6. Texture generator

With the installed plugin's `vfxtex.py`:
`star <scratch>/a.png`, `star <scratch>/b.png --inner 0.12 --sharp 3 --glow 0`,
`star <scratch>/c.png --points 6 --inner 0.55 --sharp 0.6 --steps 3`, then
`preview <scratch>/p.png <scratch>/a.png <scratch>/b.png <scratch>/c.png --tint 40C8FF`.
Open the preview (three rows: alpha, on dark, on light) and say whether the shapes and the
posterized edges look right. Write these outside the repo and outside `Assets/` (or under
`Assets/VFX/_Test004/`).

## What to send back

Per section: pass/fail with the numbers asked for, and any wrong or missing instructions
(file and what should change). Delete `Assets/VFX/_Test004/` afterwards only if the user
agrees.
