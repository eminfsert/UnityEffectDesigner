# 005: Verify 0.4.2 (fixes from 004)

Commit d68e063. Package and plugin are **0.4.2**.

**Prerequisites (need the user's approval):** update the Unity package to d68e063 and the
plugin to 0.4.2 as in 004, then `/reload-plugins` so the session reads the new skill texts.
Reconnect the MCP client if possible; if not, `execute_custom_tool` is fine again.

Uses the `Assets/VFX/_Test004/` prefab from 004 (if it was deleted, recreate it with the
004 section 2 recipe first).

## 1. Frame times and maxParticleSize

Capture `Assets/VFX/_Test004/VFX_Test004.prefab` with
`times [0, 0.008, 0.017, 0.05, 0.1, 0.105, 0.2]`, `views ["three_quarter"]`,
`backgrounds ["dark"]`, `label "test005_a"`.

- `times` in the result is `[0, 0.017, 0.05, 0.1, 0.2]`, and a note lists 0.008 and 0.105
  as same-frame times.
- The 0 and 0.017 columns now differ (0.017 is the second frame).
- Flash's size animation is visible up close: its coverage changes between 0, 0.017 and
  0.05 (in 004 all three were 0.05076). A note names `Flash` as exceeding its
  maxParticleSize at the capture distance, with the distance below which the game would
  clamp it. Is that distance plausible (1.5 m particle, 35° camera, limit 0.5 → ~4.8 m)?
- The prefab asset's own `maxParticleSize` is unchanged (still 0.5): only the capture copy
  is lifted.

## 2. system_frames

Same capture with `"system_frames": true`, `label "test005_b"`. `systemFrames` has
`Flash` and `Sparks` with a path per time (null where the system had no particles), and the
files exist. Then do the same on the CoinPickup prefab with its manifest capture block
and compare the Ring-alone frame with the composite at 0.1 s: can you now see why the ring
measured 28–35° alone and ~48° in the composite (DarkRing underneath, bloom from the star
or glints, something else)?

## 3. Recipe responses

- A patch that only sets `"order": 1` on Sparks: `applied` lists `order`, and there is no
  late-burst warning.
- A patch that changes `main.start_speed` on Sparks: the late-burst warning appears.
- A rejected recipe (e.g. the 004 `angle_deg` dry run): the error response carries
  `toolkitVersion` 0.4.2 in its data.

## 4. Skill texts

After `/reload-plugins`, does the session see the 0.4.2 texts (e.g. `unity-adapter` says
"toolkit ≥ 0.4.2", `vfx-shaders` has "Rendered hue runs a few degrees yellower")?

## What to send back

Pass/fail per item with the numbers, plus anything wrong or missing in the instructions.
Delete `Assets/VFX/_Test004/` afterwards only if the user agrees.
