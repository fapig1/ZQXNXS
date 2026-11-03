"""Re-author calm_zen as a gassho (palms together) meditation on a staged copy of Pet01.

The 09-22 calm_zen kept both arms on the belly because the old shell fused hands and
belly; with the 20260928_arm_separation doll-joint arms the hands can rise. Only the
calm_zen Action changes: mesh, UV, weights, rest skeleton and the other nine Actions
stay byte-identical (checked by fingerprint). Stable ID, 76 frames / 2.5s and the
Unity take name are unchanged. Phases:
  D:/blender.exe -b --factory-startup --python-exit-code 1 --python this_file -- --phase probe
  ... --phase build | validate | preview | promote
Promotion requires numeric validation plus a visual review naming the staged hash.
"""
import argparse
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as base
import build_pet01_extra_actions as validation

NAME = 'calm_zen'
LAST = 76  # (76-1)/30 = 2.5s, unchanged so Unity clip settings and tests stay valid
CLIPS = {NAME: (LAST, False)}
WORK = base.SOURCE / 'CalmGassho'
STAGED = WORK / 'pet01_calm_gassho.blend'
EXPORTS = WORK / 'Exports'
REPORT = WORK / 'validation_report.json'
ARCHIVE = base.SOURCE / 'Archive' / '20261026_before_calm_gassho'
SOURCE_SHA = '35c5625db06f13de88510edab883ab87293bbfee9f4464404ba871a21f12b13d'  # 20260928_arm_separation
# Gassho pose of the .L arm as armature-space rotations, mirrored for .R:
# UpperArm rotation vector (degrees), elbow bend, wrist bend, hand roll, forearm twist.
# Found by a read-only search on the skinned mesh (2026-10-26): palms face each other
# ~3.6mm apart in front of the chest, fingers up/forward, arm edge compression 0.296,
# no forearm/hand vertex inside the torso shell.
GASSHO = (23.88, 48.8, -9.41, 8.1, -52.26, 22.66, -77.74)
NODS = (8, 6, 4)  # decreasing nod depth, degrees (design v0.1 section 2.2)
SWING_LIFT = 25  # degrees, peak forward shoulder lift on the rise/release path
STRETCH_LIMIT = 2.
COMPRESS_LIMIT = .25
# Forearm/hand vertices may not go deeper than this beyond the rest pose: at rest the
# hands already sit ~2.6mm into the belly by design (arm-separation revision).
PENETRATION_DEPTH = .0005


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def curve(t, keys):
    for (a, x), (b, y) in zip(keys, keys[1:]):
        if t <= b:
            u = max(0., min(1., (t-a)/(b-a)))
            return x + (y-x)*u*u*(3-2*u)
    return keys[-1][1]


def turn(bone, axis, degrees):
    """Compose an armature-space rotation onto the bone's current rotation."""
    local = bone.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
    q = bone.rotation_euler.to_quaternion() @ Quaternion(local, math.radians(degrees))
    bone.rotation_euler = q.to_euler('XYZ', bone.rotation_euler)


def arm_deltas(rig, amount):
    bones = rig.data.bones

    def axis(n):
        return (bones[n].tail_local-bones[n].head_local).normalized()
    u0, f0, h0 = axis('UpperArm.L'), axis('Forearm.L'), axis('Hand.L')
    elbow, wrist = u0.cross(f0).normalized(), f0.cross(h0).normalized()
    g = [v*amount for v in GASSHO]
    v = Vector(g[:3])
    ru = Quaternion(v.normalized(), math.radians(v.length)) if v.length > 1e-9 else Quaternion()
    rf = ru @ Quaternion(elbow, math.radians(g[3])) @ Quaternion(f0, math.radians(g[6]))
    rh = rf @ Quaternion(wrist, math.radians(g[4])) @ Quaternion(h0, math.radians(g[5]))
    return ru.to_matrix(), rf.to_matrix(), rh.to_matrix()


