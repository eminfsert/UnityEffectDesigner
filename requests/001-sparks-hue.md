# 001: Sparks hue under linear-stop intensity, with and without the game's tonemapping

## Why

Your measurement: after the tint patch, the sparks' hue was 55-57° (lemon yellow), not the
35-45° gold I expected. Your analysis was right, and I made two fixes in 0.3.3 (`1d28783`):

1. `intensity` is now in stops of **linear light** (Unity's HDR picker convention), so +1
   stop doubles the linear value. Before, 2^I was applied to the gamma value, which made
   the brightest channel about x2.3 in linear.
2. `vfx_capture_timeline` has a `volume_profile` parameter, so a capture can use the game
   scene's tonemapping and bloom.

0.3.3 is not installed, so this test simulates fix 1 with 0.3.1: the colors below are the
**gamma values 0.3.3 would write**, given as `[r, g, b, a]` arrays. 0.3.1 writes arrays to
materials unchanged, so the materials end up exactly as 0.3.3 would set them. Fix 2 cannot
run on 0.3.1; step 3 covers it only if it fits within your limits.

## Steps

Target: the test prefab you used before (`VFX_TestImpact`); set `target` to its path.
After each apply, run a capture: same view (three_quarter/dark) and the same Color table
as last time (coverage / washedOut / saturation / hue, plus the per-system particle counts).

### 1. Linear-stop version of the previous patch (same colors and intensities as last time)

Colors: `#FFD9A0 @1`, `#FFB030 @1`, `#FF9A2E @0.75`, edge `#FF5A1F @1.5`.

```json
{
  "target": "Assets/VFX/Test/VFX_TestImpact.prefab",
  "systems": [
    { "name": "Flash", "renderer": { "material": { "path": "Assets/VFX/Test/M_Flash.mat",
        "properties": { "_TintColor": [1.3533, 1.1543, 0.856, 1] } } } },
    { "name": "Sparks", "renderer": { "material": { "path": "Assets/VFX/Test/M_Sparks.mat",
        "properties": { "_TintColor": [1.3533, 0.9397, 0.2697, 1] } } } },
    { "name": "SparkPops", "renderer": { "material": { "path": "Assets/VFX/Test/M_SparkPops.mat",
        "properties": { "_TintColor": [1.2552, 0.7633, 0.2373, 1], "_EdgeColor": [1.572, 0.5741, 0.2173, 1] } } } }
  ]
}
```

My estimate for Sparks under tonemapping None: ~40-45°. Not measured.

### 2. Sparks-only variants (after step 1; Flash and SparkPops stay as they are)

2a. `#FF8A1A @0.5` (redder tint):
```json
{ "target": "Assets/VFX/Test/VFX_TestImpact.prefab",
  "systems": [ { "name": "Sparks", "renderer": { "material": { "path": "Assets/VFX/Test/M_Sparks.mat",
      "properties": { "_TintColor": [1.1639, 0.6338, 0.1263, 1] } } } } ] }
```

2b. `#FFB030 @0` (no HDR boost, for comparison):
```json
{ "target": "Assets/VFX/Test/VFX_TestImpact.prefab",
  "systems": [ { "name": "Sparks", "renderer": { "material": { "path": "Assets/VFX/Test/M_Sparks.mat",
      "properties": { "_TintColor": [1.0, 0.6902, 0.1882, 1] } } } } ] }
```

### 3. Game tonemapping (only if it fits within your limits)

Repeat the step 1 capture with the game's post-processing (IslandPostProcess, Neutral). On
0.3.1 the only way is to open or add the Island scene **without modifying or saving it**,
so its global volume applies to the capture. If that is outside your limits, skip this
step and just note which profile/tonemapping the default capture used. Once 0.3.3 is
installed, the right way is `vfx_capture_timeline` with
`volume_profile: "<path of IslandPostProcess>"`. Please also tell me that path.

## What to send back

For each step: the Color table, the particle counts, and one line on what the contact
sheet shows (gold vs lemon, bloom, light-background readability). For step 3, also the
tonemapping used. For anything that does not match my estimates, include your reading of
why.
