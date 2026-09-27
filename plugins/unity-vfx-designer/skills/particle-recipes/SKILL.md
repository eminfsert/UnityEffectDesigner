---
name: particle-recipes
description: Reference for building Shuriken particle systems with the vfx_apply_particle_recipe tool — recipe format, value notation (curves, easing, HDR colors, palette), module keys, sub-emitters, custom data and vertex streams, common pitfalls, and stylized VFX patterns. Load whenever you create or change a ParticleSystem for an effect.
---

# Particle recipes

`vfx_apply_particle_recipe` builds or updates a whole hierarchy of Shuriken particle
systems from one JSON recipe. It validates the recipe first: if anything is wrong,
**nothing changes** and every problem is returned at once, with "did you mean"
suggestions. Fix all of them and resend.

How to call it (direct tool vs `execute_custom_tool`) is in the `unity-adapter` skill.

## Workflow

1. Write the recipe from the effect spec: one system per layer (flash, sparks, smoke...).
2. Call it with `"dry_run": true` first when the recipe is large or new.
3. Apply for real. Use `save_prefab` for a new effect, or `target` = the prefab path to
   iterate on an existing one.
4. Capture it with `vfx_capture_timeline` and look at the contact sheet.
5. Iterate with **small patch recipes**: send only the systems and keys that change.

## Top level

| Key | Meaning |
|---|---|
| `systems` | Required. Array of system definitions (below) |
| `target` | Existing prefab (`Assets/...prefab`, edited in place) or scene object to update. Omit to create a new root |
| `name` | Name of the new root when `target` is omitted |
| `save_prefab` | Save the scene root as a prefab here (folders are created) |
| `palette` | Named colors usable as `"$name"` anywhere a color is expected |
| `dry_run` | Validate and report only |

## System

```json
{ "name": "Sparks", "parent": "Core", "position": [0, 0.5, 0], "rotation": [-90, 0, 0],
  "reset": false, "main": { ... }, "emission": { ... }, "renderer": { ... } }
```

- `name` is unique in the recipe and is the GameObject name. A system whose name equals
  the root's name (and has no `parent`) lives on the root itself.
- `parent` is another system in the recipe. Sub-emitters should be children of the system
  that triggers them.
- Updates are **patches**: only keys you send change. `"reset": true` rebuilds the system
  from Unity defaults first. Use it when an iteration changes the system's concept, so
  stale settings from the last attempt don't linger.
- New systems start from Unity defaults (looping, 5 s, 10/s, cone). Always set
  `main.loop`, `main.duration`, `main.start_*` and `emission` explicitly.

## Keys = Unity property names in snake_case

Modules: `main`, `emission`, `shape`, `velocity_over_lifetime`,
`limit_velocity_over_lifetime`, `inherit_velocity`, `lifetime_by_emitter_speed`,
`force_over_lifetime`, `color_over_lifetime`, `color_by_speed`, `size_over_lifetime`,
`size_by_speed`, `rotation_over_lifetime`, `rotation_by_speed`, `external_forces`,
`noise`, `collision`, `sub_emitters`, `texture_sheet_animation`, `lights`, `trails`,
`custom_data`, plus `renderer`.

Inside a module, any public property of the Unity module works by its name in snake_case
(`start_lifetime`, `shape_type`, `num_tiles_x`, `velocity_scale`...). Sending a module
enables it unless you pass `"enabled": false`.

Frequently used:

| Module | Keys |
|---|---|
| `main` | `duration`, `loop` (`looping` also works), `start_delay`, `start_lifetime`, `start_speed`, `start_size` (or `start_size3d` + `start_size_x/y/z`), `start_rotation_deg`, `start_color`, `gravity_modifier`, `simulation_space` (local/world), `scaling_mode`, `max_particles`, `play_on_awake`, `stop_action` |
| `emission` | `rate_over_time`, `rate_over_distance`, `bursts: [{time, count, cycles, interval, probability}]` |
| `shape` | `shape_type` (sphere, hemisphere, cone, circle, edge, box, donut, mesh...), `radius`, `radius_thickness`, `angle`, `arc`, `arc_mode`, `length`, `position`, `rotation`, `scale`, `align_to_direction`, `random_direction_amount` |
| `velocity_over_lifetime` | `x`, `y`, `z`, `space`, `orbital_x/y/z`, `radial`, `speed_modifier` |
| `limit_velocity_over_lifetime` | `limit`, `dampen`, `drag` |
| `size_over_lifetime` / `color_over_lifetime` | `size` / `color` |
| `rotation_over_lifetime` | `z_deg` (or `x_deg`, `y_deg` with `separate_axes`) |
| `noise` | `strength`, `frequency`, `scroll_speed`, `damping`, `octave_count`, `quality` |
| `texture_sheet_animation` | `num_tiles_x`, `num_tiles_y`, `frame_over_time`, `start_frame`, `cycle_count`, `sprites: [paths]` |
| `trails` | `ratio`, `lifetime`, `width_over_trail`, `color_over_trail`, `inherit_particle_color`, `mode` |
| `renderer` | `render_mode` (billboard, stretch, horizontal_billboard, vertical_billboard, mesh), `material`, `trail_material`, `mesh`, `velocity_scale`, `length_scale`, `sort_mode`, `sorting_fudge`, `alignment`, `vertex_streams` |

## Value notation

