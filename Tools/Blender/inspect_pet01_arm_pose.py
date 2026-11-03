"""Read-only candidate search on the staged rig; writes numeric diagnostics only."""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01_exclusive_actions as ex

bpy.ops.wm.open_mainfile(filepath=str(ex.STAGED))
rig, mesh = bpy.data.objects['Pet01Rig'], bpy.data.objects['Pet01_Mesh']
rig.animation_data.action = None
ex.base.reset_pose(rig)
bpy.context.view_layer.update()
edges = np.array([e.vertices[:] for e in mesh.data.edges])
rest = ex.validation.positions(mesh)
lengths = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
valid = lengths > .0001
results = []
for shoulder in (-30, 0, 20, 40, 60):
    for twist in (-60, -30, 0, 30, 60):
        for elbow in (-60, -30, 0, 30, 60):
            ex.pose(rig, 'divine_awaken', 1.5)
            for sign, side in ((-1, 'L'), (1, 'R')):
                ex.rotate(rig.pose.bones, 'UpperArm.' + side, (0, 1, 0), sign * shoulder)
                b = rig.pose.bones['UpperArm.' + side]
                axis = b.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, 1))
                b.rotation_euler = (Quaternion(axis, math.radians(sign * twist)) @ b.rotation_euler.to_quaternion()).to_euler('XYZ')
                ex.rotate(rig.pose.bones, 'Forearm.' + side, (0, 0, 1), sign * elbow)
            bpy.context.view_layer.update()
            xyz = ex.validation.positions(mesh)
            ratio = np.linalg.norm(xyz[edges[:, 0]] - xyz[edges[:, 1]], axis=1)[valid] / lengths[valid]
            hand = rig.pose.bones['Hand.L'].tail
            results.append(dict(shoulder=shoulder, twist=twist, elbow=elbow,
                                max_edge_ratio=float(ratio.max()), hand=list(hand)))
results.sort(key=lambda r: abs(r['hand'][0]), reverse=True)
out = ex.WORK / 'arm_pose_diagnostics.json'
out.write_text(json.dumps({'staged_sha256': ex.sha(ex.STAGED), 'candidates': results}, indent=2) + '\n')
print(json.dumps([r for r in results if r['max_edge_ratio'] < 2][:12], indent=2), flush=True)