def set_arms(rig, amount):
    """Write the arm chain as local rotations relative to the (possibly posed) Chest."""
    ru, rf, rh = arm_deltas(rig, amount)
    flip = Matrix.Diagonal((-1, 1, 1))
    for side in 'LR':
        r = {'UpperArm.': ru, 'Forearm.': rf, 'Hand.': rh}
        if side == 'R':
            r = {k: flip @ m @ flip for k, m in r.items()}
        parent = {'UpperArm.': Matrix.Identity(3), 'Forearm.': r['UpperArm.'], 'Hand.': r['Forearm.']}
        for k in r:
            rest = rig.data.bones[k+side].matrix_local.to_3x3()
            basis = rest.inverted() @ parent[k].inverted() @ r[k] @ rest
            rig.pose.bones[k+side].rotation_euler = basis.to_euler('XYZ')


def pose(rig, t):
    base.reset_pose(rig)
    b = rig.pose.bones
    # Hands rise from the belly to the chest, hold through the nods, return.
    # Rise/release each take ~0.6s to stay under the 10mm/frame smoothness bound.
    join = curve(t, [(0, 0), (.06, 0), (.68, 1), (1.8, 1), (2.47, 0), (2.5, 0)])
    set_arms(rig, join)
    # The straight blend from rest drags the forearms ~6mm through the belly mid-path;
    # a temporary forward shoulder lift carries the hands over it (probe 2026-10-26).
    lift = SWING_LIFT*math.sin(math.pi*join)
    for side in 'LR':
        turn(b['UpperArm.'+side], (1, 0, 0), lift)
    # Eyes close slowly once the palms meet and reopen as the hands lower.
    closing = curve(t, [(0, 0), (.4, 0), (.78, 1), (1.92, 1), (2.28, 0), (2.5, 0)])
    for side in 'LR':
        b['LidUpper.'+side].rotation_euler.y = -math.radians(78)*closing
        b['LidLower.'+side].rotation_euler.y = math.radians(78)*closing
    # Inhale as the hands rise (chest lifts back), exhale through the nods.
    breath = curve(t, [(0, 0), (.2, 0), (.65, 1), (.8, 1), (1.9, .2), (2.3, 0), (2.5, 0)])
    # Three slow nods of decreasing depth (+X pitch leans back, so a bow is negative).
    a, c, e = NODS
    nod = curve(t, [(0, 0), (.8, 0), (.98, -a), (1.12, -1), (1.3, -c), (1.44, -.5),
                    (1.6, -e), (1.78, 0), (2.5, 0)])
    head = 3*breath + nod
    torso = 1.5*breath + .3*nod
    turn(b['Spine'], (1, 0, 0), torso*.5)
    turn(b['Chest'], (1, 0, 0), torso*.5)
    turn(b['Head'], (1, 0, 0), head)
    b['Spine'].scale = (1+.012*breath, 1+.008*breath, 1+.01*breath)
    b['Tail'].rotation_euler.z = math.radians(3*math.sin(math.tau*t/2.5))


# ---------------------------------------------------------------- shared checks

def meshes():
    return bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']


def fingerprint(rig, mesh):
    raw = {'vertices': [list(v.co) for v in mesh.data.vertices],
           'polygons': [list(p.vertices) for p in mesh.data.polygons],
           'uv': [[list(x.uv) for x in layer.data] for layer in mesh.data.uv_layers],
           'weights': [[(g.group, g.weight) for g in v.groups] for v in mesh.data.vertices],
           'groups': [g.name for g in mesh.vertex_groups],
           'bones': [(b.name, b.parent.name if b.parent else None,
                      [list(row) for row in b.matrix_local]) for b in rig.data.bones],
           'actions': {a.name: [(fc.data_path, fc.array_index,
                                 [(list(k.co), k.interpolation) for k in fc.keyframe_points])
                                for fc in base.action_curves(a)]
                       for a in bpy.data.actions if a.name != NAME}}
    return hashlib.sha256(json.dumps(raw, sort_keys=True).encode()).hexdigest()


