# 007: /vfx:iterate test on CoinPickup (colors after the [HDR] fix)

Commit 5c516d0. Plugin **0.4.4**, package unchanged (0.4.3).

**Prerequisites (need the user's approval):** update the plugin to 0.4.4 and
`/reload-plugins` (the session should then read the 0.4.4 skill texts: `particle-recipes`
has "Hue lives in one place", `unity-adapter` requires toolkit ≥ 0.4.3). This request
changes the look of an approved effect: **ask the user before running it**, and show them
the delta table step 2 of the iterate skill produces before anything is applied.

## Test

Run:

`/vfx:iterate CoinPickup "since the color fix the palette has split: the ring reads orange and the star and glints lemon. Bring everything back into one gold family, and make the glints readable on light and sand ground."`

Let the skill run its normal flow (delta table → spec/manifest updates if needed → owner
agents with patch recipes → one critic round with the previous `viewFraming`).

## What to send back

1. Did the skill follow the new rules without being told: hue carried by the tint with
   white/near-white particle colors (or a stated reason not to), layers within ~15° of
   the primary, fixes with `owners`/`accept`, the previous-round table in the review?
2. The delta table it proposed, and whether the user changed it.
3. Per layer before (006) → after: hue, saturation, washedOut (from `systemColorStats`), and
   the light/sand coverage vs dark from `colorStatsByBackground`.
4. The critic's score before → after, and your own look at the contact sheet.
5. Any instruction that was wrong, missing or contradictory (file and what should change),
   tool errors, and whether the post-recompile disconnect happened again.
