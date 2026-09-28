---
name: unity-adapter
description: How the Effect Designer agents talk to the Unity Editor through MCP for Unity (CoplayDev/unity-mcp) and the VFX Toolkit custom tools. Load before any Unity call in a VFX task — building particle systems or VFX Graphs, writing shaders or materials, importing textures, or capturing/reviewing an effect with vfx_capture_timeline.
---

# Unity adapter

The plugin does not ship its own MCP server. Everything in Unity goes through the
user's **MCP for Unity** server (CoplayDev/unity-mcp) plus the custom tools that the
**Effect Designer VFX Toolkit** Unity package registers into it.

## 1. Session preflight (once per task)

1. Find the MCP for Unity tools. Their names look like `mcp__<server>__manage_scene`;
   the server name depends on the user's setup (often `UnityMCP`). If no such tools
   exist, stop and tell the user to start Unity and connect MCP for Unity.
2. Activate the VFX tool group, which is hidden by default:
   `manage_tools(action="activate", group="vfx")`.
3. Check that the toolkit is installed: `vfx_capture_timeline` is listed as a tool,
   or appears in the project's custom tools (resource `mcpforunity://custom-tools`).
   If it is missing, ask the user to add the package:
   `https://github.com/eminfsert/UnityEffectDesigner.git?path=unity-package/com.effectdesigner.vfxtoolkit`
   and reconnect the MCP client.
4. Read the console once (`read_console`) so later errors are not confused with
   pre-existing ones.

## 2. Calling toolkit tools

Toolkit tools are custom tools. Depending on the server configuration they are
exposed either directly (`mcp__<server>__vfx_capture_timeline`) or only through
`execute_custom_tool`. Prefer the direct tool; otherwise call:

```
execute_custom_tool(tool_name="vfx_capture_timeline", parameters={...})
```

Parameter names are snake_case exactly as documented below.

**Stale schema after a package update.** MCP for Unity caches a custom tool's schema when
the tool is first registered. After the toolkit package is updated, the direct tool can
reject new parameters with `Unexpected keyword argument` until the MCP client reconnects.
This does **not** mean the parameter is unsupported. Retry the same call through
`execute_custom_tool`, which does not validate against the cached schema, and tell the user
to reconnect the MCP client.

**Reading toolkit results.** Prefer the tool response (structured JSON) over the console.
If you must read a menu action's log with `read_console`, note that multi-line entries are
cut after the first lines (the rest lands in the stack trace). The toolkit's menus
therefore log one line per entry, culture-invariant, all prefixed `[VFX Toolkit]`.

## 3. Which tool for which job

| Job | Tool |
|---|---|
| Scene / GameObject / prefab / asset | `manage_scene`, `manage_gameobject`, `manage_prefabs`, `manage_asset` |
| **Build or change Shuriken systems (all modules, sub-emitters, custom data, vertex streams, prefab save)** | **`vfx_apply_particle_recipe`** (toolkit), see the `particle-recipes` skill |
| Inspect or play a single Shuriken system | `manage_vfx` with `particle_*` actions (`particle_get_info`, `particle_play`) |
| VFX Graph from template + exposed properties, events, seed | `manage_vfx` with `vfx_*` actions |
| Line / trail renderers | `manage_vfx` with `line_*` / `trail_*` actions |
| Shader files | write with `manage_shader` or the file tools, then **`vfx_compile_report`** (toolkit), see the `vfx-shaders` skill |
| Materials | inline in particle recipes (`renderer.material` object), or `manage_material` |
| Simple textures, import settings | `manage_texture` |
| URP asset, renderer features, volumes | `manage_graphics` |
| **See the effect over time** | **`vfx_capture_timeline`** (toolkit) |
| Anything missing | Prototype with `execute_code` (C# method body); if used repeatedly, it belongs in the toolkit as a custom tool |

## 4. `vfx_capture_timeline`

Renders the effect in an isolated preview scene at the requested times, deterministic
(fixed seeds, played frame by frame from t = 0). Returns absolute paths.

| Parameter | Default | Notes |
|---|---|---|
| `target` | required | Prefab path (`Assets/...prefab`), scene path, name, or instance id |
| `times` | `[0,0.05,0.1,0.2,0.35,0.5,0.75,1,1.5]` | Seconds; 0 is the first frame (1/60 s). Put extra samples around the spec's beats (anticipation, impact, dissipation) and across the whole life of every layer, including sub-emitters, so nothing happens only between samples |
| `views` | `["three_quarter"]` | `front, back, side, top, three_quarter, low` or `{name, azimuth, elevation}` |
| `backgrounds` | `["dark"]` | `dark, mid, light` or hex. Use `["dark","light"]` for readability checks |
| `frame_size` | 320 | 64–1024 |
| `seed` | 1234 | Change it to check the effect is not relying on one lucky random roll |
| `auto_frame` | true | Each view is re-framed on the pixels the effect actually covers over all times (centred, ~80% of the frame) |
| `framing_radius` | auto | Fix it (meters) when comparing iterations, so scale changes are visible. Disables auto framing |
| `post_processing` | true | Uses the project's global volumes (bloom matters for stylized glow) |
| `volume_profile` | project setting | **The game scene's VolumeProfile** (`Assets/...asset`). Volumes in open scenes never reach captures; without a profile only the pipeline defaults (global + quality level) apply, and colors cannot be judged against the game (e.g. a scene's ColorAdjustments saturation +25 is missing). Set it once per project in `ProjectSettings/EffectDesigner.json` (`{"volume_profile": "Assets/...asset"}`) and every capture uses it; the parameter overrides it, and `"none"` renders with the pipeline defaults only (for before/after comparisons) |
| `label` | effect name | Name the iteration, e.g. `arcane_nova_iter2` |

**After every capture, open `contactSheet` with the Read tool and look at it.** Never
judge an effect from the numbers alone. Use `systemParticleCounts` (alive particles per system per time) to confirm every layer
and sub-emitter is active when the spec says it should be, `particleCounts` for totals,
`postProcessing` (tonemapping and bloom actually used; colors are only valid under the game's),
`colorStats` (per time, first view/background: `washedOut` = share of bright pixels that lost
their color, mean `saturation`, dominant `hue` in degrees) to judge color objectively,
`viewFraming` (look-at point and distance per view) to repeat the same framing later,
(`colorStats.coverage` is a share of the *frame*, and auto framing depends on the sampled
`times`, so coverage is only comparable between captures with the same times, or with a
fixed `framing_radius`; hue, saturation and washedOut do not depend on framing),
and `warnings` for anything that makes the frames unreliable.

The effect is played frame by frame at 60 fps from a seeded restart, like the game plays
it: t = 0 is the first frame, times are rounded to 1/60 s, sub-emitters fire.

Limits: at most 24 times and 192 frames per call. VFX Graph stepping in edit mode is
experimental. If VFX Graph frames look empty or identical, say so and verify in Play
Mode rather than guessing.
