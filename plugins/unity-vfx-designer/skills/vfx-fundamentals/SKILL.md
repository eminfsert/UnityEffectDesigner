---
name: vfx-fundamentals
description: Principles of stylized game VFX (timing beats, shape language, value and color, readability, layering) and the Effect Designer's critique rubric with its measurable checks. Load when writing an effect spec, judging a capture, or deciding what to change between iterations.
user-invocable: false
---

# Stylized VFX fundamentals

## Timing: anticipation → impact → dissipation

- **Anticipation** (optional, 0.1–0.4 s): gather, charge or suck inward. Sells the hit.
- **Impact** (1–3 frames): the brightest, largest, most contrasting moment. Flash and
  shape at full size **immediately** at t≈0 of the beat. No slow fade-in.
- **Dissipation** (0.3–1.5 s): things slow down (ease-out, drag/dampen), shrink, erode and
  lose saturation. Leftovers (embers, smoke, sparks) keep the effect alive after the peak.
- Speeds use **fast-then-hang** curves (`ease_out_expo`, limit velocity with dampen). Linear
  motion reads as cheap.
- Keep layer timings offset. If everything starts and ends together, the effect reads as
  one blob.

## Shape language

- Readable **silhouette** at gameplay size and camera distance. A good effect still reads
  as a small thumbnail.
- **Big / medium / small** hierarchy: one or two hero shapes (flash, ring, slash), a medium
  layer (smoke, shockwave), many small details (sparks, embers).
- Match shapes to intent: sharp and angular for aggressive or electric, round and puffy
  for soft, magic or healing, spirals and swirls for arcane.
- Stylized means **designed shapes**: textured masks, meshes and flipbooks, not piles of
  soft round particles.

## Value and color

- Value contrast first: a bright core against darker surroundings. Color second.
- Palette: one dominant hue, a white-ish **core** for the hottest point, one **accent**, and
  a dark companion tone for readability on bright ground.
- Glow comes from HDR on **materials** with **saturated tints**. White or overly intense
  tints wash the palette out (see `vfx-shaders` calibration).
- Saturation falls off over life: fresh = saturated and bright, old = desaturated and dim.

## Readability

- Check the effect on a **dark and a light** background. Additive layers vanish on bright
  scenes; pair them with an alpha-blended darker layer when the game has daylight scenes.
- The effect must not hide gameplay (target, character) longer than its impact beat.

## Layering

Typical impact: core flash → shape (ring/star/slash) → sparks → smoke/dust → embers →
light flash (+ camera shake in game). Each layer has one job; if two layers do the same job,
cut one.

---

# Critique rubric (used by the VFX Critic)

Score each 0–5, weight, total out of 100. **Pass at ≥ 75 with no criterion at 0–1.**

| Criterion | Weight | Evidence |
|---|---|---|
| Spec fidelity | 20 | Every spec layer present and active in its time window (`systemParticleCounts`); archetype and beats recognisable on the contact sheet |
| Timing and feel | 20 | Impact within the first 1–3 frames of its beat; clear anticipation/dissipation if specced; fast-then-hang motion; layers offset |
| Shape and silhouette | 15 | Hero shape reads at thumbnail size; big/medium/small hierarchy; designed shapes rather than soft blobs |
| Color | 15 | Dominant `colorStats.hue` within ±15° of the spec's primary hue; `washedOut` < 35% outside the core flash frame; saturation falls over life |
| Readability | 15 | Visible on both dark and light backgrounds; not a single blob at peak |
| Technical | 15 | No toolkit warnings or errors, no compile errors, sensible particle counts for the platform, no layer cut by the frame |

Always capture with the game's volume profile. Color is only judged under the game's
post-processing.

## Writing fixes

Every problem becomes a routable fix. Name the owner, the layer, what is wrong **with
evidence**, and a concrete change:

```json
{ "owner": "particle-artist", "layer": "sparks", "priority": "high",
  "issue": "Sparks still 40 alive at 0.5s but invisible (size ~0): the layer looks dead while it is still active",
  "evidence": "systemParticleCounts.Sparks = [0,40,40,40,40,32,5]; contact sheet columns 0.35s-0.5s empty",
  "change": "start_lifetime [0.3,0.55]; size_over_lifetime ease_out_quad from 1 to 0.2" }
```

Owners: `particle-artist`, `shader-artist`, `texture-artist`, `vfx-architect` (contracts,
layer structure). Give at most 6 fixes per round, highest impact first. Do not fix things
the spec did not ask for.
