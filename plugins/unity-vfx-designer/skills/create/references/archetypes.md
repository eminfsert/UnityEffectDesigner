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

## burst / pickup (reward, collect, small pop)
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
