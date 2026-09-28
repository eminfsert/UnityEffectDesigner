# 012: Verify the HDR capture target (0.5.4), after 009 is finished

Commit 562bb5e. Package and plugin are **0.5.4**. Run this **after 009 is finished** on the
version it started with (as agreed in 011). Update within your permission settings, wait
for the recompile, `/reload-plugins`.

The capture now renders into a half-float linear target and encodes 8-bit sRGB itself
after post-processing, so HDR colors reach bloom and tonemapping unclipped, as in the game.

## 1. Your tint test, again

Same setup as your finding (`_Test004` Flash, alpha blend, T_VFX_Star4, dark background,
`framing_radius 1.5`, t = 0, the island volume profile + Neutral; set `_TintColor` directly
to the linear values, restore it afterwards):
- (1, 0.5, 0.1): report mean and max RGB. Expect about the same as before (nothing
  exceeded 1 there).
- (4, 2, 0.4): **R ≠ G** now (the tonemapper compresses, it does not clip to (1, 1, 0.4)).
- (16, 8, 1.6): not neutral gray: still visibly warm, lighter than (4, 2, 0.4).

## 2. Regressions

- Background color: a pixel of the empty dark background still reads (18, 18, 23) ± 1, and
  light (199, 204, 209) ± 1 (the stats rely on the background-only render matching).
- CoinPickup, captured with its manifest block and the previous `viewFraming`: its tints
  are ≤ 1, so the contact sheet and `colorStats` should match the 006 capture within a
  few 1/255 steps. Report the largest difference you see.
- Capture time for the same capture, before vs after (the float readback costs something).

## 3. 009 under HDR

Capture StrongExplosion's final prefab once with the manifest block and the last
`viewFraming`, and compare with 009's last capture: which layers changed (Dome core @1,
rim/edge @0.3, SpeedDashes core @1 are the ones above 1), and does it look closer to or
further from the references? Also run the 011 steps (vfxref sheet with the smoke class and
one `compare`) on this capture. Do not tune: report.
