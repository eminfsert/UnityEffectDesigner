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
  backgrounds: [dark, light]

textures:                                          # everything the layers sample
  - name: T_ArcaneNova_Ring
    path: Assets/VFX/ArcaneNova/Textures/T_ArcaneNova_Ring.png
    status: make                                   # make | starter | existing
    how: "vfxtex ring --breaks 6 --width 0.14 --steps 3 --size 512"
  - name: T_VFX_Star4
    path: Packages/com.effectdesigner.vfxtoolkit/Textures/T_VFX_Star4.png
    status: starter

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

systems:                                            # one per spec layer (plus companions)
  - name: Ring
    layer: ring
    render_mode: billboard                          # or horizontal_billboard / mesh / stretch
    material: M_ArcaneNova_Ring
    vertex_streams: [Position, Color, UV, Custom1X]
    custom_data: { custom1.x: "erosion 0 -> 1 over life (ease_in_quad)" }
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
- The capture block is used for every capture of this effect, so iterations stay comparable.
