# Effect archetypes

Proven layer stacks to start from. Adapt them to the brief. They are skeletons, not
presets: every layer still gets the brief's shapes, colors and timing.

## impact (one-shot hit, explosion, spell landing)
| Layer | Job | Window (s) | Technique |
|---|---|---|---|
| flash | impact core | 0.00–0.15 | 1 particle, star/glow mask, `spike` size, additive HDR |
| shape | impact shape | 0.00–0.45 | ring/star/slash mask, size `ease_out_expo`, erosion out |
| sparks | secondary motion | 0.01–0.7 | burst 20–50, stretch, dampen, gravity |
| smoke/dust | body | 0.05–1.0 | 3–8 flipbook puffs, alpha blend, `fixed` gradient |
| embers | aftermath | 0.1–1.5 | slow, noise, fade |
| dark companion | readability | same as shape | alpha-blended darker duplicate of the hero shape, slightly larger |

## toon explosion (strong stylized blast; opaque toon shapes, reads on bright daylight ground)
| Layer | Job | Window (s) | Technique |
|---|---|---|---|
| dome | impact shape | 0.00–0.45 | `vfx_make_mesh` dome, mesh particle, Stylized Shell: ramp near-white → yellow → peach → orange over life (hard steps), orange rim, dark back faces; pops in 2–3 frames (`ease_out_back`); `stripes` erosion breaks it into strips |
| hot debris | secondary motion | 0.00–0.30 | flame tongues and small shards flung out, alpha blend, gravity |
| speed dashes | impact accent | 0.00–0.15 | short white streaks on an arc around the dome top |
| puffs | body | 0.25–0.70 | 4–8 toon puffs (`puff` texture, dark strokes in RGB) around the base, orange → darker |
| ink | cool-down | 0.35–1.20 | black `arc` slashes standing up + `swirl` curls, alpha blend, rotating, fading |
| dark smoke | aftermath | 0.30–1.50 | translucent dark dome or puffs (Stylized Shell with low opacity, or alpha puffs), slow erosion |
| embers | aftermath | 0.30–1.50 | tiny yellow specks, slow, fade |

Capture on the game's ground color (`ground`) and a light background: most layers are
alpha-blended and readable by value, not glow.

## burst / pickup (reward, collect, small pop; archetype `burst` or `pickup`)
flash (small) → radial sparkle stars (4–8, rotating) → ring pop (`pop` curve) → glints
drifting up. Short: 0.4–0.8 s. Bright, saturated and friendly shapes.

## aura (looping, on a character or object)
Loop = true, prewarm on. base glow/decal ring (slow rotation) → rising motes (rate over
time, noise) → occasional sparkle bursts (bursts with cycles/interval) → soft outer
shell. Capture over one loop period plus the first frame after prewarm.

## buff (applied status, one-shot then short loop)
Apply burst (like pickup) → looping aura layer at lower intensity. Build it as two
root systems: `Apply` (one-shot) and `Loop`.

## projectile (moving bolt)
Head (glow + star, local space) → trail (trails module or stretched particles, **world
space**) → sparks shed along the path (rate over distance) → impact is a separate effect.
Captures are static, so check the head and trail shapes and test the motion in Play Mode
or with a moving `IVfxTimeSampleable` rig.
