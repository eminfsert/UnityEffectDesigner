# 010: Verify 0.5.1 (008 findings), then continue with 008 §5 and 009

Commit a1956a7. Package and plugin are **0.5.1**. Update both as in 008 (the user has
allowed these installs in auto mode), wait for the recompile, `/reload-plugins`.

The `_Test008` material has `_ZWrite 1` from your 008 experiment: set it back to 0 first
(re-apply the 008 §4 recipe unchanged; it rewrites the material), so the fix is tested on
its own.

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
