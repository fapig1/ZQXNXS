"""Preserve the complete previous asset before the Meshy proportion revision."""
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Characters/Pet01"
TARGET = SOURCE / "Archive/20260910_before_meshy_proportions"
assert TARGET.resolve().is_relative_to(SOURCE.resolve())
if (TARGET / "backup_manifest.json").exists():
    raise SystemExit("Backup already exists; leave it unchanged.")
TARGET.mkdir(parents=True, exist_ok=False)
for name in ("pet01.blend", "Textures", "Previews", "review.html", "README.md",
             "asset_manifest.json", "validation_report.json"):
    src, dst = SOURCE / name, TARGET / name
    if src.is_dir():
        shutil.copytree(src, dst)
    else:
        shutil.copy2(src, dst)
shutil.copytree(ROOT / "ARPet/Assets/_Project/Art/Characters/Pet01", TARGET / "Runtime")
(TARGET / "Scripts").mkdir()
for name in ("build_pet01.py", "validate_pet01.py", "make_pet01_review.py"):
    shutil.copy2(ROOT / "Tools/Blender" / name, TARGET / "Scripts" / name)
for name in ("AGENTS.md", "开题说明_AR萌宠互动系统.md", "Docs/奶蛙首轮资产与动作说明.md",
             "Docs/奶蛙建模与音效资源核查.md", "ArtSource/References/20260908_naiwa_assets/建模参考简报.md"):
    dst = TARGET / "Documents" / name
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / name, dst)
records = [{"file": str(p.relative_to(TARGET)).replace("\\", "/"), "bytes": p.stat().st_size,
            "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
           for p in TARGET.rglob("*") if p.is_file()]
(TARGET / "backup_manifest.json").write_text(json.dumps({
    "reason": "User selected Meshy_AI_Golden_Guardian_0910135714_generate.glb as the new proportion reference.",
    "previous_revision": "20260910_rounder_belly", "files": records
}, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print("Backup complete:", TARGET, "files:", len(records))
