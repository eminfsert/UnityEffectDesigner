# manifest.yaml

Written by the **VFX Architect** from the approved spec. It is the contract that lets the
Texture, Shader and Particle artists work in parallel without guessing each other's names.
Location: `Assets/VFX/<EffectId>/Design/manifest.yaml`.

```yaml
effect: ArcaneNova
root: Assets/VFX/ArcaneNova
prefab: Assets/VFX/ArcaneNova/VFX_ArcaneNova.prefab
folders:
  textures: Assets/VFX/ArcaneNova/Textures
  materials: Assets/VFX/ArcaneNova/Materials
  shaders: Assets/VFX/ArcaneNova/Shaders          # only if a custom shader is needed
capture:
  volume_profile: from project settings            # or an explicit Assets/...asset path
  times: [0, 0.1, 0.25, 0.27, 0.3, 0.4, 0.55, 0.7, 0.9, 1.4]
  views: [three_quarter, side]
  backgrounds: [dark, light, "#9C8458"]            # + the game's ground color if it has one
  frame_size: 320
runtime:                                           # how the game spawns it (from its code)
  spawner: Assets/Scripts/Fx.cs                    # where it is played
  pooled: true                                     # reused: must fully reset on Play
  stop_action: none                                # pooled effects: none (the pool deactivates it); destroy only for Instantiate-and-forget
  release_after: 1.5                               # when the spawner releases it: the effect must be over by then
  scaling_mode: hierarchy                          # the game scales the root
  overrides: [start_color]                         # values the game sets at runtime (e.g. tint per team)
  attach: world                                    # world position, or parented to a moving object

textures:                                          # everything the layers sample
  - name: T_ArcaneNova_Ring
    path: Assets/VFX/ArcaneNova/Textures/T_ArcaneNova_Ring.png
    status: make                                   # make | starter | existing
    how: "vfxtex.py ring Assets/VFX/ArcaneNova/Textures/T_ArcaneNova_Ring.png --breaks 6 --width 0.14 --steps 3 --size 512"
  - name: T_VFX_Star4
    path: Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Star4.png
    status: starter

meshes:                                            # made with vfx_make_mesh by the Particle Artist
  - name: SM_ArcaneNova_Dome
    path: Assets/VFX/ArcaneNova/Meshes/SM_ArcaneNova_Dome.asset
    status: make                                   # make | existing
    make: { shape: dome, radius: 0.5, angle: 90, segments: 48, rings: 16 }

shaders:
  - name: EffectDesigner/Particles/Stylized Unlit   # default; custom shaders only when needed
    status: starter
    contract:                                       # checked with vfx_compile_report
      _BaseMap: Texture
      _TintColor: HDRColor
      _NoiseMap: Texture
      _Erosion: Float

materials:
  - name: M_ArcaneNova_Ring
    path: Assets/VFX/ArcaneNova/Materials/M_ArcaneNova_Ring.mat
    shader: EffectDesigner/Particles/Stylized Unlit
    blend: additive
    textures: { _BaseMap: T_ArcaneNova_Ring, _NoiseMap: T_VFX_Noise }
    tint: { color: primary, intensity: 1 }          # palette name + stops of linear light
    properties: { _PosterizeSteps: 3, _EdgeColor: accent }   # other shader properties (starting values)

systems:                                            # one per spec layer (plus companions)
  - name: Ring
    layer: ring
    window: [0.25, 0.70]                            # from the spec: when this system is visible
    render_mode: mesh                               # billboard / stretch / horizontal_billboard / mesh
    mesh: SM_ArcaneNova_Ring                        # mesh particles only
    alignment: local                                # mesh particles: local or world
    sorting_fudge: 0                                # draw order among layers (lower = in front)
    material: M_ArcaneNova_Ring
    vertex_streams: [Position, Normal, Color, UV, Custom1XY]
    custom_data: { custom1.x: "ramp 0 -> 1 over life", custom1.y: "erosion 0 -> 1 from 60% of life" }
    notes: "pops in 2 frames; strips open from 0.4 s"
  - name: Sparks
    layer: sparks
    render_mode: stretch
    material: M_ArcaneNova_Sparks
```

Rules:
- Names: `VFX_<Id>` prefab, `T_<Id>_<Name>` textures, `M_<Id>_<Layer>` materials, system
  GameObjects named after the layer in PascalCase.
- **Vertex streams and custom data are a contract** between the Particle and Shader
  artists. Change them only here, never on one side alone.
- Prefer the starter shader. A custom shader needs a one-line reason in the manifest.
- **Draw order is a contract** when layers overlap from a free camera: a layer that must not
  cover another gets an explicit `sorting_fudge` relation here (placement alone does not
  hold for every camera angle).
- **Who updates what.** The manifest holds contracts and starting values; the current
  values live in the last applied recipes in `Design/recipes/` (every artist, the shader
  artist included, saves the patch it applied there). When a contract changes (a shader
  property added, a stream, a draw order), the owner reports it and the architect updates
  the manifest before the next round.
- **Contracts, not tuning.** The manifest fixes names, paths, texture/material/system
  links, shader properties and streams. Curves, counts, speeds and exact colors live in the
  recipes (`Design/recipes/`) and change every round; `tint` and `properties` here are
  starting values.
- **Particle color vs tint: one of them carries the hue.** The material `tint` (HDR) sets
  the layer's hue, brightness and glow. Particle colors (start color, color over lifetime)
  are 8-bit and **multiply** the tint in linear light, so a hue in both is applied twice:
  `#FFB030` × `#FFB030` renders deep orange (~28°), not gold. Default: tint = the layer's
  palette color @ its intensity, particle color white (or near-white, for small
  variation) with the alpha fade. Use a colored particle color only for a deliberate hue
  shift over life, with a near-white tint.
- `status`: `make` = the Texture Artist creates it; after it exists the artist sets
  `existing`, so later rounds do not remake it. A fix that changes a texture sets it back
  to `make` with a new `how`. `starter` = shipped with the toolkit.
- `how` commands name `vfxtex.py`: the script in the `texture-authoring` skill's
  `scripts/` folder (the skill's base directory is shown when it loads). The output path
  is always the texture's `path`.
- The capture block is used for every capture of this effect, so iterations stay
  comparable. The camera placement is not stored here: the critic reuses the previous
  review's `viewFraming`. Add the game's ground color as a background when the effect
  plays over terrain.
- `release_after` is checked against the effect's real length: for every system,
  max start_delay + duration + max start_lifetime (curves: their maximum), the largest
  over all systems. A start_delay or a curved lifetime is easy to miss.
- Materials for the Stylized Shell list its ramp and rim properties
  (`_RampColor0..3`, `_RampStops`, `_RampHard`, `_RimColor`, `_RimWidth`, `_BackTint`,
  `_ErosionMap`) in `properties`; textures list their import (`mask` / `data` / `rgb`), which
  decides sRGB and wrap (see `texture-authoring`).
- `runtime` records what the game's code does with the effect (see `vfx-architect`); the
  Particle Artist sets `stop_action`, `scaling_mode` and looping to match it. Leave it out
  only when the effect has no spawner yet, and say so.
