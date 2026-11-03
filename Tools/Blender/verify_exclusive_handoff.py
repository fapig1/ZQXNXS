"""Verify the installed preview against Blender evidence without running Blender."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'ArtSource/Characters/Pet01/ExclusiveActions'
ART = ROOT / 'ARPet/Assets/_Project/Art/Characters/Pet01'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def unique(items):
    result = {}
    for key, value in items:
        if key in result:
            raise ValueError('Duplicate JSON key: ' + key)
        result[key] = value
    return result


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def main():
    source = ROOT / 'ArtSource/Characters/Pet01/pet01.blend'
    source_hash = sha(source)
    assert source_hash == sha(WORK / 'pet01_exclusive.blend')
    for manifest in (source.parent / 'asset_manifest.json', ART / 'asset_manifest.json'):
        assert load(manifest)['source_sha256'] == source_hash
    assert load(ART / 'final_manifest.json')['frozen']['source_sha256'] == source_hash
    assert load(WORK / 'visual_acceptance.json')['staged_sha256'] == source_hash
    installed = {}
    for report_name in ('validation_report.json', 'old_actions_regression.json'):
        report = load(WORK / report_name)
        assert report['staged_sha256'] == source_hash
        assert report.get('passed') or report.get('no_new_failures')
        for name, expected in report['exports_sha256'].items():
            exported = sha(WORK / 'Exports' / name)
            runtime = sha(ART / 'Models' / name)
            assert exported == expected
            assert runtime == exported, f'Unity export mismatch: {name}'
            installed[name] = {'current_export_sha256': exported, 'unity_sha256': runtime,
                               'byte_identical': exported == runtime}
    inventory = load(ROOT / 'Docs/四色奶蛙资产元数据.json')
    targets = {node.attrib['name'] for node in ET.parse(ROOT / inventory['database_path']).iter('ImageTarget')}
    assert len({v['pet_id'] for v in inventory['variants']}) == 4
    for variant in inventory['variants']:
        assert variant['target_id'] in targets
        for kind, directory in [('png', 'card_directory'), ('jpg', 'card_directory'), ('texture', 'texture_directory')]:
            item = variant[kind]
            assert sha(ROOT / inventory[directory] / item['file']) == item['sha256']
        assert sha(ART / 'Textures' / variant['texture']['file']) == variant['texture']['sha256']
    docs = [ROOT / p for p in ('AGENTS.md', 'ARPet/README.md', 'Docs/README.md',
            'Docs/四色奶蛙资产映射.md', 'Docs/四色专属动作接入记录_20260922.md',
            'Docs/奶蛙最终版资产说明.md', 'Docs/项目架构与技术架构.md',
            'ArtSource/Characters/Pet01/README.md',
            'ARPet/Assets/_Project/Art/Characters/Pet01/README.md',
            'ARPet/Assets/_Project/Scenes/README.md', 'ARPet/Assets/_Project/Configs/README.md')]
    for doc in docs:
        for link in re.findall(r'\]\(([^)]+)\)', doc.read_text(encoding='utf-8')):
            if '://' in link or link.startswith('#'):
                continue
            assert (doc.parent / link.split('#')[0]).exists(), f'{doc}: {link}'
    output = {'date': '2026-09-22', 'source_sha256': source_hash,
              'installed_files': installed, 'inventory_and_links_passed': True}
    (WORK / 'handoff_verification.json').write_text(json.dumps(output, indent=2) + '\n')
    print(json.dumps(output, indent=2))


if __name__ == '__main__':
    main()
