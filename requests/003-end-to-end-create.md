# 003: End-to-end /vfx:create test (agents + orchestration)

**Prerequisites (need the user's approval, they change the Claude Code setup):**
1. Install the plugin:
   ```
   /plugin marketplace add eminfsert/UnityEffectDesigner@claude/trusting-ritchie-uze78h
   /plugin install vfx@unity-effect-designer
   ```
2. The package is already current (4ae184f; this commit does not change package code).
3. Texture generation needs Python + numpy + Pillow. If they are missing, the agents should
   fall back to C#. Seeing whether they do is also part of the test.

## Test

Run: `/vfx:create a small gold coin-pickup effect: a quick star glint, a ring pop and a few
glints drifting up, 0.6 s, played in the Island scene`

- At the concept board checkpoint, show it to the user and get their approval. If the user
  has explicitly delegated that decision, you may approve it for the test, and say so in
  the report.
- Let the flow run to completion (at most 3 critic rounds).
- The expected output folder is `Assets/VFX/<Id>/`, which is inside your limits.
  Anything that touches another folder or the scene is a finding.

## What to send back

1. Which agents ran, in what order, and whether the parallel ones really ran in parallel.
2. Any tool errors, and places where the instructions (skill or agent files) were wrong,
   missing or contradictory. **This matters most.** Include the file name and what should
   change.
3. The critic's score per round and whether the fixes improved the numbers
   (systemParticleCounts, colorStats).
4. The final contact sheet path and your own honest judgement of the effect (compared with
   the brief).
5. Rough duration, and token use if you can see it.