class Shells:
    """Torso / arm shells and the forearm+hand vertices tested for penetration."""

    RAY = Vector((.11, .98, .17)).normalized()

    def __init__(self, mesh, rest):
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        bm.verts.ensure_lookup_table()
        unseen, shells = set(range(len(bm.verts))), []
        while unseen:
            seed = unseen.pop()
            group, stack = {seed}, [seed]
            while stack:
                for edge in bm.verts[stack.pop()].link_edges:
                    for v in edge.verts:
                        if v.index in unseen:
                            unseen.remove(v.index)
                            group.add(v.index)
                            stack.append(v.index)
            shells.append(group)
        bm.free()
        shells.sort(key=len, reverse=True)
        polys = [list(p.vertices) for p in mesh.data.polygons]
        self.torso = [p for p in polys if p[0] in shells[0]]
        arms = {('L' if rest[list(s), 0].mean() > 0 else 'R'): s for s in shells[1:3]}
        self.arm = {k: [p for p in polys if p[0] in s] for k, s in arms.items()}
        names = {g.index: g.name for g in mesh.vertex_groups}
        self.distal = {s: [v.index for v in mesh.data.vertices
                           if sum(g.weight for g in v.groups if names[g.group] in ('Forearm.'+s, 'Hand.'+s)) > .5]
                       for s in 'LR'}
        self.hand = {s: [v.index for v in mesh.data.vertices
                         if any(names[g.group] == 'Hand.'+s and g.weight > .5 for g in v.groups)] for s in 'LR'}

    def _inside(self, tree, p):
        origin, hits = Vector(p), 0
        for _ in range(40):
            hit = tree.ray_cast(origin, self.RAY)
            if hit[0] is None:
                break
            hits += 1
            origin = hit[0] + self.RAY*1e-6
        return hits % 2 == 1

    def depth(self, xyz, polys, idx):
        tree = BVHTree.FromPolygons([Vector(p) for p in xyz], polys, all_triangles=False)
        worst = 0.
        for i in idx:
            if self._inside(tree, xyz[i]):
                worst = max(worst, (Vector(xyz[i])-tree.find_nearest(Vector(xyz[i]))[0]).length)
        return worst

    def measure(self, xyz):
        out = {'distal_in_torso_m': max(self.depth(xyz, self.torso, self.distal[s]) for s in 'LR'),
               'hand_in_other_arm_m': max(self.depth(xyz, self.arm['R'], self.hand['L']),
                                          self.depth(xyz, self.arm['L'], self.hand['R']))}
        hl, hr = xyz[self.hand['L']], xyz[self.hand['R']]
        out['palm_gap_m'] = float(hl[:, 0].min()-hr[:, 0].max())
        return out


def sweep(rig, mesh, from_action):
    """Per-frame stretch/compress/penetration over the clip."""
    rig.animation_data.action = None
    base.reset_pose(rig)
    bpy.context.view_layer.update()
    rest = validation.positions(mesh)
    shells = Shells(mesh, rest)
    edges = np.array([e.vertices[:] for e in mesh.data.edges])
    lengths = np.linalg.norm(rest[edges[:, 0]]-rest[edges[:, 1]], axis=1)
    valid = lengths > .0001
    rest_depth = shells.measure(rest)['distal_in_torso_m']
    result = {'max_stretch': 1., 'stretch_frame': 1, 'min_compress': 1., 'compress_frame': 1,
              'rest_distal_in_torso_m': rest_depth, 'distal_in_torso_m': 0., 'torso_frame': 1,
              'hand_in_other_arm_m': 0., 'arm_frame': 1, 'min_palm_gap_m': 1., 'gap_frame': 1}
    for frame in range(1, LAST+1):
        if from_action:
            validation.set_action(rig, NAME, frame)
        else:
            pose(rig, (frame-1)/base.FPS)
            bpy.context.view_layer.update()
        xyz = validation.positions(mesh)
        r = np.linalg.norm(xyz[edges[:, 0]]-xyz[edges[:, 1]], axis=1)[valid]/lengths[valid]
        if r.max() > result['max_stretch']:
            result['max_stretch'], result['stretch_frame'] = float(r.max()), frame
        if r.min() < result['min_compress']:
            result['min_compress'], result['compress_frame'] = float(r.min()), frame
        m = shells.measure(xyz)
        excess = max(0., m['distal_in_torso_m']-rest_depth)
        if excess > result['distal_in_torso_m']:
            result['distal_in_torso_m'], result['torso_frame'] = excess, frame
        if m['hand_in_other_arm_m'] > result['hand_in_other_arm_m']:
            result['hand_in_other_arm_m'], result['arm_frame'] = m['hand_in_other_arm_m'], frame
        if m['palm_gap_m'] < result['min_palm_gap_m']:
            result['min_palm_gap_m'], result['gap_frame'] = m['palm_gap_m'], frame
    return result


