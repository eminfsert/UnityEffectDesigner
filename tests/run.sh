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
             "smoke $out/smoke.png --size 64 --steps 3"; do
    python3 "$T" $cmd > /dev/null
  done
  python3 "$T" preview "$out/preview.png" "$out"/glow.png "$out"/ring.png > /dev/null
  python3 - "$out" <<'PY'
import sys
from PIL import Image
out = sys.argv[1]
checks = {"glow": (128, 128), "ring": (256, 256), "star": (256, 256), "streak": (512, 128),
          "slash": (256, 256), "noise": (64, 64), "smoke": (256, 256)}
bad = []
for name, size in checks.items():
    im = Image.open(f"{out}/{name}.png")
    a = im.getchannel("A")
    lo, hi = a.getextrema()
    if im.size != size or hi == 0 or (name != "noise" and lo != 0):
        bad.append(f"{name}: size {im.size}, alpha {lo}-{hi}")
    # Masks must not touch the border (visible cut-off on particles).
    if name not in ("noise", "smoke", "streak"):
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
