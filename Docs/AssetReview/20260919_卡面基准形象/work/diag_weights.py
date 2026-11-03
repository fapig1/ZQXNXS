# 诊断权重污染：rest 状态下统计顶点权重归属异常
import bpy
from mathutils import Vector

mesh = bpy.data.objects["Pet01_Mesh"]
arm = bpy.data.objects["Pet01Rig"].data
ARM_BONES = ["UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"]
BODY_BONES = ["Root","Pelvis","Spine","Chest","Head","Tail","Thigh.L","Shin.L","Foot.L","Thigh.R","Shin.R","Foot.R"]

segs = {}
for b in arm.bones:
    segs[b.name] = (Vector(b.head_local), Vector(b.tail_local))

def dist_seg(p, a, b):
    ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
    t = max(0.0, min(1.0, t))
    return (p - (a + ab * t)).length

name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
stats = {"belly_with_arm_w":0, "belly_arm_examples":[], "arm_with_body_w":0, "arm_body_examples":[]}
for v in mesh.data.vertices:
    gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups if ge.group in name_by_idx}
    arm_w = sum(w for n,w in gw.items() if n in ARM_BONES)
    body_w = sum(w for n,w in gw.items() if n in BODY_BONES)
    d_arm = min((dist_seg(v.co, *segs[n]) for n in ARM_BONES))
    in_belly = abs(v.co.x) < 0.055 and v.co.y > 0.02 and 0.04 < v.co.z < 0.15
    if in_belly and arm_w > 0.02:
        stats["belly_with_arm_w"] += 1
        if len(stats["belly_arm_examples"]) < 8:
            stats["belly_arm_examples"].append((v.index, tuple(round(c,3) for c in v.co), {n:round(w,3) for n,w in gw.items() if w>0.01}, round(d_arm,4)))
    if d_arm < 0.018 and body_w > 0.02 and arm_w > 0.02:
        stats["arm_with_body_w"] += 1
        if len(stats["arm_body_examples"]) < 8:
            stats["arm_body_examples"].append((v.index, tuple(round(c,3) for c in v.co), {n:round(w,3) for n,w in gw.items() if w>0.01}, round(d_arm,4)))

print("belly-zone verts with arm weight:", stats["belly_with_arm_w"])
for e in stats["belly_arm_examples"]: print("  ", e)
print("near-arm verts with body weight:", stats["arm_with_body_w"])
for e in stats["arm_body_examples"]: print("  ", e)
