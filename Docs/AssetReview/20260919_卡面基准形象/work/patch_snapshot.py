# 修补：用面快照差集捕获 holes_fill 新面，替代索引计数
s = open("pose_render3.py", encoding="utf-8").read()

old = """    def fill_loop(loop):
        \"\"\"补一个环，返回 (class, new_faces)\"\"\"
        vs = loop_verts(loop)
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        if is_mouth_co(cen):
            return ("mouth_skip", [])
        nv_arm = sum(vclass(v) for v in vs)
        cls = "arm" if nv_arm > len(vs)/2 else "body"
        cnt_before = len(bm.faces)
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.faces.ensure_lookup_table()
        new_faces = []
        for f in bm.faces:
            if f.index >= cnt_before:
                new_faces.append(f)
        return (cls, new_faces)

    body_fill_faces, arm_fill_faces = [], []
    loop_report = []
    for loop in boundary_loops(bm):
        cls, nf = fill_loop(loop)
        loop_report.append((cls, len(nf)))
        if cls == "body": body_fill_faces += nf
        elif cls == "arm": arm_fill_faces += nf
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()"""

new = """    # 面快照（按顶点位置集合），用于识别 holes_fill 新建的填充面
    snapshot = set()
    for f in bm.faces:
        snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
    body_fill_faces, arm_fill_faces = [], []
    loop_report = []
    for loop in boundary_loops(bm):
        vs = loop_verts(loop)
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        if is_mouth_co(cen):
            loop_report.append(("mouth_skip", len(vs)))
            continue
        nv_arm = sum(vclass(v) for v in vs)
        cls = "arm" if nv_arm > len(vs)/2 else "body"
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        loop_report.append((cls, len(vs)))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    for f in bm.faces:
        key = frozenset(pos_key(v.co) for v in f.verts)
        if key in snapshot: continue
        ws = [pos2armw.get(pos_key(v.co), 0.0) for v in f.verts]
        if sum(1 for w in ws if w >= 0.5) > len(ws)/2:
            arm_fill_faces.append(f)
        else:
            body_fill_faces.append(f)
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()"""
assert old in s, "fill_loop block not found"
s = s.replace(old, new)
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched snapshot capture")
