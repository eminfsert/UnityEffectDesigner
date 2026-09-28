# Effect Designer VFX Toolkit

Unity 6 (URP) editor package used by the **Unity Effect Designer** Claude Code plugin.
It adds VFX-specific tools to [MCP for Unity](https://github.com/CoplayDev/unity-mcp)
through its custom tool mechanism (`[McpForUnityTool]`), so no extra server or port is
needed.

## Install

Requires MCP for Unity (`com.coplaydev.unity-mcp`) in the same project.

Package Manager → **Add package from git URL**:

```
https://github.com/eminfsert/UnityEffectDesigner.git?path=unity-package/com.effectdesigner.vfxtoolkit
```

After installing, reconnect your MCP client so it picks up the new tools.

## Tools

### `vfx_capture_timeline`

Renders an effect at several points in time and writes a labelled contact sheet. This is
how the agents *see* motion and timing.

- Works in **edit mode**, inside an isolated preview scene: the open scene is not modified
  and nothing else in it is rendered. The project's global Volumes (bloom, tonemapping)
  still apply.
- Plays the effect forward **frame by frame at 60 fps**, like the game does, so bursts at
  `t = 0` show in the first frame and sub-emitters fire. Times are rounded to 1/60 s;
  `t = 0` is the first frame.
- **Deterministic:** every ParticleSystem gets a fixed seed and each pass restarts from
  `t = 0`, so two captures of the same effect are identical.
- Samples Shuriken (including sub-emitters and child systems), VFX Graph
  (`VisualEffect`, experimental in edit mode) and any component implementing
  `EffectDesigner.VFXToolkit.IVfxTimeSampleable` (mesh scale curves, lights, material
  animation).
- **Auto framing:** each view is framed on the pixels the effect covers across
  all sampled times (compared against a background-only render, so vignette and fog
  are ignored; the outer 2% of covered pixels per axis are trimmed so a few stray
  particles do not dictate the framing).
  Pass `framing_radius` to keep a fixed scale between iterations instead.
- Sets the shader globals `_VFXToolkitCapture = 1` and `_VFXToolkitTime = t` while
  rendering, so toolkit shaders can use capture time instead of `_Time` for scrolling
  and dissolves.

Example call (MCP):

```json
{
  "target": "Assets/VFX/ArcaneNova/Prefabs/VFX_ArcaneNova.prefab",
  "times": [0, 0.1, 0.2, 0.35, 0.5, 0.8, 1.2],
  "views": ["three_quarter", "side"],
  "backgrounds": ["dark", "light"],
  "label": "arcane_nova_iter1"
}
```

Output (in `Library/VFXToolkit/Captures/<label>_<timestamp>/` by default):

- `contact_sheet.png`: columns are times (stamped at the top), rows are view/background
  pairs in the order given by `rows` in the response.
- One PNG per frame: `<view>_<background>_t<time>.png`.
- `particleCounts` per time and `systemParticleCounts` per system per time, framing, and
  warnings (e.g. a sub-emitter that never produced particles).
- `postProcessing`: tonemapping and bloom in effect. Volumes in the open scenes never reach
  the capture's preview scene, so without `volume_profile` only the pipeline's default
  profiles (global + quality level) apply. Pass the game scene's VolumeProfile, or set it
  once per project: select it, then **Assets → Effect Designer → Use As Capture Volume
  Profile** (writes `ProjectSettings/EffectDesigner.json`, used by every capture).
- `colorStats` per time (first view/background): coverage, `washedOut` (share of bright
  pixels that lost their color, e.g. from a white HDR tint), mean saturation, dominant hue.
  Most frames washed out gives a warning.

Without an MCP client you can use **Tools → Effect Designer → Capture Timeline Of
Selection**.

### `vfx_apply_particle_recipe`

Creates or updates a hierarchy of Shuriken particle systems from one JSON recipe.

- Every module and renderer property is addressable by its Unity name in snake_case
  (bound by reflection, so nothing is left out), plus `emission.bursts`, `sub_emitters`,
  `custom_data`, `renderer.vertex_streams` and texture-sheet `sprites`.
- Curves: `1.5`, `[min, max]`, `[[t, v], ...]`, `{"ease": "ease_out_expo", "from": 1, "to": 0}`.
  Colors: `#hex`, `$palette` names, HDR `{"color": "#hex", "intensity": stops}` (stops of linear light, like Unity's HDR picker)
  (particle colors are 8-bit in Shuriken, so HDR is rescaled with a warning; glow belongs in the material).
  Angles in degrees with a `_deg` suffix (`start_rotation_deg`).
- **Validated before anything changes:** on any error nothing is modified and all problems
  come back at once with "did you mean" suggestions. `dry_run` validates only.
- Updates are patches; `"reset": true` rebuilds a system. Target a prefab path to edit it
  in place, a scene object, or nothing to create a new root (`save_prefab` to save it).

```json
{
  "name": "VFX_ArcaneSparks",
  "save_prefab": "Assets/VFX/ArcaneSparks/VFX_ArcaneSparks.prefab",
  "palette": { "core": "#FFF4D6", "accent": "#FFC247" },
  "systems": [
    {
      "name": "Sparks",
      "main": { "loop": false, "duration": 1, "start_lifetime": [0.4, 0.8], "start_speed": [6, 12],
                "start_size": [0.04, 0.09], "start_color": ["$accent", "$core"], "gravity_modifier": 0.6 },
      "emission": { "rate_over_time": 0, "bursts": [{ "time": 0, "count": [30, 45] }] },
      "shape": { "shape_type": "sphere", "radius": 0.2 },
      "limit_velocity_over_lifetime": { "limit": 3, "dampen": 0.15 },
      "size_over_lifetime": { "size": { "ease": "ease_out_quad", "from": 1, "to": 0 } },
      "renderer": { "render_mode": "stretch", "velocity_scale": 0.08, "length_scale": 1 }
    }
  ]
}
```

`renderer.material` / `trail_material` may be inline objects that create or patch the
material asset: `{"path", "shader", "blend": "additive|alpha|premultiplied|multiply|soft_additive",
"properties": {...}, "keywords": {...}}`. Material colors keep HDR intensity.

The full format reference for agents is the plugin's `particle-recipes` skill.

### `vfx_compile_report`

Re-imports shaders and reports compile errors/warnings with file and line, and each
shader's properties (name, type, HDR flag). `paths` accepts `.shader`/`.shadergraph`
files, folders, and include files (`.hlsl`/`.cginc`: every shader under `Assets/` that
includes it is checked). `expected_properties` (`{"_TintColor": "HDRColor", ...}`) checks
a property contract and lists missing properties and type mismatches.

## Shaders

- `Shaders/VFXCore.hlsl`: shared URP effect functions: `VFXTime()` (capture-aware time),
  scrolling and polar UVs, erosion with edge, posterize, soft particles, fresnel.
- `EffectDesigner/Particles/Stylized Unlit`: particle shader with HDR tint, blend presets,
  noise erosion driven by `_Erosion` + Custom1.x (vertex streams
  `Position, Color, UV, Custom1X`), glowing erosion edge, posterized alpha, soft particles.

## Starter textures

Procedural, white RGB with the shape in alpha (color comes from the material tint):
`Textures/T_VFX_SoftGlow.png` (glow with a hot core), `Textures/T_VFX_Star4.png`
(stylized 4-point sparkle), `Textures/T_VFX_Noise.png` (tileable fractal noise, linear,
repeat) for erosion. Regenerate with `tools/textures/gen_starter_textures.py`. Effect-specific
textures are made by the Texture & Vector Artist agent in the project's own folders.

## Development

`tests/run.sh` (repo root) compiles the package against Unity reference assemblies in
every conditional-compilation variant and checks that recipes map onto real Unity module
properties. Needs the .NET 8 SDK; no Unity install required.
