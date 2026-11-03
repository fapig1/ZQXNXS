# 诊断 v3：rage/classic 姿态下，肚皮前方区域大位移顶点的权重与贴图颜色
import bpy, math
from mathutils import Matrix, Vector
import numpy as np

rig = bpy.data.objects["Pet01Rig"]
mesh = bpy.data.objects["Pet01_Mesh"]
if rig.animation_data:
    rig.animation_data_clear()

def rot(pb, axis, deg):
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def aim(pb, target):
    t = Vector(target).normalized()
    m = pb.matrix.copy()
    d = (m.to_3x3() @ Vector((0,1,0)))
    if d.length < 1e-9: return
    d.normalize()
    R = d.rotation_difference(t).to_matrix().to_4x4()
    head = m.translation
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

name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
me = mesh.data
# UV 采样（正确方向：Blender 像素自底向上）
img = bpy.data.images.load(r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work\tex_variant\Pet01_BaseColor_rage.png", check_existing=False)
W, H = img.size
buf = np.empty(W*H*4, dtype=np.float32)
img.pixels.foreach_get(buf)
px = buf.reshape(H, W, 4)
uvl = me.uv_layers[0].data
vert_uv = np.zeros((len(me.vertices), 2))
cnt = np.zeros(len(me.vertices), dtype=np.int32)
for li, l in enumerate(me.loops):
    u, v = uvl[li].uv
    vert_uv[l.vertex_index, 0] += u; vert_uv[l.vertex_index, 1] += v
    cnt[l.vertex_index] += 1
cnt[cnt == 0] = 1
vert_uv /= cnt[:, None]

rest = eval_verts()

def sample_color(i):
    u = min(max(vert_uv[i,0],0),0.9999); v = min(max(vert_uv[i,1],0),0.9999)
    r, g, b = px[int(v*H), int(u*W), :3]
    mx = max(r,g,b); mn = min(r,g,b)
    return float(mx), float((mx-mn)/max(mx,1e-6))

import sys
pose_name = sys.argv[-1]
if pose_name == "rage":
    ops = [("rot","Root",'Z',-6),("aim","UpperArm.L",(0.78,0.52,0.10)),("aim","Forearm.L",(0.88,0.42,0.05)),("aim","Hand.L",(0.90,0.40,0.02)),("rot","Head",'Z',-16),("rot","Head",'X',5)]
else:
    ops = [("rot","Root",'Z',-30),("rot","Spine",'X',2),("rot","Head",'X',8),("aim","UpperArm.L",(0.42,-0.42,-0.80)),("aim","Forearm.L",(0.12,-0.88,-0.46)),("aim","Hand.L",(0.05,-0.95,-0.31)),("aim","UpperArm.R",(-0.42,-0.42,-0.80)),("aim","Forearm.R",(-0.12,-0.88,-0.46)),("aim","Hand.R",(-0.05,-0.95,-0.31))]
for op in ops:
    if op[0]=="rot": rot(rig.pose.bones[op[1]], op[2], op[3])
    else: aim(rig.pose.bones[op[1]], op[2])
    bpy.context.view_layer.update()
posed = eval_verts()
disp = np.linalg.norm(posed - rest, axis=1)

ARM_BONES = {"UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"}
n = 0
for i in range(len(me.vertices)):
    if disp[i] < 0.010: continue
    x, y, z = rest[i]
    if abs(x) < 0.035 and y > 0.030 and 0.04 < z < 0.145:
        v = me.vertices[i]
        w = {k: round(ge.weight,3) for ge in v.groups for k in [name_by_idx[ge.group]] if ge.weight > 0.01}
        vv, ss = sample_color(i)
        print(f"vert {i} rest=({x:.3f},{y:.3f},{z:.3f}) disp={disp[i]:.4f} v={vv:.2f} s={ss:.2f} w={w}")
        n += 1
        if n > 25: break
print("total big-disp belly-front verts:", sum(1 for i in range(len(me.vertices)) if disp[i]>=0.010 and abs(rest[i][0])<0.035 and rest[i][1]>0.030 and 0.04<rest[i][2]<0.145))
