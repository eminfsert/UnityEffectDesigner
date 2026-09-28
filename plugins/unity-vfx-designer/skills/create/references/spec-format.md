# effect.spec.yaml

Written by the Director (main session) after intake, approved by the user before
production. It describes **what** the effect is, not how it is built. Location:
`Assets/VFX/<EffectId>/Design/effect.spec.yaml`.

```yaml
id: ArcaneNova                 # PascalCase, used in every asset name
title: "Arcane Nova impact"
archetype: impact              # impact | toon_explosion | burst | pickup | aura | buff | projectile | custom (see archetypes.md)
style: stylized_hand_painted   # stylized_hand_painted | anime_cel | moba_readable
brief: >
  Purple-gold spell impact: brief inward pull, then a white flash, a broken ring
  shockwave and gold sparks, leaving drifting purple embers.
references:                    # optional; images the user gave, with what to take from each
  - path: Assets/VFX/ArcaneNova/Design/refs/ref_01.png
    take: "broken ring edge, spark density"
reference_analysis:            # when references were measured with vfxref.py (reference-analysis skill)
  sheet: Assets/VFX/ArcaneNova/Design/refs/analysis/reference_sheet.png
  json: Assets/VFX/ArcaneNova/Design/refs/analysis/reference.json
  times_estimated: true        # screenshots: times are estimates; GIF/video: exact
  sequence: "white core 7% + yellow 44% (h42) -> peach (h33) -> orange + ink 13% -> ink 27% + dark smoke -> fade"
  follow: [color sequence, ink takeover timing, dome silhouette]   # what the critic checks against the reference"
game:
  context: "enemy hit by player spell, third-person camera ~8 m away"
  scale_meters: 2.5            # diameter at the impact peak
  loop: false
  duration: 1.4                # seconds until the last particle is gone
  backgrounds: [dark, light]   # light if the game has daylight or sand scenes
palette:                       # plain colors; HDR intensity is decided per material later
  core: "#FFF4D6"
  primary: "#9B5CFF"
  accent: "#FFC247"
  dark: "#2A0F4F"              # companion tone for readability on bright ground
beats:
  - { t: 0.00, name: anticipation, note: "motes pulled inward, 0.25 s" }
  - { t: 0.25, name: impact, note: "flash + ring at full size within 2 frames" }
  - { t: 0.40, name: dissipation, note: "sparks decelerate, embers drift up and fade" }
layers:
  - id: pull_motes
    job: anticipation
    window: [0.00, 0.28]
    look: "small purple motes spiralling into the center"
    color: primary
  - id: flash
    job: impact core
    window: [0.25, 0.40]
    look: "4-point white-gold star, huge for 2 frames then collapses"
    color: core
  - id: ring
    job: impact shape
    window: [0.25, 0.70]
    look: "broken stylized ring expanding to full scale, eroding at the end"
    color: primary
  - id: sparks
    job: secondary motion
    window: [0.26, 0.90]
    look: "fast gold streaks, fast then hang, gravity"
    color: accent
  - id: embers
    job: aftermath
    window: [0.35, 1.40]
    look: "slow purple embers drifting up with noise"
    color: primary
readability:
  dark_companion: true         # alpha-blended dark layer so it reads on light ground
budget:
  max_particles: 300
```

Rules:
- Every layer has one **job** and a **window**. The Critic checks that each layer is
  alive in its window (`systemParticleCounts`).
- Keep 3–7 layers. More layers make a muddier effect, not a better one.
- `palette` names are reused as `$name` in particle recipes.
- Capture times are derived from the beats: 0, every beat, beat + 2 frames (+0.034 s:
  the impact must be at full size there), and the end of every window. Captures run at
  60 fps: times closer than 0.017 s fall on the same frame and are captured once.
- **White heroes.** A white core is right for the 1–3 frame flash; a white layer that
  lasts longer reads as washed out (and the capture flags it). Make the core a warm or
  cool off-white (`#FFF4D6`, not `#FFFFFF`), keep it small, and give a long-lived white
  hero shape (a white star, a white ring) a saturated rim or companion so the palette
  survives. Say in the layer's `look` when white is intended, so the Critic scores it
  against the intent.
