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
- Speeds use **fast-then-hang** curves (`velocity_over_lifetime.speed_modifier` with
  `ease_out_expo`, or limit velocity with dampen). Linear motion reads as cheap.
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
  tints wash the palette out (see the color notes in `vfx-shaders`).
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
| Spec fidelity | 20 | Every spec layer present and active in its time window (`systemParticleCounts`); archetype and beats recognisable on the contact sheet. **With references:** the `vfxref compare` sheet shows the same sequence (`curve_correlation` of bright and ink ≥ 0.7), each phase's bright hue within ±12°, and the silhouettes the spec's `follow` list names; 0–1 if the sequence is different |
| Timing and feel | 20 | Impact within the first 1–3 frames of its beat; clear anticipation/dissipation if specced; fast-then-hang motion; layers offset |
| Shape and silhouette | 15 | Hero shape reads at thumbnail size; big/medium/small hierarchy; designed shapes rather than soft blobs |
| Color | 15 | Each layer's rendered hue (`systemColorStats.<layer>.hue`) within ±15° of its palette color, the whole effect's (`colorStats.hue`) within ±15° of the primary; `washedOut` < 35% outside the core flash frame; saturation falls over each layer's life (`systemColorStats`) |
| Readability | 15 | No "Low contrast" warning: coverage on the light/ground background ≥ 50% of the dark one (`colorStatsByBackground`); with a `ground`, `valueContrast` against it ≥ 0.5 at the impact and through the main body; readable at thumbnail size; not a single blob at peak |
| Technical | 15 | No toolkit `warnings` or errors (`notes` are information, not problems), no compile errors, sensible particle counts for the platform, no layer cut by the frame |

Always capture with the game's volume profile. Color is only judged under the game's
post-processing: "rendered hue" is the hue of the captured pixels after tonemapping and
bloom, which can differ from the material's tint (a saturated gold whose red channel hits
the ceiling renders yellow). Capture every round with the previous round's `viewFraming`
passed back as `view_framing`, so sizes are compared at identical framing.

**A layer alone and the composite differ.** Overlapping layers (a dark companion under
it, bloom from brighter neighbours) shift the composite's hue: a ring measured 28–35°
alone and ~48° in the full frame. Judge each layer's palette color on its own
(`systemColorStats`), and the overall read on the composite (`colorStats`). When the two
disagree enough to matter, capture with `system_frames: true` and compare the images.

**Diagnostic captures are allowed.** When a verdict needs it, capture more times around a
beat, a single view at a larger frame size, or `system_color_stats` over more times. Say
in the review which captures a score rests on.

## Fixing color by measurement

- A hue off target because a channel clips (gold drifting to yellow, magenta to pink):
  **lower the intensity or the clipped channel's neighbours' share**, do not add
  intensity. Raising HDR intensity pushes more channels to the ceiling and moves the hue
  further toward white.
- Control a layer's hue with **one** carrier (normally the material tint; particle color
  white), and its brightness with intensity. A hue in both tint and particle color is
  applied twice and drifts toward red/orange.
- **One palette family per effect.** When layers measure far apart (a ring at 20–30°
  next to stars at 60°), the effect reads as two effects: bring the layers' tints within
  ~15° of the primary unless the spec asks for an accent. Numbers from earlier effects are a starting point only: the same values
  render differently with another shape, size, blend mode or volume profile.
- Every color fix states its **acceptance test** in the stats the next capture returns,
  e.g. `systemColorStats.Sparks hue 35–50 and washedOut < 35% at 0.1–0.35 s`.

## Writing fixes

Every problem becomes a routable fix. Name the owner, the layer, what is wrong **with
evidence**, and a concrete change:

```json
{ "id": "F2", "owners": ["particle-artist"], "layer": "sparks", "priority": "high",
  "issue": "Sparks still 40 alive at 0.5s but invisible (size ~0): the layer looks dead while it is still active",
  "evidence": "systemParticleCounts.Sparks = [0,40,40,40,40,32,5]; contact sheet columns 0.35s-0.5s empty",
  "change": "start_lifetime [0.3,0.55]; size_over_lifetime ease_out_quad from 1 to 0.2",
  "accept": "Sparks visible in the 0.35 s and 0.5 s columns; systemColorStats.Sparks coverage > 0 there" }
```

- `owners`: one or more of `particle-artist`, `shader-artist`, `texture-artist`,
  `vfx-architect` (contracts, layer structure). The first owner leads; list a second when
  the change crosses (a new mask plus the system that uses it).
- `accept`: what the next capture must show, in contact-sheet columns or returned stats.
- `"optional": true` marks polish that should not block a pass; required fixes come first.

Give at most 6 fixes per round, highest impact first. Do not fix things the spec did not
ask for.

From round 2 on, open the review with the **previous round's table**: each earlier fix,
whether it landed, and its acceptance metric before → after. A fix that did not land is
carried over (same id) before new ones are added.