**Numbers and curves** (MinMaxCurve):

| Write | Means |
|---|---|
| `1.5` | constant |
| `[0.4, 0.8]` | random between two constants |
| `[[0, 1], [0.7, 0.8], [1, 0]]` | curve keys `[time, value]` (smooth tangents added) |
| `{"ease": "ease_out_expo", "from": 1, "to": 0}` | named curve over the particle's life |
| `{"curve": [...], "multiplier": 2}` | curve scaled by a multiplier |
| `{"curve_min": [...], "curve_max": [...]}` | random between two curves |

Eases: `linear`, `ease_in_quad`, `ease_out_quad`, `ease_in_out_quad`, `ease_in_cubic`,
`ease_out_cubic`, `ease_in_out_cubic`, `ease_in_expo`, `ease_out_expo`, `ease_in_back`,
`ease_out_back`, plus VFX shapes `spike` (instant peak, fast decay: flashes), `pop`
(overshoot to 1.2, settle, fade at the end) and `fade_in_out`.

**Angles:** Unity's API uses radians. Append `_deg` to any numeric or curve key to give
degrees: `"start_rotation_deg": [0, 360]`, `"z_deg": [-90, 90]`.

**Colors:** `"#FFC247"`, `"#FFC24780"` (with alpha), `"$accent"` (palette),
`{"color": "#9B5CFF", "intensity": 2}` (HDR, intensity in stops: x4 here), `[r, g, b, a]`.

**Particle colors are 8-bit.** Shuriken stores start color, color over lifetime and every
other particle color field as Color32, so HDR values are clipped per channel: an HDR
gold becomes plain white. The tool rescales HDR colors to full brightness with their hue
kept, and warns. Glow and bloom come from the **material**: an HDR base/emission
color, or a shader multiplier fed by `custom_data`. Keep palette HDR entries for
materials and use plain hex colors on particles.

**Color fields** (MinMaxGradient): a color; `["#a", "#b"]` (random between two);
`{"gradient": {"colors": ["$core", "$primary"], "alphas": [[0, 1], [1, 0]]}}`, where
colors may also be `[[t, color], ...]` (max 8 color and 8 alpha keys) and
`"mode": "fixed"` gives stepped, cel-style color changes; `{"gradient_min", "gradient_max"}`;
`{"random_color": {gradient}}`.

**Assets:** project paths, e.g. `"material": "Assets/VFX/ArcaneNova/Materials/M_Spark.mat"`.
**Enums:** case-insensitive, snake_case fine (`"world"`, `"horizontal_billboard"`).

## Sub-emitters

```json
"sub_emitters": [ { "system": "SparkPops", "type": "death", "inherit": ["color"], "probability": 0.5 } ]
```

`type`: birth, collision, death, trigger, manual. `inherit`: color, size, rotation,
lifetime, duration, everything. The sub-emitter system should have `parent` set to the
emitting system and emit through bursts (rate 0).

## Custom data and vertex streams (shader contract)

Per-particle data for the shader, e.g. dissolve progress over life:

```json
"custom_data": { "custom1": { "x": {"ease": "ease_in_quad", "from": 0, "to": 1}, "y": 2 } },
"renderer": { "vertex_streams": ["Position", "Color", "UV", "Custom1XY"] }
```

The stream order defines the shader's TEXCOORD layout. It **must** match the
`manifest.yaml` contract that the Shader Artist implements, so do not reorder streams
without updating the shader. Stream names are Unity's `ParticleSystemVertexStream` values
(`UV2`, `AgeFraction`, `Custom1XYZW`, `StableRandomX`, `Velocity`...).

## Pitfalls

- `main.loop`, not `looping` (both are accepted, but the name Unity uses is `loop`).
- Rotations in radians unless you use `_deg`.
- Stretched billboards need speed. With `velocity_scale` 0 and `length_scale` 1 they are
  static quads.
- A new system without `renderer.material` gets URP's ParticlesUnlit as a placeholder
  (the result warns). Give every layer its real material.
- World-space simulation (`simulation_space: world`) for anything that should trail
  behind a moving emitter; local for effects glued to their transform.
- `max_particles` caps bursts silently. Keep it above the largest burst.
- The response's `errors` list (after validation passed) means some properties failed in
  Unity and the rest were applied. Read them before capturing.

## Stylized patterns (starting points, tune after capturing)

- **Impact flash:** 1 particle, lifetime 0.1–0.2, `size_over_lifetime` `spike`, core color
  fading to the primary color, additive material with HDR intensity, `sorting_fudge`
  negative so it draws on top.
- **Sparks:** burst 20–50, speed [6, 14], `limit_velocity_over_lifetime.dampen` 0.1–0.2 for
  the "fast then hang" feel, stretch render mode, `gravity_modifier` 0.3–1, size ends at 0.
- **Toon smoke puff:** 3–8 particles, flipbook 4x4, `color_over_lifetime` with
  `"mode": "fixed"` (2–3 flat tones), `start_rotation_deg` [0, 360], slow upward velocity,
  `pop` size curve.
- **Embers:** rate 10–30, long life, `noise` strength 0.3–0.8 at low frequency, small
  sizes, brightness from an HDR material. Always check them on the light background.
- **Readability:** vary size (a few large "hero" particles among many small ones) and give
  additive layers a darker alpha-blended companion so the effect reads on bright scenes.
