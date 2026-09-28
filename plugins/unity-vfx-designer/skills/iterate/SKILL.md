---
name: iterate
description: Change an existing Effect Designer VFX from natural-language feedback ("more aggressive", "fade out slower", "more purple", "reads badly on sand") by translating it into spec changes and small patch recipes, then re-capturing. Use when the user gives feedback on an effect made with /vfx:create.
argument-hint: "<effect id or prefab path> <feedback>"
---

# /vfx:iterate

Request: $ARGUMENTS

You are the Director again (see the `create` skill for the team and rules). Load
`unity-adapter` and `vfx-fundamentals`.

1. Find the effect: `Assets/VFX/<Id>/Design/` (spec, manifest, latest review, log). If the
   id is ambiguous, list the effects under `Assets/VFX/` and ask.
2. **Translate the feedback into concrete deltas** and show them in one short table before
   changing anything, for example:

   | Feedback | Change | Owner |
   |---|---|---|
   | "more aggressive" | impact 2 frames earlier, sparks speed ×1.4, sharper star texture | particle, texture |
   | "fade slower" | embers window end 1.4 → 2.0 s, alpha curve `ease_in_quad` | particle |
   | "more purple" | tints and palette primary shift toward #8A4DFF; flash stays core | particle (materials) |

   Ask only when a delta is a real creative fork. Otherwise proceed.
3. Update `effect.spec.yaml` (and `manifest.yaml` through the architect if names, layers or
   contracts change).
4. Route the changes to the owners as **patch recipes** (only the systems and keys that
   change). Use `"reset": true` only when a layer changes concept.
5. Run `vfx-critic` for one review round with the same capture block, so the before and
   after are comparable. Show both contact sheets (previous and new) and the score change.
6. Append to `Design/log.md`.
