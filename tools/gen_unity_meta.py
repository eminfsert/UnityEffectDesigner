#!/usr/bin/env python3
"""Create missing Unity .meta files for a UPM package.

Packages installed from git are immutable, so Unity ignores any asset without a
committed .meta file. GUIDs are derived from the package-relative path, so running
this again never changes existing GUIDs and a moved file gets a new one.

Usage: tools/gen_unity_meta.py unity-package/com.effectdesigner.vfxtoolkit
"""
import hashlib
import sys
from pathlib import Path

FOOTER = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

TEMPLATES = {
    "folder": "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + FOOTER,
    ".cs": (
        "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
        "  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n" + FOOTER
    ),
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + FOOTER,
    ".json": "TextScriptImporter:\n  externalObjects: {}\n" + FOOTER,
    ".md": "TextScriptImporter:\n  externalObjects: {}\n" + FOOTER,
    ".txt": "TextScriptImporter:\n  externalObjects: {}\n" + FOOTER,
    ".hlsl": "ShaderIncludeImporter:\n  externalObjects: {}\n" + FOOTER,
    ".shader": (
        "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n"
        "  nonModifiableTextures: []\n" + FOOTER
    ),
}


def guid_for(package_name: str, rel_path: str) -> str:
    return hashlib.md5(f"{package_name}/{rel_path}".encode("utf-8")).hexdigest()


def main(package_dir: str) -> int:
    root = Path(package_dir).resolve()
    package_name = root.name
    created = 0
    for path in sorted(root.rglob("*")):
        if path.suffix == ".meta" or any(part.startswith(".") for part in path.relative_to(root).parts):
            continue
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        kind = "folder" if path.is_dir() else path.suffix.lower()
        template = TEMPLATES.get(kind)
        if template is None:
            print(f"skip (no template): {path.relative_to(root)}", file=sys.stderr)
            continue
        rel = path.relative_to(root).as_posix()
        meta.write_text(f"fileFormatVersion: 2\nguid: {guid_for(package_name, rel)}\n{template}", encoding="utf-8")
        print(f"created {meta.relative_to(root)}")
        created += 1
    print(f"{created} meta file(s) created")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
