#!/usr/bin/env bash
# Offline checks for the VFX Toolkit package (needs the .NET 8 SDK):
#  1. the package compiles with and without MCP for Unity / URP present (warnings are errors)
#  2. particle recipes map onto real Unity module properties, and bad recipes are rejected clearly
set -euo pipefail
cd "$(dirname "$0")"
./setup.sh
# MSBuild needs ";" escaped as %3B inside -p values.
for defines in "VFXTOOLKIT_MCP%3BVFXTOOLKIT_URP" "NONE"; do
  echo "== compile package (defines: $defines)"
  dotnet build PackageCompile -nologo -v q "-p:Defines=$defines" | grep -E "error|Build succeeded" | sort -u
done
echo "== recipe mapping"
dotnet run --project RecipeMapping -nologo -v q
echo "== texture generator (vfxtex.py)"
if python3 -c "import numpy, PIL" 2>/dev/null; then
  out=$(mktemp -d)
  T=../plugins/unity-vfx-designer/skills/texture-authoring/scripts/vfxtex.py
  for cmd in "glow $out/glow.png" "ring $out/ring.png --breaks 5" "star $out/star.png --points 6 --steps 3" \
             "streak $out/streak.png" "slash $out/slash.png" "noise $out/noise.png --size 64" \
             "smoke $out/smoke.png --size 64 --steps 3" "swirl $out/swirl.png" "shard $out/shard.png" \
             "flame $out/flame.png" "puff $out/puff.png" "stripes $out/stripes.png --size 128"; do
    python3 "$T" $cmd > /dev/null
  done
  python3 "$T" preview "$out/preview.png" "$out"/glow.png "$out"/ring.png > /dev/null
  python3 - "$out" <<'PY'
import sys
from PIL import Image
out = sys.argv[1]
checks = {"glow": (128, 128), "ring": (256, 256), "star": (256, 256), "streak": (512, 128),
          "slash": (256, 256), "noise": (64, 64), "smoke": (256, 256), "swirl": (256, 256),
          "shard": (256, 256), "flame": (256, 256), "puff": (256, 256), "stripes": (128, 128)}
bad = []
for name, size in checks.items():
    im = Image.open(f"{out}/{name}.png")
    a = im.getchannel("A")
    lo, hi = a.getextrema()
    if im.size != size or hi == 0 or (name not in ("noise", "stripes") and lo != 0):
        bad.append(f"{name}: size {im.size}, alpha {lo}-{hi}")
    # Masks must not touch the border (visible cut-off on particles).
    if name not in ("noise", "smoke", "streak", "stripes"):
        w, h = im.size
        border = [a.getpixel((x, 0)) for x in range(w)] + [a.getpixel((x, h - 1)) for x in range(w)]
        if max(border) > 8:
            bad.append(f"{name}: shape touches the border")
print("PASS vfxtex: all shapes generated, correct sizes, masks clear of the border" if not bad else "FAIL vfxtex: " + "; ".join(bad))
sys.exit(1 if bad else 0)
PY
  rm -rf "$out"
else
  echo "skip (numpy/Pillow not installed)"
fi
echo "== reference analysis (vfxref.py)"
if python3 -c "import numpy, PIL" 2>/dev/null; then
  out=$(mktemp -d)
  R=../plugins/unity-vfx-designer/skills/reference-analysis/scripts/vfxref.py
  python3 - "$out" <<'PY'
import sys, numpy as np
from PIL import Image
out = sys.argv[1]
yy, xx = np.mgrid[0:120, 0:160]
disk = (xx - 80) ** 2 + (yy - 60) ** 2 < 40 ** 2
ring = disk & ((xx - 80) ** 2 + (yy - 60) ** 2 > 30 ** 2)
import os
os.makedirs(f"{out}/cap", exist_ok=True)
for i, (shape, color) in enumerate([(disk, (255, 230, 150)), (disk, (240, 150, 60)), (ring, (15, 12, 10))]):
    ref = np.zeros((120, 160, 3), np.uint8); ref[:] = (70, 140, 50)          # grass
    ref[shape] = color
    Image.fromarray(ref).save(f"{out}/ref{i}.png")
    cap = np.zeros((120, 160, 3), np.uint8); cap[:] = (78, 138, 58)           # capture on a ground color
    cap[shape] = color
    Image.fromarray(cap).save(f"{out}/cap/three_quarter_dark_t{[0, 0.2, 0.5][i]:.3f}.png")
PY
  python3 "$R" sheet "$out/s" --inputs "$out"/ref0.png "$out"/ref1.png "$out"/ref2.png --times 0,0.2,0.5 > /dev/null
  python3 "$R" compare "$out/c" --reference "$out/s/reference.json" --capture "$out/cap" --background-name dark --background "#4E8A3A" > /dev/null
  python3 - "$out" <<'PY'
import sys, json
out = sys.argv[1]
ref = json.load(open(f"{out}/s/reference.json"))
cmp = json.load(open(f"{out}/c/compare.json"))
f = ref["frames"]
bad = []
if not (f[0]["white"] > 0.9 or f[0]["bright"] > 0.9): bad.append(f"frame 0 should be white/bright: {f[0]}")
if not (f[1]["bright"] > 0.9 and 20 <= f[1]["bright_hue"] <= 35): bad.append(f"frame 1 should be orange: {f[1]}")
if not (f[2]["ink"] > 0.9): bad.append(f"frame 2 should be ink: {f[2]}")
if abs(f[1]["coverage"] - 0.26) > 0.03: bad.append(f"grass not excluded: coverage {f[1]['coverage']}")
s = cmp["summary"]
if s["mean_abs_bright_hue_difference"] not in (0, 0.0) or max(s["mean_abs_share_difference"].values()) > 0.02: bad.append(f"identical frames should compare equal: {s}")
print("PASS vfxref: grass excluded, white/orange/ink classified, identical capture compares equal" if not bad else "FAIL vfxref: " + "; ".join(bad))
sys.exit(1 if bad else 0)
PY
  rm -rf "$out"
else
  echo "skip (numpy/Pillow not installed)"
fi
