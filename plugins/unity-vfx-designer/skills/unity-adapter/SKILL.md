---
name: unity-adapter
description: How the Effect Designer agents talk to the Unity Editor through MCP for Unity (CoplayDev/unity-mcp) and the VFX Toolkit custom tools. Load before any Unity call in a VFX task — building particle systems or VFX Graphs, writing shaders or materials, importing textures, or capturing/reviewing an effect with vfx_capture_timeline.
user-invocable: false
---

# Unity adapter

The plugin does not ship its own MCP server. Everything in Unity goes through the
user's **MCP for Unity** server (CoplayDev/unity-mcp) plus the custom tools that the
**Effect Designer VFX Toolkit** Unity package registers into it.

## 1. Session preflight (once per task)

The Director runs this once. Subagents it starts can assume it passed and skip it.

1. Find the MCP for Unity tools. Their names look like `mcp__<server>__manage_scene`;
   the server name depends on the user's setup (often `UnityMCP`). In Claude Code many MCP
   tools are **deferred**: only their names are listed until loaded. Load what you need
   with ToolSearch (e.g. `select:mcp__UnityMCP__manage_tools`, or a keyword search for
   `vfx_capture`) before calling. If no MCP for Unity tools exist at all, stop and tell
   the user to start Unity and connect MCP for Unity.
2. Activate the VFX tool group, which is hidden by default:
   `manage_tools(action="activate", group="vfx")`.
3. Check that the toolkit is installed: `vfx_capture_timeline` is listed as a tool (maybe
   deferred), or appears in the project's custom tools (resource
   `mcpforunity://custom-tools`; that resource is not readable in every client, so a
   failed read proves nothing). If still unsure, call `vfx_compile_report` through
   `execute_custom_tool` with `{"paths": ["Packages/com.effectdesigner.vfxtoolkit/Shaders"]}`:
   it is harmless and returns `toolkitVersion`. If the toolkit is missing, ask the user to
   add the package in Package Manager (*Add package from git URL*):
   `https://github.com/eminfsert/UnityEffectDesigner.git?path=unity-package/com.effectdesigner.vfxtoolkit#claude/trusting-ritchie-uze78h`
   and reconnect the MCP client.
4. **Check the version.** Every toolkit result has `toolkitVersion`. This plugin version
   needs **toolkit ≥ 0.5.4** (HDR captures like the game camera; before 0.5.4 captures
   clipped HDR colors at 1 before post-processing, so their color numbers are not
   comparable), `valueContrast`, the two-pass shell, `vfx_make_mesh`, the Stylized Shell shader, capture `ground`, `colorStatsByBackground`, `systemColorStats`,
   `view_framing`, `system_frames`, off-scene `save_prefab`, same-frame time merging, and
   correct `[HDR]` material colors: effects built with ≤ 0.4.2 have too-light tints and
   need their inline materials re-applied).
   Error responses carry it too (in their data). A missing field means an older toolkit: tell
   the user to update the package (Package Manager → the package → Update, or remove the
   `com.effectdesigner.vfxtoolkit` entry from `Packages/packages-lock.json` so the git
   dependency re-resolves), and work around the missing features until then.
5. Read the console once (`read_console`) so later errors are not confused with
   pre-existing ones.

## 2. Calling toolkit tools

Toolkit tools are custom tools. Depending on the server configuration they are
exposed either directly (`mcp__<server>__vfx_capture_timeline`) or only through
`execute_custom_tool`. Prefer the direct tool; otherwise call:

```
execute_custom_tool(tool_name="vfx_capture_timeline", parameters={...})
```

Parameter names are snake_case exactly as documented below.

