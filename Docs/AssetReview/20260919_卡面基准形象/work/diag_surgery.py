# 网格手术前诊断：
# 1) 原始网格（无任何清理）的边界边/非流形统计 —— 确认是否本来闭合
# 2) 混合面（手臂权重跨双侧）分布：到 Forearm/Hand/UpperArm 骨骼段的距离直方图
# 3) 按 armw 分类的顶点数：BODY(<0.2) / MID / ARM(>0.8)
import bpy
from mathutils import Vector
import numpy as np

mesh = bpy.data.objects["Pet01Rig"].children[0] if False else bpy.data.objects["Pet01_Mesh"]
me = mesh.data
name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
ARM_BONES = {"UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"}

# 1) 拓扑
import bmesh
bm = bmesh.new()
bm.from_mesh(me)
n_boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
n_nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
n_loose_v = sum(1 for v in bm.verts if not v.link_faces)
n_faces = len(bm.faces)
print(f"TOPO: faces={n_faces} edges={len(bm.edges)} verts={len(bm.verts)} boundary_edges={n_boundary} nonmanifold_edges={n_nonmanifold} loose_verts={n_loose_v}")
if n_boundary:
    locs = [tuple(round(c,4) for c in e.verts[0].co) for e in bm.edges if len(e.link_faces)==1][:15]
    print("boundary examples:", locs)
bm.free()

# 2) 权重分类 + 距离
arm = bpy.data.objects["Pet01Rig"].data
segs = {b: (Vector(arm.bones[b].head_local), Vector(arm.bones[b].tail_local)) for b in ARM_BONES}
def dmin(p, names):
    best = 1e9
    for n in names:
        a, b = segs[n]
        ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
        t = max(0.0, min(1.0, t))
        best = min(best, (p - (a + ab*t)).length)
    return best

N = len(me.vertices)
armw = np.zeros(N)
for v in me.vertices:
    armw[v.index] = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARM_BONES)
n_body = int((armw < 0.2).sum()); n_mid = int(((armw >= 0.2) & (armw <= 0.8)).sum()); n_arm = int((armw > 0.8).sum())
print(f"WCLASS: body(<0.2)={n_body} mid={n_mid} arm(>0.8)={n_arm}")

# 混合面分布
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
aw = [0.0]*len(bm.verts)
for v in bm.verts: aw[v.index] = armw[v.index]
buckets = {}
mixed_faces = 0
for f in bm.faces:
    mx = max(aw[v.index] for v in f.verts); mn = min(aw[v.index] for v in f.verts)
    if mx > 0.55 and mn < 0.45:
        mixed_faces += 1
        c = f.calc_center_median()
        d_f = dmin(c, ["Forearm.L","Forearm.R"])
        d_h = dmin(c, ["Hand.L","Hand.R"])
        d_u = dmin(c, ["UpperArm.L","UpperArm.R"])
        key = "hand" if d_h < 0.035 else ("forearm" if d_f < 0.030 else ("upperarm" if d_u < 0.030 else "far"))
        buckets[key] = buckets.get(key, 0) + 1
print(f"MIXED faces total={mixed_faces} by zone={buckets}")
bm.free()
