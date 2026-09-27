#!/usr/bin/env bash
# Downloads Unity reference assemblies (UnityEngine.dll / UnityEditor.dll) used to compile and
# test the VFX Toolkit outside Unity. They come from the Unity3D.SDK NuGet package (Unity 2021.1
# API): good for catching compile and mapping errors, but they do not know about APIs that Unity 6
# made obsolete-as-error, so a real editor compile is still the final check.
set -euo pipefail
cd "$(dirname "$0")"
REFS=.unity-refs
if [[ -f $REFS/lib/UnityEngine.dll && -f $REFS/lib/UnityEditor.dll ]]; then
  exit 0
fi
mkdir -p "$REFS"
curl -sSL -o "$REFS/sdk.nupkg" "https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg"
python3 -c "import zipfile,sys; zipfile.ZipFile('$REFS/sdk.nupkg').extractall('$REFS')"
echo "Unity reference assemblies ready in tests/$REFS/lib"