**Right after a package update Unity recompiles and reloads.** Wait until
`EditorApplication.isCompiling` and `EditorApplication.isUpdating` are both false (poll
with `execute_code` or check the editor state resource) before the next toolkit call. A
call that returns "disconnected while awaiting command_result" may or may not have been
applied: read back what it should have changed, then resend it. Recipes and material
writes are idempotent, so resending is safe.

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
| **Meshes for mesh particles (dome, sphere, ring, cylinder/cone, arc slash)** | **`vfx_make_mesh`** (toolkit): `{shape, path, radius, ...}`; rebuilding a path keeps references. UVs: u around/along, v across |
| Inspect or play a single Shuriken system | `manage_vfx` with `particle_*` actions (`particle_get_info`, `particle_play`) |
| VFX Graph from template + exposed properties, events, seed | `manage_vfx` with `vfx_*` actions |
| Line / trail renderers | `manage_vfx` with `line_*` / `trail_*` actions |
| Shader files | write with `manage_shader` or the file tools, then **`vfx_compile_report`** (toolkit), see the `vfx-shaders` skill |
| Materials | inline in particle recipes (`renderer.material` object), or `manage_material` |
| Simple textures, import settings | `manage_texture` |
| URP asset, renderer features, volumes | `manage_graphics` |
| **See the effect over time** | **`vfx_capture_timeline`** (toolkit) |
| Anything missing | Prototype with `execute_code` (C# method body); if used repeatedly, it belongs in the toolkit as a custom tool |

`execute_code` limits seen in practice: `Object` is ambiguous (write `UnityEngine.Object`,
and full type names whenever a name could clash), and
`AssetDatabase.DeleteAsset` is blocked: delete assets with `manage_asset` (`delete`).
Never use it to change game code, scenes or project settings outside the effect's folder.

## 4. `vfx_capture_timeline`

Renders the effect in an isolated preview scene at the requested times, deterministic
(fixed seeds, played frame by frame from t = 0). Returns absolute paths.

| Parameter | Default | Notes |
|---|---|---|
| `target` | required | Prefab path (`Assets/...prefab`), scene path, name, or instance id |
| `times` | `[0,0.05,0.1,0.2,0.35,0.5,0.75,1,1.5]` | Seconds after the first frame: 0 is the first rendered frame, 0.017 the second, each 1/60 s one more. Times that round to the same frame are captured once (a note lists them), so space samples ≥ 0.017 s apart: "beat + 1 frame" is beat + 0.017, "+ 2 frames" beat + 0.034. Put extra samples around the spec's beats (anticipation, impact, dissipation) and across the whole life of every layer, including sub-emitters, so nothing happens only between samples |
| `views` | `["three_quarter"]` | `front, back, side, top, three_quarter, low` or `{name, azimuth, elevation}` |
| `backgrounds` | `["dark"]` | `dark, mid, light` or hex. Use `["dark","light"]` for readability checks |
| `frame_size` | 320 | 64–1024 |
| `seed` | 1234 | Change it to check the effect is not relying on one lucky random roll |
| `auto_frame` | true | Each view is re-framed on the pixels the effect actually covers over all times (centred, ~80% of the frame) |
| `framing_radius` | auto | Fix it (meters) when comparing iterations, so scale changes are visible. Disables auto framing |
| `view_framing` | none | A previous result's `viewFraming` (`[{view, lookAt, distance}]`): those views reuse that camera placement exactly and skip auto framing. Use it every round after the first so iterations line up |
| `system_color_stats` | true | Also renders each system alone (first view, first background) for `systemColorStats`. One extra render per system per time (~10% capture time for two systems); turn off for quick looks |
| `ground` | none | A ground plane under the effect in the game's ground color (hex), at the pivot (`ground_height` shifts it). Use it for anything that sits on the ground (domes, ground rings, dust); it counts as background in the color stats, so readability is measured against the real ground |
| `system_frames` | false | Also saves those system-alone renders as PNGs (`systemFrames`), to see why a layer alone and the composite measure differently |
| `post_processing` | true | Uses the project's global volumes (bloom matters for stylized glow) |
| `volume_profile` | project setting | **The game scene's VolumeProfile** (`Assets/...asset`). Volumes in open scenes never reach captures; without a profile only the pipeline defaults (global + quality level) apply, and colors cannot be judged against the game (e.g. a scene's ColorAdjustments saturation +25 is missing). Set it once per project in `ProjectSettings/EffectDesigner.json` (`{"volume_profile": "Assets/...asset"}`) and every capture uses it; the parameter overrides it, and `"none"` renders with the pipeline defaults only (for before/after comparisons) |
| `label` | effect name | Name the iteration, e.g. `arcane_nova_iter2` |
| `output_folder` | `Library/VFXToolkit/Captures` | Keep the default: Library is neither imported nor versioned. Reviews reference the absolute contact sheet path. If captures must sit next to the effect, use `Assets/VFX/<Id>/Design/captures~` (Unity ignores folders ending in `~`) and tell the user to add it to `.gitignore` if they do not want images in version control |

**After every capture, open `contactSheet` with the Read tool and look at it.** Never
judge an effect from the numbers alone. Use `systemParticleCounts` (alive particles per system per time) to confirm every layer
and sub-emitter is active when the spec says it should be, `particleCounts` for totals,
`postProcessing` (tonemapping and bloom actually used; colors are only valid under the game's),
`colorStats` (per time, first view/background: `washedOut` = share of bright pixels that lost
their color, mean `saturation`, dominant `hue` in degrees) to judge color objectively,
`colorStatsByBackground` (the same per background, first view: readability on light and
ground colors), `valueContrast` in every color stat (share of the effect's pixels whose
brightness differs from the background behind them by ≥ 0.2: how much reads by value; with
a `ground`, the readability measure), `systemColorStats` (the same for each system rendered alone: each layer's
own hue and how its saturation changes over its life),
`viewFraming` (look-at point and distance per view) to pass back as `view_framing`,
(`colorStats.coverage` is a share of the *frame*, and auto framing depends on the sampled
`times`, so coverage is only comparable between captures with the same times, or with a
fixed `framing_radius`; hue, saturation and washedOut do not depend on framing),
`warnings` for likely problems (washed out, low contrast on a background, silent
sub-emitters, no game volume profile), `notes` for information that needs no action, and
`toolkitVersion`.

The effect is played frame by frame at 60 fps from a seeded restart, like the game plays
it: t = 0 is the first frame, times are rounded to 1/60 s, sub-emitters fire.

Capture cameras are much closer than a game camera, so the capture copy lifts every
particle renderer's `maxParticleSize` (share of screen height, 0.5 by default) to show the
real size animation of large particles. When a layer would have been clamped, a note says
so, with the camera distance below which the game would clamp it too.

Limits: at most 24 times and 192 frames per call. VFX Graph stepping in edit mode is
experimental. If VFX Graph frames look empty or identical, say so and verify in Play
Mode rather than guessing.
