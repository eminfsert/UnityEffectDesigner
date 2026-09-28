# 015: 0.5.8, before the next StrongExplosion iterate round

Commit e0627d9. Package and plugin are **0.5.8**. If the user approves the second iterate
round (F10: cooling follows the strips, center orange by 0.2 s, edges white until they
break; F11: lighter puffs), update **before** it starts (within your permission settings),
wait for the recompile, `/reload-plugins`, and keep the version fixed during the round.

What changed that matters for that round:
- **Erosion 0 cuts nothing** in Stylized Shell and Stylized Unlit (no edge band either): the
  7 thin protrusions at frame 0 with `_EdgeWidth > 0` are gone. `StrongExplosionShell` is a
  copy: port the same change into it (`if (input.custom.y > 0.0)` around the erosion block,
  edge without the `step`), and check it clips eroded pixels (`clip(color.a - 0.001)`,
  in Stylized Shell since 0.5.5). With the clip, `_ZWrite 1` no longer hides layers behind
  the arch gaps; keep 0 unless something needs it.
- `stripes` now has a 0.03 floor and wants **V Clamp** (U Repeat); `puff` strokes sit on
  every other bump (no crossings); new `wisp` for smoke tendrils.
- Capture: the compact response gives color stats as **columns**
  (`systemColorStats.Dome.hue[i]` at `...time[i]`), and a new `coreSaturation` (fill only:
  the gray-puff saturation question from the last round).
- There is still **no per-region measurement inside one layer** (dome center vs edge). For
  F10, measure it the way the critic did (fixed pixel samples) or with `system_frames` and a
  crop; say in the review how it was measured.
- Instructions: architect first when contracts change; draw order in the manifest;
  acceptance tests must be physically possible (a translucent fill cannot carry
  valueContrast alone; test the rim/strokes).

Report the round as before (delta table, score, compare summary, paths, honest look,
problems).
