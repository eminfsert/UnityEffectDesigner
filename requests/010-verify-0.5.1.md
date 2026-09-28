# 010: Verify 0.5.1 (008 findings), then continue with 008 §5 and 009

Commit a1956a7. Package and plugin are **0.5.1**. Update both as in 008 (within your
session's permission settings; if anything prompts, ask the user), wait for the recompile,
`/reload-plugins`.

The `_Test008` material has `_ZWrite 1` from your 008 experiment. Set it back to 0 first, so
the fix is tested on its own (a material patch only changes the properties it names, so
re-applying the 008 recipe would keep 1):

```json
{ "target": "Assets/VFX/_Test008/VFX_Test008.prefab",
  "systems": [ { "name": "Dome", "renderer": { "material": {
    "path": "Assets/VFX/_Test008/M_Test008_Dome.mat",
    "shader": "EffectDesigner/Particles/Stylized Shell",
    "properties": { "_ZWrite": 0 } } } } ] }
```

## 1. Shell sorting

`vfx_compile_report` on `StylizedShell.shader` (it now has two passes: `StylizedShellInside`
with LightMode SRPDefaultUnlit and Cull Front, then `StylizedShell` with UniversalForward
and Cull Back). Then capture `_Test008` with the 008 §4 capture block plus
`view_framing` = the 008 capture's `viewFraming`, `label "test010_dome"`:
- side view: no sawtooth bands; the inside (dark `_BackTint`) shows only where the front is
  cut away (strips) or translucent.
- If the inside is not drawn at all (the SRPDefaultUnlit pass skipped), say so: that
  decides whether the two-pass approach works in this URP version.

## 2. Capture fixes

From the same capture:
- no maxParticleSize note for the mesh Dome;
- `systemColorStats.Dome` present (equal to `colorStats`) with a note;
- white-dome frames (0–0.05 s): hue no longer 82–85° (the grass-colored edges are left out);
- every color stat has `valueContrast`; with the ground filling the three_quarter frame
  there is a note instead of a low-contrast warning. Report `valueContrast` per time.

## 3. Then

Run 008 §5 (vfxref on the user's references) once the screenshots are in
`Assets/VFX/StrongExplosion/Design/refs/`, then 009.
