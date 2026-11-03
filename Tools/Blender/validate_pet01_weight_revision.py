"""Compare authorized weight revision to archived frozen geometry and old Actions."""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01_exclusive_actions as ex

OLD = {'Idle': (91, True), 'Eat': (97, False), 'TouchReact': (22, False),
       'DozeLoop': (121, True), 'WalkLoop': (31, True)}


def immutable_data():
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    return {
        'vertices': [v.co[:] for v in mesh.data.vertices],
        'polygons': [p.vertices[:] for p in mesh.data.polygons],
        'uv': [[x.uv[:] for x in layer.data] for layer in mesh.data.uv_layers],
        'bones': [(b.name, b.parent.name if b.parent else None, [row[:] for row in b.matrix_local]) for b in rig.data.bones],
        'actions': {name: [(fc.data_path, fc.array_index,
                           [(k.co[:], k.handle_left[:], k.handle_right[:], k.interpolation) for k in fc.keyframe_points])
                          for fc in ex.base.action_curves(bpy.data.actions[name])] for name in OLD},
    }


def main():
    baseline = ex.base.SOURCE / 'Archive/20260922_before_exclusive_actions/pet01.blend'
    bpy.ops.wm.open_mainfile(filepath=str(baseline))
    before = immutable_data()
    bpy.ops.wm.open_mainfile(filepath=str(ex.STAGED))
    unchanged = before == immutable_data()
    ex.configure_validation()
    ex.validation.CLIPS = OLD
    ex.validation.export()
    snapshots = ex.validation.validate()
    ex.validation.check('geometry_uv_rest_bones_old_curves_unchanged', unchanged, {'baseline_sha256': ex.sha(baseline)})
    ex.validation.validate_roundtrip(snapshots)
    report = ex.validation.REPORT
    failures = [c['name'] for c in report['checks'] if not c['passed']]
    report['known_baseline_failures'] = ['WalkLoop_above_ground']
    report['no_new_failures'] = failures == report['known_baseline_failures']
    report['passed'] = not failures
    report['staged_sha256'] = ex.sha(ex.STAGED)
    report_path = ex.WORK / 'old_actions_regression.json'
    report_path.write_text(json.dumps(report, indent=2)+'\n')
    if not unchanged or not report['no_new_failures']:
        raise RuntimeError('Old animation regression introduced new failures')
    # Export the actual revised rest mesh, not just animation-only resources.
    bpy.ops.wm.open_mainfile(filepath=str(ex.STAGED))
    rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
    rig.animation_data.action = None
    ex.base.reset_pose(rig)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True); mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(ex.EXPORTS/'Pet01.fbx'), use_selection=True,
        object_types={'ARMATURE','MESH'}, add_leaf_bones=False, use_armature_deform_only=True,
        axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        use_mesh_modifiers=True, mesh_smooth_type='FACE', path_mode='RELATIVE',
        embed_textures=False, bake_anim=False, armature_nodetype='NULL')
    report['exports_sha256'] = {name: ex.sha(ex.EXPORTS/name) for name in
                               ['Pet01.fbx'] + [f'Pet01_{clip}.fbx' for clip in OLD]}
    report_path.write_text(json.dumps(report, indent=2)+'\n')


if __name__ == '__main__':
    main()
