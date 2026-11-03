# 姿态后位移诊断：找出位移最大的顶点与权重归属
import bpy, sys
from mathutils import Matrix, Vector
import numpy as np

mesh = bpy.data.objects["Pet01_Mesh"]
rig = bpy.data.objects["Pet01Rig"]
arm = rig.data
if rig.animation_data: rig.animation_data_clear()

def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy(); head = m.translation
    R = Matrix.Rotation(__import__("math").radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
# divine 手臂姿态（产生尖刺的构型）
for n, ax, d in [("UpperArm.L",'X',-40),("UpperArm.L",'Y',-30),("Forearm.L",'X',-14),
                 ("UpperArm.R",'X',-40),("UpperArm.R",'Y',30),("Forearm.R",'X',-14)]:
    rot(rig.pose.bones[n], ax, d)
bpy.context.view_layer.update()

name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
rest = np.empty(len(mesh.data.vertices)*3); mesh.data.vertices.foreach_get("co", rest)
rest = rest.reshape(-1,3)
dg = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(dg)
defo = np.empty(len(ev.data.vertices)*3); ev.data.vertices.foreach_get("co", defo)
defo = defo.reshape(-1,3)
mw = ev.matrix_world
disp = np.linalg.norm(defo - rest, axis=1)
order = np.argsort(-disp)
print("top displaced verts (idx, rest, defo, disp, weights):")
for i in order[:30]:
    gw = {name_by_idx[ge.group]: round(ge.weight,3) for ge in mesh.data.vertices[i].groups if ge.weight>0.01}
    print(f"  v{i} rest={rest[i].round(3)} defo={defo[i].round(3)} d={disp[i]:.3f} w={gw}")
# 位移直方图
for th in (0.01,0.02,0.03,0.05):
    print(f"disp>{th}: {(disp>th).sum()} verts")