def configure_validation():
    validation.CLIPS = CLIPS
    validation.BLEND = STAGED
    validation.MODEL_DIR = EXPORTS
    validation.REPORT = {'schema_version': 1, 'asset_id': 'Pet01', 'date': '2026-10-26',
                         'extra_actions': {}, 'checks': [], 'roundtrip': {}}


# ---------------------------------------------------------------- phases

def probe():
    """In-memory frame sweep on the canonical source; never saves."""
    bpy.ops.wm.open_mainfile(filepath=str(base.BLEND))
    rig, mesh = meshes()
    print('PROBE', json.dumps(sweep(rig, mesh, False)), flush=True)


def build():
    WORK.mkdir(exist_ok=True)
    EXPORTS.mkdir(exist_ok=True)
    if STAGED.exists():
        raise RuntimeError('Staged source exists; archive the evidence batch before rebuilding')
    if sha(base.BLEND) != SOURCE_SHA:
        raise RuntimeError('Canonical source hash changed; re-check before re-authoring')
    bpy.ops.wm.open_mainfile(filepath=str(base.BLEND))
    rig, mesh = meshes()
    before = fingerprint(rig, mesh)
    old = bpy.data.actions[NAME]
    bpy.data.actions.remove(old)
    rig.animation_data_create()
    rig.animation_data.action = None
    for frame in range(1, LAST+1):
        bpy.context.scene.frame_set(frame)
        pose(rig, (frame-1)/base.FPS)
        for bone in rig.pose.bones:
            for channel in ('location', 'rotation_euler', 'scale'):
                bone.keyframe_insert(data_path=channel, frame=frame, group=bone.name)
    action = rig.animation_data.action
    action.name = NAME
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, LAST
    for fc in base.action_curves(action):
        for key in fc.keyframe_points:
            key.interpolation = 'LINEAR'
    for label, frame in (('Start', 1), ('Complete', LAST)):
        action.pose_markers.new(label).frame = frame
    if before != fingerprint(rig, mesh):
        raise RuntimeError('Frozen data changed')
    (WORK/'source_provenance.json').write_text(json.dumps({
        'source_sha256': SOURCE_SHA, 'frozen_data_fingerprint': before,
        'source': str(base.BLEND.relative_to(base.ROOT)).replace('\\', '/'),
        'gassho_pose': GASSHO, 'nods_deg': NODS,
        'note': ('Only calm_zen re-authored (palms together + closed eyes + three decreasing nods). '
                 'Geometry, UV, weights, rest bones and the other 9 Actions unchanged.')},
        ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    rig.animation_data.action = bpy.data.actions['Idle']
    bpy.context.scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(STAGED))
    validation.export()
    print('BUILT', sha(STAGED), flush=True)


def validate():
    snapshots = validation.validate()
    rig, mesh = meshes()
    provenance = json.loads((WORK/'source_provenance.json').read_text(encoding='utf-8'))
    validation.check('frozen_data_unchanged', fingerprint(rig, mesh) == provenance['frozen_data_fingerprint'], {})
    validation.check('other_actions_present', all(n in bpy.data.actions for n in (
        'Idle', 'Eat', 'TouchReact', 'DozeLoop', 'WalkLoop', 'classic_proud', 'rage_roar',
        'divine_awaken', 'social_laugh')), {'actions': sorted(a.name for a in bpy.data.actions)})
    s = sweep(rig, mesh, True)
    validation.REPORT['extra_actions'][NAME].update(s)
    validation.check(NAME+'_bounded_edge_stretch', s['max_stretch'] < STRETCH_LIMIT,
                     {'max_ratio': s['max_stretch'], 'frame': s['stretch_frame'], 'limit': STRETCH_LIMIT})
    validation.check(NAME+'_bounded_edge_compress', s['min_compress'] > COMPRESS_LIMIT,
                     {'min_ratio': s['min_compress'], 'frame': s['compress_frame'], 'limit': COMPRESS_LIMIT})
    validation.check(NAME+'_hands_outside_torso', s['distal_in_torso_m'] < PENETRATION_DEPTH,
                     {'excess_depth_m': s['distal_in_torso_m'], 'rest_depth_m': s['rest_distal_in_torso_m'],
                      'frame': s['torso_frame'], 'limit_m': PENETRATION_DEPTH})
    validation.check(NAME+'_hands_not_interpenetrating', s['hand_in_other_arm_m'] < PENETRATION_DEPTH,
                     {'depth_m': s['hand_in_other_arm_m'], 'frame': s['arm_frame'], 'limit_m': PENETRATION_DEPTH})
    for frame in (10, 19, 30, 41, 47, 60):  # rise, joined, each nod, release
        validation.set_action(rig, NAME, frame)
        snapshots[NAME][frame] = validation.positions(mesh)
    validation.validate_roundtrip(snapshots)
    validation.REPORT['passed'] = all(c['passed'] for c in validation.REPORT['checks'])
    validation.REPORT['staged_sha256'] = sha(STAGED)
    validation.REPORT['exports_sha256'] = {f'Pet01_{NAME}.fbx': sha(EXPORTS/f'Pet01_{NAME}.fbx')}
    REPORT.write_text(json.dumps(validation.REPORT, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print('VALIDATION', 'PASSED' if validation.REPORT['passed'] else 'FAILED', flush=True)
    if not validation.REPORT['passed']:
        raise RuntimeError('Validation failed; inspect '+str(REPORT))


FRAMES = {'rise': 10, 'joined': 22, 'nod1': 30, 'nod2': 40, 'nod3': 49, 'release': 64}


def preview():
    bpy.ops.wm.open_mainfile(filepath=str(STAGED))
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.render.resolution_x, scene.render.resolution_y = 480, 528
    scene.render.resolution_percentage = 100
    rig, _ = meshes()
    out = WORK/'Previews'
    out.mkdir(exist_ok=True)
    for view in ('hero', 'side', 'front', 'face'):
        base.set_camera(view)
        validation.CLIPS = {'Idle': (91, True), NAME: (LAST, False)}
        validation.set_action(rig, 'Idle', 1)
        scene.render.filepath = str(out/f'Idle_{view}_01_rest.png')
        bpy.ops.render.render(write_still=True)
        for label, frame in FRAMES.items():
            validation.set_action(rig, NAME, frame)
            scene.render.filepath = str(out/f'{NAME}_{view}_{frame:02d}_{label}.png')
            bpy.ops.render.render(write_still=True)


def promote():
    numeric = json.loads(REPORT.read_text(encoding='utf-8'))
    if not numeric['passed'] or numeric.get('staged_sha256') != sha(STAGED):
        raise RuntimeError('Numeric validation has not passed for the staged file')
    approval = WORK/'visual_acceptance.json'
    accepted = json.loads(approval.read_text(encoding='utf-8')) if approval.exists() else {}
    if accepted.get('staged_sha256') != sha(STAGED) or accepted.get('deformation_passed') is not True:
        raise RuntimeError('Visual acceptance must pass for the staged source hash')
    for name, value in numeric['exports_sha256'].items():
        if sha(EXPORTS/name) != value:
            raise RuntimeError('Export hash mismatch: '+name)
    if sha(base.BLEND) != SOURCE_SHA:
        raise RuntimeError('Source changed since staging; refusing overwrite')
    if ARCHIVE.exists():
        raise RuntimeError('Archive already exists; leave it unchanged')
    ARCHIVE.mkdir(parents=True)
    shutil.copy2(base.BLEND, ARCHIVE/'pet01.blend')
    shutil.copy2(base.SOURCE/'asset_manifest.json', ARCHIVE/'asset_manifest.json')
    runtime = base.MODEL_DIR/f'Pet01_{NAME}.fbx'
    shutil.copy2(runtime, ARCHIVE/runtime.name)
    records = [{'file': p.name, 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(ARCHIVE.iterdir())]
    (ARCHIVE/'backup_manifest.json').write_text(json.dumps({
        'reason': 'Before calm_zen gassho re-author (Action only).',
        'previous_source_sha256': SOURCE_SHA, 'files': records}, ensure_ascii=False, indent=2)+'\n',
        encoding='utf-8')
    shutil.copy2(STAGED, base.BLEND)
    shutil.copy2(EXPORTS/runtime.name, runtime)
    print('PROMOTED', sha(base.BLEND), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--phase', choices=['probe', 'build', 'validate', 'preview', 'promote'], required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    configure_validation()
    globals()[args.phase]()
