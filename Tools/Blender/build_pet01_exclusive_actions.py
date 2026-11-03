"""Author approved candidate-A actions on a staged copy of the frozen Pet01.

Run D:/blender.exe -b --python-exit-code 1 --python this_file -- --phase build
Then apply the authorized weight revision, validate, preview and promote.
Promotion is for the labelled development preview, not full performance acceptance.
Action authoring preserves mesh, weights, rest bones and existing Actions.
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

WORK = base.SOURCE / 'ExclusiveActions'
STAGED = WORK / 'pet01_exclusive.blend'
EXPORTS = WORK / 'Exports'
REPORT = WORK / 'validation_report.json'
CLIPS = {'classic_proud': (61, False), 'calm_zen': (76, False),
         'rage_roar': (61, False), 'divine_awaken': (76, False)}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def curve(t, keys):
    """Smoothstep each beat; deterministic per-frame baking at 30fps."""
    for (a, x), (b, y) in zip(keys, keys[1:]):
        if t <= b:
            u = max(0., min(1., (t-a)/(b-a)))
            return x + (y-x)*u*u*(3-2*u)
    return keys[-1][1]


def rotate(bones, name, axis, degrees):
    # Express pitch/roll in armature space, then convert into bone rest axes.
    # +Y is forward, +Z up; +X pitch leans backward, +Y roll leans sideways.
    b = bones[name]
    local_axis = b.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
    b.rotation_euler = Quaternion(local_axis, math.radians(degrees)).to_euler('XYZ')


def pose(rig, name, t):
    base.reset_pose(rig)
    b = rig.pose.bones
    pitch, head, roll, arms = 0., 0., 0., 0.
    if name == 'classic_proud':
        pitch = curve(t, [(0,0),(.28,7),(1.5,7),(2,0)])
        head = curve(t, [(0,0),(.28,11),(1.45,9),(2,0)])
        roll = curve(t, [(0,0),(.3,0),(.65,9),(1,-9),(1.35,0),(2,0)])
        arms = curve(t, [(0,0),(1.25,0),(1.55,30),(1.75,30),(2,0)])
        rotate(b, 'UpperArm.L', (0,1,0), -arms)
    elif name == 'calm_zen':
        # Arms stay at authored crossed-belly rest pose for the entire clip.
        head = curve(t, [(0,0),(.25,3),(.55,-8),(.82,4),(1.1,-6),
                         (1.4,3),(1.7,-4),(2.15,0),(2.5,0)])
        pitch = head * .25
    elif name == 'rage_roar':
        pitch = curve(t, [(0,0),(.3,-7),(.55,6),(1.35,6),(1.62,-4),(2,0)])
        head = curve(t, [(0,0),(.3,-5),(.55,13),(1.35,13),(1.65,-3),(2,0)])
        pulse = curve(t, [(0,0),(.45,0),(.6,1),(1.25,1),(1.45,0),(2,0)])
        roll = 2 * math.sin(math.tau * 8 * t) * pulse
        arms = curve(t, [(0,0),(.3,0),(.6,32),(1.35,32),(2,0)])
    else:
        pitch = curve(t, [(0,0),(.35,-2),(1.35,4),(2,4),(2.5,0)])
        head = curve(t, [(0,0),(.35,-7),(1.35,10),(2,10),(2.5,0)])
        arms = curve(t, [(0,0),(.4,0),(1.4,32),(2,32),(2.5,0)])
    rotate(b, 'Spine', (1,0,0), pitch * .55)
    rotate(b, 'Chest', (1,0,0), pitch * .45)
    # Compose roll with the pitch rotation (never translate non-root bones).
    spine = b['Spine']
    axis = spine.bone.matrix_local.to_3x3().inverted() @ Vector((0,1,0))
    spine.rotation_euler = (spine.rotation_euler.to_quaternion() @
                           Quaternion(axis, math.radians(roll))).to_euler('XYZ')
    rotate(b, 'Head', (1,0,0), head)
    if name in ('rage_roar', 'divine_awaken'):
        for sign, side in ((-1,'L'), (1,'R')):
            rotate(b, 'UpperArm.'+side, (0,1,0), sign*arms)
            # Forearm/wrist additions failed the deformation bound. Keep these
            # neutral; the open-palm silhouette is still an acceptance gap.


def fingerprint(rig, mesh):
    """Byte-stable geometry/UV/weights/rest-skeleton and original action data."""
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
                       for a in bpy.data.actions if a.name not in CLIPS}}
    return hashlib.sha256(json.dumps(raw, sort_keys=True).encode()).hexdigest()


def configure_validation():
    validation.CLIPS = CLIPS
    validation.BLEND = STAGED
    validation.MODEL_DIR = EXPORTS
    validation.REPORT = {'schema_version': 1, 'asset_id': 'Pet01', 'date': '2026-09-22',
                         'extra_actions': {}, 'checks': [], 'roundtrip': {}}


def build():
    WORK.mkdir(exist_ok=True)
    EXPORTS.mkdir(exist_ok=True)
    if STAGED.exists():
        raise RuntimeError('Staged source exists; archive the entire evidence batch before rebuilding')
    bpy.ops.wm.open_mainfile(filepath=str(base.BLEND))
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    before = fingerprint(rig, mesh)
    source_hash = sha(base.BLEND)
    if any(n in bpy.data.actions for n in CLIPS):
        raise RuntimeError('Source already contains exclusive actions; do not overwrite a released source.')
    for name, (last, _) in CLIPS.items():
        rig.animation_data.action = None
        for frame in range(1, last+1):
            bpy.context.scene.frame_set(frame)
            pose(rig, name, (frame-1)/base.FPS)
            for bone in rig.pose.bones:
                for channel in ('location', 'rotation_euler', 'scale'):
                    bone.keyframe_insert(data_path=channel, frame=frame, group=bone.name)
        action = rig.animation_data.action
        action.name = name
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, last
        for fc in base.action_curves(action):
            for key in fc.keyframe_points:
                key.interpolation = 'LINEAR'
        for label, frame in [('Start',1), ('Complete',last)]:
            action.pose_markers.new(label).frame = frame
    assert before == fingerprint(rig, mesh), 'Frozen data changed'
    (WORK / 'source_provenance.json').write_text(json.dumps({
        'source_sha256': source_hash, 'frozen_data_fingerprint': before,
        'source': str(base.BLEND.relative_to(base.ROOT)),
        'note': 'Geometry, UV, weights, rest bones and original 5 actions unchanged.'}, indent=2)+'\n')
    validation.set_action(rig, 'calm_zen', 1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(STAGED))
    validation.export()


def validate():
    snapshots = validation.validate()
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    provenance = json.loads((WORK/'source_provenance.json').read_text())
    validation.check('frozen_data_unchanged', fingerprint(rig,mesh) == provenance['frozen_data_fingerprint'], {})
    # Sample all source frames for edge stretch: numeric diagnostic, not a substitute for visual review.
    edges = np.array([e.vertices[:] for e in mesh.data.edges])
    rig.animation_data.action = None
    base.reset_pose(rig)
    bpy.context.view_layer.update()
    rest = validation.positions(mesh)
    lengths = np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]], axis=1)
    valid = lengths > .0001
    for name,(last,_) in CLIPS.items():
        worst = 1.
        for frame in range(1,last+1):
            validation.set_action(rig,name,frame)
            xyz = validation.positions(mesh)
            ratio = np.linalg.norm(xyz[edges[:,0]]-xyz[edges[:,1]],axis=1)[valid]/lengths[valid]
            worst = max(worst, float(ratio.max()))
        validation.REPORT['extra_actions'][name]['max_edge_stretch_ratio'] = worst
        validation.check(name+'_bounded_edge_stretch', worst < 2., {'max_ratio':worst, 'limit':2.})
    validation.validate_roundtrip(snapshots)
    validation.REPORT['passed'] = all(c['passed'] for c in validation.REPORT['checks'])
    validation.REPORT['staged_sha256'] = sha(STAGED)
    validation.REPORT['exports_sha256'] = {f'Pet01_{name}.fbx': sha(EXPORTS/f'Pet01_{name}.fbx') for name in CLIPS}
    REPORT.write_text(json.dumps(validation.REPORT,ensure_ascii=False,indent=2)+'\n', encoding='utf-8')
    if not validation.REPORT['passed']:
        raise RuntimeError('Validation failed; inspect '+str(REPORT))


def preview():
    bpy.ops.wm.open_mainfile(filepath=str(STAGED))
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 12
    scene.render.resolution_x, scene.render.resolution_y = 480, 528
    scene.render.resolution_percentage = 100
    rig = bpy.data.objects['Pet01Rig']
    for name,(last,_) in CLIPS.items():
        for view in ('hero','side'):
            base.set_camera(view)
            for i,frame in enumerate([1,round(last*.22),round(last*.48),round(last*.72),last]):
                validation.set_action(rig,name,frame)
                scene.render.filepath = str(WORK/'Previews'/f'{name}_{view}_{i}.png')
                bpy.ops.render.render(write_still=True)


def promote():
    numeric = json.loads(REPORT.read_text())
    if not numeric['passed'] or numeric.get('staged_sha256') != sha(STAGED):
        raise RuntimeError('Numeric validation has not passed')
    if not (WORK/'visual_review_final.md').exists():
        raise RuntimeError('Visual review must be written before promotion')
    approval = WORK/'visual_acceptance.json'
    if not approval.exists() or json.loads(approval.read_text()).get('staged_sha256') != sha(STAGED):
        raise RuntimeError('Visual acceptance must identify the reviewed staged source hash')
    if json.loads(approval.read_text()).get('deformation_passed') is not True:
        raise RuntimeError('Sampled deformation must pass before runtime preview import')
    regression = json.loads((WORK/'old_actions_regression.json').read_text())
    if regression.get('staged_sha256') != sha(STAGED) or regression.get('no_new_failures') is not True:
        raise RuntimeError('Old action regression must match the staged source')
    for report in (numeric, regression):
        exports = report.get('exports_sha256', {})
        if not exports or any(not (EXPORTS/name).exists() or sha(EXPORTS/name) != value for name,value in exports.items()):
            raise RuntimeError('Export hashes do not match the validation evidence')
    expected = json.loads((WORK/'source_provenance.json').read_text())['source_sha256']
    if sha(base.BLEND) != expected:
        raise RuntimeError('Source changed since staging; refusing overwrite')
    archive = base.SOURCE/'Archive'/'20260922_before_final_weight_promotion'
    archive.mkdir(parents=True,exist_ok=True)
    backup = archive/'pet01.blend'
    if backup.exists() and sha(backup) != expected:
        raise RuntimeError('Archive collision')
    shutil.copy2(base.BLEND,backup)
    shutil.copy2(STAGED,base.BLEND)
    runtime_archive = archive/'Models'
    runtime_archive.mkdir(exist_ok=True)
    for file in base.MODEL_DIR.glob('*.fbx*'):
        if not (runtime_archive/file.name).exists():
            shutil.copy2(file,runtime_archive/file.name)
    for file in EXPORTS.glob('*.fbx'):
        shutil.copy2(file,base.MODEL_DIR/file.name)
    print('PROMOTED',sha(base.BLEND),flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--phase',choices=['build','validate','preview','promote'],required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    configure_validation()
    globals()[args.phase]()
