# 修订 surgery：A) 口部环跳过填充  B) 肩桥顶点豁免 arm 提升  C) 内部平滑/投影限制在臂区
s = open("pose_render3.py", encoding="utf-8").read()

# A: 环分类时跳过口部环（z>0.17 且 y>0.01 的质心），不填充、不计环
old = """    loops = boundary_loops(bm)
    arm_ring_pos, body_ring_pos = set(), set()
    loop_report = []
    for loop in loops:
        vs = loop_verts(loop)
        nv_arm = sum(vclass(v) for v in vs)
        cls = "arm" if nv_arm > len(vs)/2 else "body"
        loop_report.append((cls, len(vs)))
        tgt = arm_ring_pos if cls == "arm" else body_ring_pos
        tgt.update(pos_key(v.co) for v in vs)
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()"""
new = """    loops = boundary_loops(bm)
    arm_ring_pos, body_ring_pos = set(), set()
    loop_report = []
    n_mouth_kept = 0
    for loop in loops:
        vs = loop_verts(loop)
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        if cen.z > 0.17 and cen.y > 0.01:
            n_mouth_kept += 1
            continue   # 口部开缝为冻结资产固有，保持原状不填充
        nv_arm = sum(vclass(v) for v in vs)
        cls = "arm" if nv_arm > len(vs)/2 else "body"
        loop_report.append((cls, len(vs)))
        tgt = arm_ring_pos if cls == "arm" else body_ring_pos
        tgt.update(pos_key(v.co) for v in vs)
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()"""
assert old in s
s = s.replace(old, new)

# A2: 口部保护从拆分阶段也要跳过——过渡边拆分只处理非口部边界边
old = """    n_split = 0
    for _ in range(4):
        tedges = [e for e in bm.edges if len(e.link_faces) == 1 and vclass(e.verts[0]) != vclass(e.verts[1])]
        if not tedges: break"""
new = """    n_split = 0
    def is_mouth_edge(e):
        c = (e.verts[0].co + e.verts[1].co) / 2
        return c.z > 0.17 and c.y > 0.01
    for _ in range(4):
        tedges = [e for e in bm.edges if len(e.link_faces) == 1
                  and not is_mouth_edge(e) and vclass(e.verts[0]) != vclass(e.verts[1])]
        if not tedges: break"""
assert old in s
s = s.replace(old, new)

# B: 肩桥顶点豁免 arm 提升
old = """        if i in arm_ring_idx or aw_v > 0.5:
            side = "L" if v.co.x >= 0 else "R\""""
new = """        if (i in arm_ring_idx or aw_v > 0.5) and d_shoulder(v.co) > 0.014:
            side = "L" if v.co.x >= 0 else "R\""""
assert old in s
s = s.replace(old, new)

# C: 内部顶点选择限制在臂区
old = """            dmin = min(float(np.linalg.norm(co - q)) for q in Rp)
            if dmin < 0.05:
                interior.append(v)"""
new = """            dmin = min(float(np.linalg.norm(co - q)) for q in Rp)
            if dmin < 0.05 and d_zone(Vector(v.co)) < 0.06:
                interior.append(v)"""
assert old in s
s = s.replace(old, new)

# C2: 球面拟合邻点也限制在臂区
old = """                for e in vv.link_edges:
                    o = e.other_vert(vv)
                    if pos_key(o.co) in body_ring_pos: continue
                    if pos2armw.get(pos_key(o.co), 0.0) >= 0.5: continue
                    fit_pts.append(np.array([o.co.x, o.co.y, o.co.z]))"""
new = """                for e in vv.link_edges:
                    o = e.other_vert(vv)
                    if pos_key(o.co) in body_ring_pos: continue
                    if pos2armw.get(pos_key(o.co), 0.0) >= 0.5: continue
                    if d_zone(Vector(o.co)) >= 0.06: continue
                    fit_pts.append(np.array([o.co.x, o.co.y, o.co.z]))"""
assert old in s
s = s.replace(old, new)

open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched A/B/C")
