"""Finish the authorized 20260922 weight revision on the staged action file.

Run after build_pet01_exclusive_actions.py --phase build. Never overwrites the
canonical source. The pre-smoothing staged file is archived once for reproducibility.
"""
import hashlib
import json
import shutil
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01_exclusive_actions as ex


def main():
    input_file = ex.STAGED
    provenance_path = ex.WORK / 'source_provenance.json'
    if json.loads(provenance_path.read_text()).get('shape_revision'):
        raise RuntimeError('Staged weights are already revised; rebuild actions from the baseline before smoothing')
    archive = ex.WORK / ('weight_revision_input_' + ex.sha(input_file)[:16] + '.blend')
    if archive.exists() and ex.sha(archive) != ex.sha(input_file):
        raise RuntimeError('Weight revision archive collision')
    if not archive.exists():
        shutil.copy2(input_file, archive)
    bpy.ops.wm.open_mainfile(filepath=str(input_file))
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    count = len(mesh.data.vertices)
    xyz = np.array([v.co[:] for v in mesh.data.vertices])
    edges = np.array([e.vertices[:] for e in mesh.data.edges])
    lengths = np.linalg.norm(xyz[edges[:, 0]] - xyz[edges[:, 1]], axis=1)
    src = np.concatenate([edges[:, 0], edges[:, 1]])
    dst = np.concatenate([edges[:, 1], edges[:, 0]])
    coefficients = 1 / np.maximum(np.concatenate([lengths, lengths]), .0001)
    denominator = np.bincount(src, weights=coefficients, minlength=count)
    weights = np.zeros((count, len(mesh.vertex_groups)))
    for vertex in mesh.data.vertices:
        for group in vertex.groups:
            weights[vertex.index, group.group] = group.weight
    original = weights.copy()
    # Includes hip/torso boundary so smoothing does not create a new hard seam.
    # Feet and facial components stay outside the edited region.
    mask = (xyz[:, 2] > .045) & (xyz[:, 2] < .175)
    for _ in range(200):
        sums = np.zeros_like(weights)
        np.add.at(sums, src, weights[dst] * coefficients[:, None])
        weights[mask] = (.5 * weights + .5 * sums / np.maximum(denominator[:, None], 1e-10))[mask]
    keep = np.argsort(weights, axis=1)[:, -8:]
    final = np.zeros_like(weights)
    np.put_along_axis(final, keep, np.take_along_axis(weights, keep, axis=1), axis=1)
    final[final < 1e-5] = 0
    final /= final.sum(axis=1)[:, None]
    for vertex in mesh.data.vertices:
        if not mask[vertex.index]:
            continue
        for group in list(vertex.groups):
            mesh.vertex_groups[group.group].remove([vertex.index])
        for gi in np.where(final[vertex.index] > 0)[0]:
            mesh.vertex_groups[int(gi)].add([vertex.index], float(final[vertex.index, gi]), 'REPLACE')
    # Staged .blend lives one directory deeper than the runtime source.
    bpy.data.images['Pet01_BaseColor'].filepath = str(ex.base.TEXTURE_DIR / 'Pet01_BaseColor.png')
    ex.configure_validation()
    ex.validation.set_action(rig, 'calm_zen', 1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(ex.STAGED))
    provenance = json.loads((ex.WORK / 'source_provenance.json').read_text())
    provenance['pre_revision_fingerprint'] = provenance['frozen_data_fingerprint']
    provenance['frozen_data_fingerprint'] = ex.fingerprint(rig, mesh)
    provenance['shape_revision'] = '20260922_arm_belly_weight_transition'
    provenance['note'] = 'User-authorized weight revision; geometry, UV, rest bones and old Action curves preserved. Up to 8 influences.'
    (ex.WORK / 'source_provenance.json').write_text(json.dumps(provenance, indent=2)+'\n')
    report = {
        'shape_revision': provenance['shape_revision'],
        'input_sha256': ex.sha(archive), 'staged_sha256': ex.sha(ex.STAGED),
        'method': '200 inverse-edge-length Laplacian iterations, alpha .5, z .045..175m, normalized top 8',
        'changed_vertices': np.where(mask & (np.max(np.abs(final-original), axis=1)>1e-6))[0].tolist(),
        'max_influences': max(len(v.groups) for v in mesh.data.vertices),
        'canonical_promotion': 'pending numeric and visual validation',
    }
    (ex.WORK / 'weight_revision.json').write_text(json.dumps(report, indent=2)+'\n')
    ex.validation.export()


if __name__ == '__main__':
    main()
