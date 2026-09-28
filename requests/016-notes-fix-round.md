# 016: 0.5.9 for the StrongExplosion fix round (F12, F13, F11)

Commit after 1b8c0f7. Package and plugin are **0.5.9**. Update before the fix round starts
(within your permission settings; after the user's grass decision), and keep it fixed
during the round.

- **Reach is measurable now.** Add `"spread_reference": "Dome"` (or the smoke layer, if the
  spec's reach is against the smoke: name it explicitly) to the capture block. Each other
  system gets `systemSpread.<system>`: `inside` (share of its pixels inside the reference
  silhouette), `p50`/`p90`/`max` (distance from the reference centre in its radii), per
  time, first view. F12's acceptance ("15–45% inside at 0.1/0.2 s; p90 1.5–2.1 radii at
  0.3 s, max ≤ 2.3") can be checked directly instead of by hand.
- **Rubric changes** the critic will read: readability with large translucent layers is
  judged on the opaque layers' own `valueContrast` and the translucent layers' rims (the
  composite is diluted by design); the hue of translucent layers over grass is judged
  against the sky (side view) or the light background, not over grass.
- The grass fork itself (smoke shells reading green over grass) is the user's decision; the
  tools only measure it.
