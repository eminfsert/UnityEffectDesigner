---
name: review
description: Capture and critique any Unity particle effect (made with Effect Designer or not) against stylized VFX principles, with a scored report and concrete fixes. Use when the user asks to review, critique or check an existing effect.
argument-hint: "<prefab path or scene object> [what it is for]"
---

# /vfx:review

Request: $ARGUMENTS

Start the `vfx-critic` agent with the target, the user's description of what the effect is
for, and (if they exist) the effect's `Design/effect.spec.yaml` and `manifest.yaml`. Without
a spec, the critic reviews against `vfx-fundamentals` and infers the intended archetype and
beats from the capture. It reports those assumptions explicitly.

Show the user the contact sheet, the score table and the fix list. Do not apply fixes
unless the user asks. Offer `/vfx:iterate` for Effect Designer effects.
