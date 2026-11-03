# 混合权重诊断：手臂骨骼与躯干骨骼各占一定比例的顶点
import bpy
from mathutils import Vector
mesh = bpy.data.objects["Pet01_Mesh"]
ARM = {"UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"}
name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
n_mixed = 0; examples=[]
for v in mesh.data.vertices:
    gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups if ge.weight>0.01}
    a = sum(w for n,w in gw.items() if n in ARM)
    b = sum(w for n,w in gw.items() if n not in ARM)
    if a>0.05 and b>0.05:
        n_mixed += 1
        if len(examples)<12: examples.append((v.index, tuple(round(c,3) for c in v.co), {n:round(w,3) for n,w in gw.items()}))
print("mixed arm/body verts:", n_mixed)
for e in examples: print("  ", e)

