# 调试 v2：按顶点索引追踪
import bpy, math
from mathutils import Matrix, Vector
import numpy as np

rig = bpy.data.objects["Pet01Rig"]
mesh = bpy.data.objects["Pet01_Mesh"]
if rig.animation_data:
    rig.animation_data_clear()
print("modifiers:", [(m.name, m.type, m.show_viewport) for m in mesh.modifiers])
print("parents:", mesh.parent.name if mesh.parent else None)
print("vertex_groups on mesh:", len(mesh.vertex_groups))

def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def eval_verts():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    vs = np.empty(len(ev.data.vertices)*3, dtype=np.float64)
    ev.data.vertices.foreach_get("co", vs)
    vs = vs.reshape(-1,3)
    m44 = np.array(mesh.matrix_world)
    return (m44 @ np.hstack([vs, np.ones((len(vs),1))]).T).T[:, :3]

rest = eval_verts()
center = np.array([0.014, 0.048, 0.101])
i = int(np.linalg.norm(rest - center, axis=1).argmin())
name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
v = mesh.data.vertices[i]
print("tracked vert", i, "rest pos", rest[i], "weights", {name_by_idx[ge.group]: round(ge.weight,3) for ge in v.groups if ge.weight>0.01})

for bn, ax, dg_ in [("Root",'Z',35),("UpperArm.L",'X',-22),("Forearm.L",'Y',-42),("Hand.L",'X',-8)]:
    rot(rig.pose.bones[bn], ax, dg_)
bpy.context.view_layer.update()
posed = eval_verts()
print("tracked vert after pose:", posed[i], "moved", np.linalg.norm(posed[i]-rest[i]))

# 直接对 Palm 硬转 90 度看看网格跟不跟
rig2 = rig
for pb in rig2.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
rest2 = eval_verts()
rot(rig2.pose.bones["UpperArm.L"], 'Y', 90)
bpy.context.view_layer.update()
posed2 = eval_verts()
pb = rig2.pose.bones["UpperArm.L"]
print("UpperArm.L matrix_basis after rotY90:", [round(x,3) for row in pb.matrix_basis for x in row])
print("bone head world:", tuple(round(c,4) for c in (rig2.matrix_world @ pb.matrix).translation))
print("vert moved by rotY90:", np.linalg.norm(posed2[i]-rest2[i]))
