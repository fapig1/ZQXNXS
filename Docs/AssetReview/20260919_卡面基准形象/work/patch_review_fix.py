# 终审修复补丁：1) 填充面 smooth=True（消除角状碎面）2) 取消肩桥豁免完全分离（消除拉伸翻边）
# 3) arm 膜沿骨骼内缩 0.5mm 4) per-loop 独立球面投影
s = open("pose_render3.py", encoding="utf-8").read()

# 1) 切割区取消肩桥豁免
old = """        in_zone = (d_zone(c) < 0.030) or \\
                  (min(dist_seg(c, *SEGS[n]) for n in ("UpperArm.L","UpperArm.R")) < 0.030
                   and d_shoulder(c) > 0.014)
        if not in_zone: continue"""
new = """        in_zone = (d_zone(c) < 0.030) or \\
                  (min(dist_seg(c, *SEGS[n]) for n in ("UpperArm.L","UpperArm.R")) < 0.030)
        if not in_zone: continue"""
assert old in s, "in_zone"
s = s.replace(old, new)

# 2) 重写填充段：per-loop 管线
start = s.index("    # 填充前快照")
end = s.index("    # 残余边界收尾")
new_mid = r'''    # 填充前快照
    snapshot = set()
    for f in bm.faces:
        snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
    # 材质槽
    skin_idx = 0
    for si, slot in enumerate(mesh.material_slots):
        if slot.material and slot.material.name == 'Pet01_Skin':
            skin_idx = si; break
    uvlay = bm.loops.layers.uv.active
    # body UV 源：body 顶点(armw<0.2) 位置与平均 UV（z 加权最近邻匹配用）
    bpos, buv = [], []
    if uvlay is not None:
        for v in bm.verts:
            if not v.is_valid: continue
            if pos2armw.get(pos_key(v.co), 0.0) >= 0.2: continue
            uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            if not uvs: continue
            bpos.append([v.co.x, v.co.y, v.co.z])
            buv.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
    Bp = np.array(bpos) if bpos else np.zeros((0,3))
    Bu = np.array(buv) if buv else np.zeros((0,2))
    # arm 膜锚点：前臂区平均 UV（排除手部棕色区）
    fore_uvs = []
    if uvlay is not None:
        for v in bm.verts:
            if not v.is_valid: continue
            if pos2armw.get(pos_key(v.co), 0.0) < 0.8: continue
            if dist_seg(v.co, *SEGS["Forearm.L"]) > 0.015 and dist_seg(v.co, *SEGS["Forearm.R"]) > 0.015: continue
            if dist_seg(v.co, *SEGS["Hand.L"]) < 0.025 or dist_seg(v.co, *SEGS["Hand.R"]) < 0.025: continue
            uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            if uvs:
                fore_uvs.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
    arm_anchor = (sum(u[0] for u in fore_uvs)/len(fore_uvs), sum(u[1] for u in fore_uvs)/len(fore_uvs)) if fore_uvs else (0.5, 0.5)
    WZ = 5.0

    loop_report = []
    n_binner = n_ainner = 0
    body_methods = []
    for loop in boundary_loops(bm):
        vs = loop_verts(loop)
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        if is_mouth_co(cen):
            loop_report.append(("mouth_skip", len(vs)))
            continue
        nv_arm = sum(vclass(v) for v in vs)
        cls = "arm" if nv_arm > len(vs)/2 else "body"
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        # 捕获本环新填充面
        new_faces = [f for f in bm.faces
                     if frozenset(pos_key(v.co) for v in f.verts) not in snapshot]
        for f in new_faces:
            snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
            f.material_index = skin_idx
            f.smooth = True            # 平滑着色：消除角状碎面观感
        if not new_faces:
            loop_report.append((cls+"_nofill", len(vs)))
            continue
        # UV
        if uvlay is not None and cls == "body" and len(Bp):
            for f in new_faces:
                for loop in f.loops:
                    co = np.array([loop.vert.co.x, loop.vert.co.y, loop.vert.co.z*WZ])
                    best = None
                    for s0 in range(0, len(Bp), 4096):
                        Bw = Bp.copy(); Bw[:,2] *= WZ
                        d2 = ((Bw[s0:s0+4096] - co)**2).sum(-1)
                        j = int(np.argmin(d2))
                        if best is None or d2[j] < best[0]:
                            best = (d2[j], Bu[s0+j])
                    loop[uvlay].uv = best[1]
        elif uvlay is not None:
            for f in new_faces:
                for loop in f.loops:
                    loop[uvlay].uv = arm_anchor
        # 细分
        edges = set()
        for f in new_faces:
            for e in f.edges: edges.add(e)
        cuts = 3 if cls == "body" else 2
        ret = bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=cuts, use_grid_fill=False)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        inner = [v for v in ret.get('geom_inner', []) if v.is_valid]
        for f in new_faces:
            if f.is_valid: f.smooth = True
        if cls == "body":
            n_binner += len(inner)
            if inner:
                ringV = [Vector(k) for k in {pos_key(v.co) for v in vs}]
                fit_pts = [np.array(k) for k in {pos_key(v.co) for v in vs}]
                vidx = {pos_key(v.co): v for v in bm.verts if v.is_valid}
                for k in {pos_key(v.co) for v in vs}:
                    vv = vidx.get(k)
                    if vv is None or not vv.is_valid: continue
                    for e in vv.link_edges:
                        o = e.other_vert(vv)
                        if not o.is_valid or pos_key(o.co) in {pos_key(x.co) for x in vs}: continue
                        if pos2armw.get(pos_key(o.co), 0.0) >= 0.5: continue
                        fit_pts.append(np.array([o.co.x, o.co.y, o.co.z]))
                fit = fit_sphere(fit_pts) if len(fit_pts) >= 8 else None
                pre_proj = {v.index: Vector(v.co) for v in inner}
                c = r = None
                if fit is not None and fit[1] < 0.5 and fit[2] < 0.008:
                    c, r = fit[0], fit[1]
                    body_methods.append(f"r={r:.3f}")
                    for v in inner:
                        d = Vector(v.co) - Vector(c)
                        if d.length > 1e-9:
                            v.co = Vector(c) + d.normalized() * r
                    bm.verts.ensure_lookup_table()
                for _ in range(2):
                    newpos = {}
                    for v in inner:
                        if not v.is_valid: continue
                        n = [e.other_vert(v) for e in v.link_edges]
                        n = [o for o in n if o.is_valid]
                        if not n: continue
                        avg = sum((o.co for o in n), Vector()) / len(n)
                        newpos[v.index] = v.co.lerp(avg, 0.5)
                    for i, npv in newpos.items():
                        bm.verts[i].co = npv
                    bm.verts.ensure_lookup_table()
                if c is not None:
                    for v in inner:
                        if not v.is_valid: continue
                        dv = Vector(v.co)
                        dmin = min((dv - q).length for q in ringV)
                        w = min(dmin / 0.010, 1.0)
                        d = dv - Vector(c)
                        if d.length > 1e-9:
                            proj = Vector(c) + d.normalized() * r
                            v.co = pre_proj.get(v.index, dv).lerp(proj, w)
                    bm.verts.ensure_lookup_table()
            loop_report.append(("body_fill", len(inner)))
        else:
            n_ainner += len(inner)
            if inner:
                for _ in range(2):
                    newpos = {}
                    for v in inner:
                        if not v.is_valid: continue
                        n = [e.other_vert(v) for e in v.link_edges]
                        n = [o for o in n if o.is_valid]
                        if not n: continue
                        avg = sum((o.co for o in n), Vector()) / len(n)
                        newpos[v.index] = v.co.lerp(avg, 0.4)
                    for i, npv in newpos.items():
                        bm.verts[i].co = npv
                    bm.verts.ensure_lookup_table()
                # 内缩 0.5mm：沿"指向最近臂骨"方向，保证膜不凸出臂轮廓
                for v in inner:
                    if not v.is_valid: continue
                    side_bones = ARMSIDE["L"] if v.co.x >= 0 else ARMSIDE["R"]
                    bestp = None
                    for n in side_bones:
                        a, b = SEGS[n]
                        ab = b - a; t = (v.co - a).dot(ab) / max(ab.dot(ab), 1e-12)
                        t = max(0.0, min(1.0, t))
                        pt = a + ab*t
                        d = (pt - v.co).length
                        if bestp is None or d < bestp[0]:
                            bestp = (d, pt)
                    if bestp is not None and bestp[0] > 1e-6:
                        v.co = v.co + (bestp[1] - v.co).normalized() * 0.0005
                bm.verts.ensure_lookup_table()
            loop_report.append(("arm_fill", len(inner)))

'''
s = s[:start] + new_mid + s[end:]

# 3) Pass C 去掉肩桥豁免
old = """        if aw_v > 0.5 and d_shoulder(v.co) > 0.014:"""
new = """        if aw_v > 0.5:"""
assert old in s
s = s.replace(old, new)
old = """        elif aw_v < 0.5 and d_shoulder(v.co) > 0.014:"""
new = """        elif aw_v < 0.5:"""
assert old in s
s = s.replace(old, new)

open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched final-fix")
