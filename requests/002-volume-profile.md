# 002: Test volume_profile with the real tool (after 0.3.5 is installed)

**Prerequisite:** the user installs 0.3.5 (0.3.4 plus your two notes). Until then this request waits. Do not look for
another way to install it.

## Thank you, and what changed

Your 001 results (hue 56° → 41–44°) confirmed the linear-stop fix. Your two corrections were
right too, and they are fixed in 0.3.4:
- The capture was never without tonemapping: Neutral came from the quality profile
  (`SampleSceneProfile`). I fixed the wrong "tonemapping None" wording in the docs; the
  1d28783 commit message stays as it is (history is not rewritten).
- Volumes in open scenes never reach the capture (`StageUtility`). I fixed the comments,
  the skill and the warning. The warning now says only the pipeline defaults
  (global + quality) apply.
- `DescribePostProcessing` reads the camera's own `volumeStack` first, falling back to
  the shared stack.
- New: `ProjectSettings/EffectDesigner.json` → `{"volume_profile": "..."}`. Every capture
  (MCP and menu) uses it by default. It can be set from the menu: select the profile, then
  **Assets → Effect Designer → Use As Capture Volume Profile**.
- Your calibration table went into the `vfx-shaders` skill.
- Per-system colorStats went into the backlog; it will come later.
- 0.3.5 (your notes on 8468d0e): `volume_profile: "none"` renders with the pipeline
  defaults only even when a project default is set; the response shows the source in
  `postProcessing.volumeProfileSource`. A profile path that no longer exists fails
  before the capture starts, and the message names where the path came from (parameter
  or project settings) and how to fix it.

## Steps

1. **Via MCP** (does not touch project settings): call `vfx_capture_timeline` with
   `target` = the VFX_TestImpact prefab, `times` = [0, 0.1, 0.25, 0.5, 0.8, 2.5],
   `volume_profile` = `Assets/Settings/IslandPostProcess.asset`.
   - Check the `postProcessing` field in the response: I expect Neutral tonemapping
     and bloom 0.95 / 0.85.
   - Compare the three_quarter/dark Color table with your 001 step 3 table (the
     SetCustomDefaultProfiles method). It should match within measurement noise.
   - With no volume_profile passed, the warning should say "pipeline's default volume
     profiles only".
   - With `volume_profile: "none"`: `volumeProfileSource` = "none (explicit)", no warning,
     and the table should match your 001 step 1 table (0.1s 41°/0.52, 0.25s 44°/0.57).
2. **Project setting (only with the user's approval, because it writes to
   `ProjectSettings/`):** select IslandPostProcess, then **Use As Capture Volume Profile**,
   then run a menu capture (Capture Timeline Of Selection). The log's "Post-processing:"
   line should show the profile.

## What to send back

The `postProcessing` field, the Color table, any warnings, and whether it matches the
001 step 3 table.
