"""Author the shared exaggerated social_laugh Action on a staged copy of Pet01.

Only adds one Action: mesh, UV, weights, rest skeleton and the nine existing
Actions stay byte-identical (checked by fingerprint). Phases:
  D:/blender.exe -b --python-exit-code 1 --python this_file -- --phase probe
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

import bpy
import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as base
import build_pet01_extra_actions as validation

NAME = 'social_laugh'
LAST = 73  # (73-1)/30 = 2.4s, matches design_v1 shared-laugh duration
CLIPS = {NAME: (LAST, False)}
WORK = base.SOURCE / 'SocialLaugh'
STAGED = WORK / 'pet01_social_laugh.blend'
EXPORTS = WORK / 'Exports'
REPORT = WORK / 'validation_report.json'
# Distribution of the torso bend. Pelvis stays 0 so the legs (Pelvis children)
# never move and the feet remain exactly planted.
BEND = {'Spine': .20, 'Chest': .25, 'Head': .55}
# Exaggerated laugh is deliberately fast; the 10mm/frame heuristic was set for
# gentle clips. The FBX roundtrip check still samples the fastest frames.
STEP_LIMIT = .035
# Arm fling peaks (wave1, wave2) in degrees and spine stretch at the wave peaks.
# Tuned by frame sweep so every frame stays under STRETCH_LIMIT (2026-09-28):
# wave 1 is the flailing/stretchy one, wave 2 is the deepest backbend.
FLING = (22, 10)
STRETCH = .10
PEAK = 105  # second-wave backbend, degrees
HICCUP = 3  # laughing-fit pitch pulse, degrees
TWIST2 = 10  # second-wave chest twist, degrees
STRETCH_WAVE2 = 0.  # share of the spine stretch kept at the deeper second wave
SQUASH = .15  # spine squash on the crunch
PAT = 14  # belly-pat upper-arm swing, degrees
CRUNCH = -35  # belly-clutch forward bend, degrees (probe: 40 safe, 70 folds)
STRETCH_LIMIT = 2.
# 2026-09-28 probe: a 70deg forward fold collapsed the belly at 0.18.
COMPRESS_LIMIT = .25


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


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


def pose(rig, t):
    base.reset_pose(rig)
    b = rig.pose.bones
    # Two waves: burst back 65deg, rebound into a belly crunch, bigger 105deg wave.
    back = curve(t, [(0, 0), (.30, -8), (.62, 65), (.74, 60), (1.05, 22), (1.22, CRUNCH),
                     (1.48, CRUNCH*.8), (1.82, PEAK), (2.00, PEAK-5), (2.40, 0)])
    # Laughing fits: whole-envelope pulses, zero at both ends.
    fit = curve(t, [(0, 0), (.35, 0), (.55, 1), (1.95, 1), (2.30, 0), (2.40, 0)])
    hiccup = HICCUP * math.sin(math.tau * 5.5 * t) * fit
    shake = 6 * math.sin(math.tau * 8 * t) * fit
    twist = curve(t, [(0, 0), (.55, 0), (.70, 12), (1.05, -8), (1.50, 0), (1.85, TWIST2),
                      (2.05, -TWIST2*.6), (2.40, 0)])
    for name, share in BEND.items():
        turn(b[name], (1, 0, 0), back*share + hiccup*(share if name != 'Head' else .6))
    turn(b['Chest'], (0, 1, 0), shake)
    turn(b['Chest'], (0, 0, 1), twist)
    turn(b['Head'], (0, 1, 0), -shake*.8)
    # Squash on the crunch, stretch at the wave peaks.
    stretch = curve(t, [(0, 0), (.30, -.4), (.62, .8), (1.05, .2), (1.30, -1), (1.48, -.8),
                        (1.82, STRETCH_WAVE2), (2.00, STRETCH_WAVE2), (2.40, 0)])
    s = STRETCH*stretch if stretch > 0 else SQUASH*stretch
    b['Spine'].scale = (1-s*.6, 1+s, 1-s*.6)
    # Arms fling out on the waves (kept under the 40deg arm/belly limit) and
    # pat the belly during the crunch.
    fling = curve(t, [(0, 0), (.40, 0), (.62, FLING[0]), (.95, FLING[0]*.7), (1.15, 4), (1.50, 4),
                      (1.82, FLING[1]), (2.05, FLING[1]*.9), (2.40, 0)])
    pat = curve(t, [(0, 0), (1.05, 0), (1.18, 1), (1.52, 1), (1.65, 0), (2.4, 0)])
    for sign, side in ((-1, 'L'), (1, 'R')):
        arm = b['UpperArm.'+side]
        turn(arm, (0, 1, 0), sign*fling)
        turn(arm, (1, 0, 0), PAT*math.sin(math.tau*6*t + (0 if side == 'L' else math.pi))*pat)
    # Happy squint and wide open mouth (existing Eat mouth range, measured safe).
    # Stay squinting through the settle; reopen only in the final beat.
    squint = curve(t, [(0, 0), (.30, .55), (.55, .8), (2.22, .8), (2.40, 0)])
    for side in 'LR':
        b['LidUpper.'+side].rotation_euler.y = -math.radians(78)*squint
        b['LidLower.'+side].rotation_euler.y = math.radians(78)*squint
    mouth = curve(t, [(0, 0), (.30, .2), (.58, 1), (1.05, .75), (1.30, .9), (1.80, 1),
                      (2.10, .9), (2.40, 0)])
    mouth *= 1 - .15*abs(math.sin(math.tau*5.5*t))*fit
    b['MouthLower'].location.y = -.0063*mouth
    b['MouthUpper'].location.y = .0016*mouth
    b['Tail'].rotation_euler.z = math.radians(18*math.sin(math.tau*4*t)*fit)


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


def configure_validation():
    validation.CLIPS = CLIPS
    validation.BLEND = STAGED
    validation.MODEL_DIR = EXPORTS
    validation.REPORT = {'schema_version': 1, 'asset_id': 'Pet01', 'date': '2026-09-28',
                         'extra_actions': {}, 'checks': [], 'roundtrip': {}}


def edge_setup(mesh):
    edges = np.array([e.vertices[:] for e in mesh.data.edges])
    rest = validation.positions(mesh)
    lengths = np.linalg.norm(rest[edges[:, 0]]-rest[edges[:, 1]], axis=1)
    return edges, lengths, lengths > .0001, rest


def probe():
    """In-memory frame sweep; never saves."""
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    rig.animation_data.action = None
    base.reset_pose(rig); bpy.context.view_layer.update()
    edges, lengths, valid, rest = edge_setup(mesh)
    planted = np.where(rest[:, 2] < 1e-5)[0]
    prev, worst, step, feet, low, at, squeeze = rest, 1., 0., 0., 0., {}, 1.
    for frame in range(1, LAST+1):
        pose(rig, (frame-1)/base.FPS); bpy.context.view_layer.update()
        xyz = validation.positions(mesh)
        r = np.linalg.norm(xyz[edges[:, 0]]-xyz[edges[:, 1]], axis=1)[valid]/lengths[valid]
        if r.max() > worst:
            worst, at['stretch'] = float(r.max()), frame
        if r.min() < squeeze:
            squeeze, at['compress'] = float(r.min()), frame
        d = float(np.linalg.norm(xyz-prev, axis=1).max())
        if d > step:
            step, at['step'] = d, frame
        feet = max(feet, float(np.linalg.norm(xyz[planted]-rest[planted], axis=1).max()))
        low = min(low, float(xyz[:, 2].min()))
        prev = xyz
    print('PROBE', json.dumps({'max_stretch': worst, 'min_compress': squeeze, 'max_step_m': step, 'foot_drift_m': feet,
                               'min_z_m': low, 'at_frame': at,
                               'endpoint_m': float(np.linalg.norm(prev-rest, axis=1).max())}), flush=True)


def build():
    WORK.mkdir(exist_ok=True)
    EXPORTS.mkdir(exist_ok=True)
    if STAGED.exists():
        raise RuntimeError('Staged source exists; archive the evidence batch before rebuilding')
    bpy.ops.wm.open_mainfile(filepath=str(base.BLEND))
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    before = fingerprint(rig, mesh)
    source_hash = sha(base.BLEND)
    if NAME in bpy.data.actions:
        raise RuntimeError('Source already contains social_laugh; do not overwrite a released source.')
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
    for label, frame in [('Start', 1), ('Complete', LAST)]:
        action.pose_markers.new(label).frame = frame
    assert before == fingerprint(rig, mesh), 'Frozen data changed'
    (WORK / 'source_provenance.json').write_text(json.dumps({
        'source_sha256': source_hash, 'frozen_data_fingerprint': before,
        'source': str(base.BLEND.relative_to(base.ROOT)).replace('\\', '/'),
        'note': 'Only social_laugh added. Geometry, UV, weights, rest bones and 9 existing actions unchanged.'},
        indent=2)+'\n')
    # Keep the source's default display action on Idle, as before promotion.
    rig.animation_data.action = bpy.data.actions['Idle']
    bpy.context.scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(STAGED))
    validation.export()


def validate():
    snapshots = validation.validate()
    # The shared smooth-motion check uses 10mm; replace it with the documented laugh limit.
    for c in validation.REPORT['checks']:
        if c['name'] == NAME+'_smooth_motion':
            c['evidence']['limit_m'] = STEP_LIMIT
            c['evidence']['note'] = 'exaggerated laugh; generic 10mm heuristic replaced, roundtrip still sampled'
            c['passed'] = c['evidence']['max_step_m'] < STEP_LIMIT
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    provenance = json.loads((WORK/'source_provenance.json').read_text())
    validation.check('frozen_data_unchanged', fingerprint(rig, mesh) == provenance['frozen_data_fingerprint'], {})
    validation.check('existing_actions_present', all(n in bpy.data.actions for n in (
        'Idle', 'Eat', 'TouchReact', 'DozeLoop', 'WalkLoop',
        'classic_proud', 'calm_zen', 'rage_roar', 'divine_awaken')), {'actions': sorted(a.name for a in bpy.data.actions)})
    rig.animation_data.action = None
    base.reset_pose(rig); bpy.context.view_layer.update()
    edges, lengths, valid, _ = edge_setup(mesh)
    worst, worst_frame, squeeze, squeeze_frame = 1., 1, 1., 1
    for frame in range(1, LAST+1):
        validation.set_action(rig, NAME, frame)
        xyz = validation.positions(mesh)
        ratio = np.linalg.norm(xyz[edges[:, 0]]-xyz[edges[:, 1]], axis=1)[valid]/lengths[valid]
        if ratio.max() > worst:
            worst, worst_frame = float(ratio.max()), frame
        if ratio.min() < squeeze:
            squeeze, squeeze_frame = float(ratio.min()), frame
    validation.REPORT['extra_actions'][NAME]['max_edge_stretch_ratio'] = worst
    validation.REPORT['extra_actions'][NAME]['min_edge_compress_ratio'] = squeeze
    validation.check(NAME+'_bounded_edge_stretch', worst < STRETCH_LIMIT,
                     {'max_ratio': worst, 'frame': worst_frame, 'limit': STRETCH_LIMIT})
    validation.check(NAME+'_bounded_edge_compress', squeeze > COMPRESS_LIMIT,
                     {'min_ratio': squeeze, 'frame': squeeze_frame, 'limit': COMPRESS_LIMIT})
    # Roundtrip at the densest frames too: both wave peaks and the crunch.
    for frame in (19, 36, 56, 61):
        validation.set_action(rig, NAME, frame)
        snapshots[NAME][frame] = validation.positions(mesh)
    validation.validate_roundtrip(snapshots)
    validation.REPORT['passed'] = all(c['passed'] for c in validation.REPORT['checks'])
    validation.REPORT['staged_sha256'] = sha(STAGED)
    validation.REPORT['exports_sha256'] = {f'Pet01_{NAME}.fbx': sha(EXPORTS/f'Pet01_{NAME}.fbx')}
    REPORT.write_text(json.dumps(validation.REPORT, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    if not validation.REPORT['passed']:
        raise RuntimeError('Validation failed; inspect '+str(REPORT))


def preview():
    bpy.ops.wm.open_mainfile(filepath=str(STAGED))
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.render.resolution_x, scene.render.resolution_y = 480, 528
    scene.render.resolution_percentage = 100
    rig = bpy.data.objects['Pet01Rig']
    (WORK/'Previews').mkdir(exist_ok=True)
    frames = {'start': 1, 'wave1': 19, 'rebound': 32, 'crunch': 40, 'wave2': 56, 'hold': 61, 'settle': 67}
    for view in ('hero', 'side', 'front', 'face'):
        base.set_camera(view)
        for label, frame in frames.items():
            validation.set_action(rig, NAME, frame)
            scene.render.filepath = str(WORK/'Previews'/f'{NAME}_{view}_{frame:02d}_{label}.png')
            bpy.ops.render.render(write_still=True)


def promote():
    numeric = json.loads(REPORT.read_text(encoding='utf-8'))
    if not numeric['passed'] or numeric.get('staged_sha256') != sha(STAGED):
        raise RuntimeError('Numeric validation has not passed for the staged file')
    approval = WORK/'visual_acceptance.json'
    if not approval.exists():
        raise RuntimeError('Visual acceptance missing')
    accepted = json.loads(approval.read_text(encoding='utf-8'))
    if accepted.get('staged_sha256') != sha(STAGED) or accepted.get('deformation_passed') is not True:
        raise RuntimeError('Visual acceptance must pass for the staged source hash')
    for name, value in numeric['exports_sha256'].items():
        if sha(EXPORTS/name) != value:
            raise RuntimeError('Export hash mismatch: '+name)
    expected = json.loads((WORK/'source_provenance.json').read_text())['source_sha256']
    if sha(base.BLEND) != expected:
        raise RuntimeError('Source changed since staging; refusing overwrite')
    archive = base.SOURCE/'Archive'/'20260928_before_social_laugh'
    archive.mkdir(parents=True, exist_ok=True)
    backup = archive/'pet01.blend'
    if backup.exists() and sha(backup) != expected:
        raise RuntimeError('Archive collision')
    shutil.copy2(base.BLEND, backup)
    shutil.copy2(STAGED, base.BLEND)
    for file in EXPORTS.glob('*.fbx'):
        shutil.copy2(file, base.MODEL_DIR/file.name)
    print('PROMOTED', sha(base.BLEND), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--phase', choices=['probe', 'build', 'validate', 'preview', 'promote'], required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    configure_validation()
    globals()[args.phase]()
