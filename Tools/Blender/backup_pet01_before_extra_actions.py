"""Preserve the frozen final asset before adding TouchReact/DozeLoop/WalkLoop actions.

The mesh and skeleton are not modified by this revision; only three new Actions
(animation clips) are added to the existing armature. Archiving first follows the
same discipline as shape revisions, per Docs/奶蛙最终版资产说明.md 第 7 节.
"""
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Characters/Pet01"
TARGET = SOURCE / "Archive/20260917_before_extra_actions"
assert TARGET.resolve().is_relative_to(SOURCE.resolve())
if (TARGET / "backup_manifest.json").exists():
    raise SystemExit("Backup already exists; leave it unchanged.")
TARGET.mkdir(parents=True, exist_ok=False)
for name in ("pet01.blend", "Textures", "Previews", "review.html", "README.md",
             "asset_manifest.json", "validation_report.json"):
    src, dst = SOURCE / name, TARGET / name
    if not src.exists():
        continue
    if src.is_dir():
        shutil.copytree(src, dst)
    else:
        shutil.copy2(src, dst)
shutil.copytree(ROOT / "ARPet/Assets/_Project/Art/Characters/Pet01", TARGET / "Runtime")
(TARGET / "Scripts").mkdir()
for name in ("build_pet01.py", "validate_pet01.py", "make_pet01_review.py"):
    shutil.copy2(ROOT / "Tools/Blender" / name, TARGET / "Scripts" / name)
for name in ("AGENTS.md", "Docs/奶蛙最终版资产说明.md", "Docs/奶蛙首轮资产与动作说明.md"):
    dst = TARGET / "Documents" / name
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / name, dst)
records = [{"file": str(p.relative_to(TARGET)).replace("\\", "/"), "bytes": p.stat().st_size,
            "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
           for p in TARGET.rglob("*") if p.is_file()]
(TARGET / "backup_manifest.json").write_text(json.dumps({
    "reason": "Adding TouchReact/DozeLoop/WalkLoop Actions to the frozen final rig; mesh and skeleton unchanged.",
    "previous_revision": "20260911_meshy_proportions (final, frozen 2026-09-11)",
    "files": records
}, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print("Backup complete:", TARGET, "files:", len(records))
