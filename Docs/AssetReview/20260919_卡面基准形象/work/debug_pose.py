# 调试：应用 calm 姿态前后，手臂骨骼世界 tail 位置 + 对应网格顶点是否跟随
import bpy, math
from mathutils import Matrix, Vector

rig = bpy.data.objects["Pet01Rig"]
mesh = bpy.data.objects["Pet01_Mesh"]
if rig.animation_data:
    rig.animation_data_clear()

def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def tail_world(bname):
    pb = rig.pose.bones[bname]
    return tuple(round(c, 4) for c in (rig.matrix_world @ pb.matrix).translation)

for b in ("UpperArm.L", "Forearm.L", "Hand.L"):
    print("REST", b, "tail_world=", tail_world(b))

spec = [
    ("Root", 'Z', 35), ("Pelvis", 'X', 6),
    ("UpperArm.L", 'X', -22), ("Forearm.L", 'Y', -42), ("Hand.L", 'X', -8),
]
for bn, ax, dg in spec:
    rot(rig.pose.bones[bn], ax, dg)
bpy.context.view_layer.update()
for b in ("UpperArm.L", "Forearm.L", "Hand.L"):
    print("POSE", b, "tail_world=", tail_world(b))

# 手部附近顶点（rest 时手的位置附近）在姿态后的位移
dg = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(dg)
import numpy as np
vs = np.empty(len(ev.data.vertices)*3, dtype=np.float64)
ev.data.vertices.foreach_get("co", vs)
vs = vs.reshape(-1, 3)
m44 = np.array(mesh.matrix_world)
vs = (m44 @ np.hstack([vs, np.ones((len(vs),1))]).T).T[:, :3]
# rest 手掌中心约 (0.014, 0.048, 0.101)（Hand.L tail 附近），找最近顶点
center = np.array([0.014, 0.048, 0.101])
d = np.linalg.norm(vs - center, axis=1)
i = int(d.argmin())
print("nearest vert to rest-hand-center now at", vs[i], "dist", d[i])
# 该顶点的权重
v = mesh.data.vertices[i]
name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
print("weights:", {name_by_idx[ge.group]: round(ge.weight,3) for ge in v.groups if ge.weight > 0.01})
# 有多少顶点权重主要在 Hand/Forearm.L 上
import collections
c = collections.Counter()
for vv in mesh.data.vertices:
    w = {name_by_idx[ge.group]: ge.weight for ge in vv.groups}
    hw = sum(x for n, x in w.items() if n in ("Hand.L","Forearm.L"))
    if hw > 0.5: c["hand_forearm_dominant"] += 1
print("verts with >0.5 Hand/Forearm.L weight:", c["hand_forearm_dominant"])
