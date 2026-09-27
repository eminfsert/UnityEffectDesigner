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
- **Deterministic:** every ParticleSystem gets a fixed seed and is re-simulated from
  `t = 0` for each time, so two captures of the same effect are identical.
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
- `particleCounts` per time, framing bounds, and warnings.

Without an MCP client you can use **Tools → Effect Designer → Capture Timeline Of
Selection**.
