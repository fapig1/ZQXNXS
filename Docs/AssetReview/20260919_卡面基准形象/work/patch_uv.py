# 修补：为新填充面从相邻原始面复制周圈 UV（否则 N-gon 采样 (0,0) 出现棕斑/白 smear）
s = open("pose_render3.py", encoding="utf-8").read()
old = """    for f in bm.faces:
        key = frozenset(pos_key(v.co) for v in f.verts)
        if key in snapshot: continue
        ws = [pos2armw.get(pos_key(v.co), 0.0) for v in f.verts]
        if sum(1 for w in ws if w >= 0.5) > len(ws)/2:
            arm_fill_faces.append(f)
        else:
            body_fill_faces.append(f)
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()"""
new = """    for f in bm.faces:
        key = frozenset(pos_key(v.co) for v in f.verts)
        if key in snapshot: continue
        ws = [pos2armw.get(pos_key(v.co), 0.0) for v in f.verts]
        if sum(1 for w in ws if w >= 0.5) > len(ws)/2:
            arm_fill_faces.append(f)
        else:
            body_fill_faces.append(f)
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    # 周圈 UV 复制：新填充面的每条 loop 从相邻原始面的同边同顶点 loop 拷贝 UV
    uvlay = bm.loops.layers.uv.active
    if uvlay is not None:
        new_set = set(id(f) for f in body_fill_faces) | set(id(f) for f in arm_fill_faces)
        n_uv = 0
        for f in body_fill_faces + arm_fill_faces:
            for loop in f.loops:
                v, e = loop.vert, loop.edge
                src = None
                for f2 in e.link_faces:
                    if id(f2) in new_set: continue
                    for l2 in f2.loops:
                        if l2.vert.index == v.index:
                            src = l2; break
                    if src: break
                if src is not None:
                    loop[uvlay].uv = src[uvlay].uv
                    n_uv += 1
        p(f"SURGERY uv copied loops={n_uv}")"""
assert old in s
s = s.replace(old, new)
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched uv copy")
