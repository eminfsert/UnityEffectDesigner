# 009: /vfx:create StrongExplosion from references (after 008 passes)

Run this only after 008 passes (or after its failures are fixed in a newer commit). It is
the real test of the 0.5.0 infrastructure: can the team rebuild a complex stylized effect
from reference screenshots?

**Needs the user:** the concept board checkpoint is theirs to approve. The effect is for
their game: a strong explosion, used the same way.

## Run

`/vfx:create A strong stylized explosion, like the reference screenshots in Assets/VFX/StrongExplosion/Design/refs/ (ref_01..05, in time order): a toon dome that pops up white-hot, cools to yellow, peach and orange while breaking into vertical strips, then orange toon puffs with dark strokes, black ink slashes and swirls, and dark translucent smoke that fades with a few embers. About 1.5 s in total, about 3 times the character's height across, sitting on bright grass in daylight.`

Known from the user: duration ~1.5 s; size ~3× the character's height; used in their game
as a strong explosion; ground is bright grass (use the game's grass color for `ground`;
the references' grass measures about `#4E8A3A`).

## What to send back

1. The concept board and whether the user changed it; the spec's layer inventory and the
   `reference_analysis` block (did the Director use vfxref's numbers for beats and palette?).
2. The manifest's meshes, textures and shaders (which new building blocks were used).
3. Per critic round: score, the `vfxref compare` summary (curve correlations, share and
   hue differences), and the fixes.
4. The final contact sheet and compare sheet paths, and your honest judgement against the
   references: what reads like them, what does not.
5. Every instruction that was wrong, missing or contradictory, and every tool error. This
   matters most: it is what the next infrastructure round fixes.
