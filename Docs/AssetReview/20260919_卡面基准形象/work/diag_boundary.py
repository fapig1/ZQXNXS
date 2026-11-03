# 统计网格开放边界环：位置分布、长度
import bpy
import bmesh
from mathutils import Vector

mesh = bpy.data.objects["Pet01_Mesh"]
bm = bmesh.new()
bm.from_mesh(mesh.data)

boundary_edges = [e for e in bm.edges if len(e.link_faces) == 1]
print("total boundary edges:", len(boundary_edges))

# 聚类边界环
visited = set()
loops = []
import collections
adj = collections.defaultdict(list)
for e in boundary_edges:
    adj[e.verts[0].index].append(e)
    adj[e.verts[1].index].append(e)
for e in boundary_edges:
    if e.index in visited: continue
    # BFS
    stack = [e]; visited.add(e.index)
    loop = []
    while stack:
        ce = stack.pop(); loop.append(ce)
        for v in ce.verts:
            for ne in adj[v.index]:
                if ne.index not in visited:
                    visited.add(ne.index); stack.append(ne)
    loops.append(loop)

print("boundary loops:", len(loops))
for loop in loops:
    vs = set()
    for e in loop:
        vs.update(e.verts)
    cos = [v.co for v in vs]
    mn = Vector((min(c.x for c in cos), min(c.y for c in cos), min(c.z for c in cos)))
    mx = Vector((max(c.x for c in cos), max(c.y for c in cos), max(c.z for c in cos)))
    print(f"  loop: edges={len(loop)} verts={len(vs)} bbox=({mn.x:.3f},{mn.y:.3f},{mn.z:.3f})..({mx.x:.3f},{mx.y:.3f},{mx.z:.3f})")
bm.free()
