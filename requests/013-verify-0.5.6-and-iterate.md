# 013: Verify 0.5.5 / 0.5.6, then /vfx:iterate StrongExplosion toward the references

Commits 8985bb2 (toolkit 0.5.5) and f655485 (plugin 0.5.6). Update both within your
permission settings, wait for the recompile, `/reload-plugins` (the session should then
read the 0.5.6 texts: `vfx-fundamentals` has "Acceptance tests that can be met").

## 1. Quick checks (only under `Assets/VFX/_Test008/` and captures)

- **Capture**: re-run the 012 CoinPickup capture. Report: response size (compact by
  default; `resultFile` exists and holds `frames`), `postProcessing.qualityLevel`,
  `pipelineAsset`, `colorGrading`, capture time vs 0.5.4 (expected clearly faster), sand
  (`e6d2a4`) hue no longer -1, and whether the two "Low contrast" warnings from 012 are
  gone (they compared bloom-inflated coverage).
- **Recipes**, on `_Test008`: one patch recipe where system `Dome` references
  `"material": "Assets/VFX/_Test008/M_Test008_Dome2.mat"` by path while a second new
  system `Ring` defines that same material inline (shader Stylized Shell, `"_ErosionMap":
  {"texture": "Assets/VFX/_Test008/Textures/T_Test008_Stripes.png", "tiling": [2, 1]}`).
  Expect: applies on the first try, both systems use the new material, tiling (2, 1). Then
  patch `Dome` `custom_data.custom1` with only `"y"`: `x` must survive (component count 2).
  Afterwards set `Dome` back to `M_Test008_Dome.mat` and remove nothing else.
- **Arc rim**: `vfx_make_mesh` arc again (rebuild), capture `_Test008` if it uses it, or
  just confirm the rebuilt mesh's normals are not all (0, 0, -1).
- **vfxtex**: `stripes --bands 7 --arch 1`, `preview ... --ground 4E8A3A`; open the preview:
  data textures gray in every row, a fourth ground row.

## 2. /vfx:iterate StrongExplosion (ask the user first: it changes the approved effect)

The user's goal is an effect that really reads like the references; the 009 review's
remaining differences are the brief. Show the user the delta table before applying.

`/vfx:iterate StrongExplosion "closer to the references: the dome should break into a few wide arches opening from the ground (not thin umbrella strips), the puffs should be round toon puffs that stay orange then turn warm gray (not flower shapes, not brown mud) and not cover the front of the dome, the dark smoke dome should be translucent, and the ink should form one big swirl at 0.8 s rather than a pile of rings"`

Useful new pieces: `stripes --arch 1` (odd band count), `puff` with its strokes kept off
the middle, acceptance tests with both bounds, per-phase hue. Captures are HDR now
(0.5.4+): the white-hot start looks different from 009's LDR captures; compare against the
references, not against 009's numbers.

## What to send back

§1 pass/fail with numbers. §2: the delta table, the critic's score and compare summary
(bright/ink/smoke correlations, phase hues), the contact sheet and compare sheet paths,
your honest look against the references, and every instruction or tool problem.
