# 修补 v3：填充面材质槽改 Skin、UV 用最近肚皮顶点、球面投影加羽化
s = open("pose_render3.py", encoding="utf-8").read()

old = """    # 周圈 UV 复制：新填充面的每条 loop 从相邻原始面的同边同顶点 loop 拷贝 UV
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
new = """    # 材质槽：填充面一律用 Pet01_Skin（默认 index0 可能是 Oral 深棕）
    skin_idx = 0
    for si, slot in enumerate(mesh.material_slots):
        if slot.material and slot.material.name == 'Pet01_Skin':
            skin_idx = si; break
    for f in body_fill_faces + arm_fill_faces:
        f.material_index = skin_idx
    # UV：每个填充面 loop 取"最近肚皮顶点"的 UV（贴图与肚皮一致，避免采样到臂面棕区）
    uvlay = bm.loops.layers.uv.active
    if uvlay is not None:
        bpos, buv = [], []
        for v in bm.verts:
            if not v.is_valid: continue
            if pos2armw.get(pos_key(v.co), 0.0) >= 0.2: continue
            uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            if not uvs: continue
            bpos.append([v.co.x, v.co.y, v.co.z])
            buv.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
        B = np.array(bpos); U = np.array(buv)
        n_uv = 0
        step = max(1, len(B)//64)
        for f in body_fill_faces + arm_fill_faces:
            for loop in f.loops:
                co = np.array([loop.vert.co.x, loop.vert.co.y, loop.vert.co.z])
                best = None
                for s0 in range(0, len(B), 4096):
                    d2 = ((B[s0:s0+4096] - co)**2).sum(-1)
                    j = int(np.argmin(d2))
                    if best is None or d2[j] < best[0]:
                        best = (d2[j], U[s0+j])
                loop[uvlay].uv = best[1]
                n_uv += 1
        p(f"SURGERY uv nearest-body assigned loops={n_uv} skin_slot={skin_idx}")"""
assert old in s
s = s.replace(old, new)

# 羽化：投影后对靠近环的内部顶点向投影前位置回混
old = """            if c is not None:
                for v in inner:
                    if not v.is_valid: continue
                    d = Vector(v.co) - Vector(c)
                    if d.length > 1e-9:
                        v.co = Vector(c) + d.normalized() * r
                bm.verts.ensure_lookup_table()"""
new = """            if c is not None:
                # 羽化：距环越近越保留细分后的原位置，避免与周围曲面出现高度差锯齿
                rpos = [Vector(k) for k in ring_pos]
                for v in inner:
                    if not v.is_valid: continue
                    dv = Vector(v.co)
                    dmin = min((dv - q).length for q in rpos)
                    w = min(dmin / 0.010, 1.0)
                    base = pre_proj.get(v.index, dv)
                    d = dv - Vector(c)
                    if d.length > 1e-9:
                        proj = Vector(c) + d.normalized() * r
                        v.co = base.lerp(proj, w)
                bm.verts.ensure_lookup_table()"""
assert old in s
s = s.replace(old, new)
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched material/uv/feather")
