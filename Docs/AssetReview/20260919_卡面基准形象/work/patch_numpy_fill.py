# 终版填充段重构：geom_inner 立即转 numpy 位置，避免 bmesh 包装器失效
s = open("pose_render3.py", encoding="utf-8").read()
start = s.index("    # 填充前快照")
end = s.index("    # 残余边界收尾")
new = r'''    # 填充前快照
    snapshot = set()
    for f in bm.faces:
        snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
    # 材质槽
    skin_idx = 0
    for si, slot in enumerate(mesh.material_slots):
        if slot.material and slot.material.name == 'Pet01_Skin':
            skin_idx = si; break
    uvlay = bm.loops.layers.uv.active
    # body UV 源
    bpos, buv = [], []
    if uvlay is not None:
        for v in bm.verts:
            try:
                if not v.is_valid: continue
            except ReferenceError: continue
            if pos2armw.get(pos_key(v.co), 0.0) >= 0.2: continue
            try:
                uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            except ReferenceError: continue
            if not uvs: continue
            bpos.append([v.co.x, v.co.y, v.co.z])
            buv.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
    Bp = np.array(bpos) if bpos else np.zeros((0,3))
    Bu = np.array(buv) if buv else np.zeros((0,2))
    # arm 膜锚点：前臂区平均 UV（排除手部棕色区）
    fore_uvs = []
    if uvlay is not None:
        for v in bm.verts:
            try:
                if not v.is_valid: continue
            except ReferenceError: continue
            if pos2armw.get(pos_key(v.co), 0.0) < 0.8: continue
            if dist_seg(v.co, *SEGS["Forearm.L"]) > 0.015 and dist_seg(v.co, *SEGS["Forearm.R"]) > 0.015: continue
            if dist_seg(v.co, *SEGS["Hand.L"]) < 0.025 or dist_seg(v.co, *SEGS["Hand.R"]) < 0.025: continue
            try:
                uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            except ReferenceError: continue
            if uvs:
                fore_uvs.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
    arm_anchor = (sum(u[0] for u in fore_uvs)/len(fore_uvs), sum(u[1] for u in fore_uvs)/len(fore_uvs)) if fore_uvs else (0.5, 0.5)
    WZ = 5.0

    def nearest_body_uv(co3):
        if len(Bp) == 0: return (0.5, 0.5)
        cow = np.array([co3[0], co3[1], co3[2]*WZ])
        best = None
        for s0 in range(0, len(Bp), 4096):
            Bw = Bp.copy(); Bw[:,2] *= WZ
            d2 = ((Bw[s0:s0+4096] - cow)**2).sum(-1)
            j = int(np.argmin(d2))
            if best is None or d2[j] < best[0]:
                best = (d2[j], Bu[s0+j])
        return tuple(best[1])

    loop_report = []
    n_binner = n_ainner = 0
    body_methods = []
    for loop in boundary_loops(bm):
        vs = loop_verts(loop)
        ring_keys = {pos_key(v.co) for v in vs}
        ringA = np.array([k for k in ring_keys])
        cen = sum((v.co for v in vs), Vector()) / len(vs)
        if is_mouth_co(cen):
            loop_report.append(("mouth_skip", len(vs)))
            continue
        nv_arm = sum(1 for k in ring_keys if pos2armw.get(k, 0.0) >= 0.5)
        cls = "arm" if nv_arm > len(ring_keys)/2 else "body"
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        # 捕获本环新填充面
        new_faces = [f for f in bm.faces
                     if frozenset(pos_key(v.co) for v in f.verts) not in snapshot]
        for f in new_faces:
            snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
            try:
                f.material_index = skin_idx
                f.smooth = True   # 平滑着色：消除角状碎面
            except ReferenceError: pass
        if not new_faces:
            loop_report.append((cls+"_nofill", len(vs)))
            continue
        # UV（细分前先设周圈，细分内部自动插值）
        if uvlay is not None:
            if cls == "body":
                for f in new_faces:
                    try:
                        for loop in f.loops:
                            loop[uvlay].uv = nearest_body_uv(loop.vert.co)
                    except ReferenceError: pass
            else:
                for f in new_faces:
                    try:
                        for loop in f.loops:
                            loop[uvlay].uv = arm_anchor
                    except ReferenceError: pass
        # 细分 → geom_inner 立即转 numpy 位置
        edges = set()
        for f in new_faces:
            try:
                for e in f.edges: edges.add(e)
            except ReferenceError: pass
        cuts = 3 if cls == "body" else 2
        ret = bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=cuts, use_grid_fill=False)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        inner_idx, innerP = [], []
        for v in ret.get('geom_inner', []):
            try:
                if v.is_valid:
                    inner_idx.append(v.index)
                    innerP.append([v.co.x, v.co.y, v.co.z])
            except ReferenceError: continue
        innerP = np.array(innerP) if innerP else np.zeros((0,3))
        if cls == "body":
            n_binner += len(inner_idx)
            method = "none"
            if len(innerP):
                fit_pts = [np.array(k) for k in ring_keys]
                for k in ring_keys:
                    d = np.linalg.norm(Bp - np.array(k), axis=1)
                    j = int(np.argmin(d))
                    if d[j] < 0.006:
                        fit_pts.append(Bp[j])
                fit = fit_sphere(fit_pts) if len(fit_pts) >= 8 else None
                if fit is not None and fit[1] < 0.5 and fit[2] < 0.008:
                    c, r = fit[0], fit[1]
                    method = f"sphere(r={r:.4f}, rms={fit[2]*1000:.2f}mm)"
                    nrm = innerP - c
                    ln = np.maximum(np.linalg.norm(nrm, axis=1), 1e-9)[:, None]
                    innerP = c + nrm/ln*r
                # 2 轮近邻平滑（3mm 内平均）
                for _ in range(2):
                    if len(innerP) < 2: break
                    D = ((innerP[:, None, :] - innerP[None, :, :])**2).sum(-1)
                    np.fill_diagonal(D, 1e9)
                    mask = D < (0.003**2)
                    cnt = mask.sum(1)[:, None]
                    avg = (innerP[None, :, :] * mask[:, :, None]).sum(1) / np.maximum(cnt, 1)
                    upd = cnt[:, 0] > 0
                    innerP[upd] = innerP[upd]*0.5 + avg[upd]*0.5
                if c is not None:
                    nrm = innerP - c
                    ln = np.maximum(np.linalg.norm(nrm, axis=1), 1e-9)[:, None]
                    innerP = c + nrm/ln*r
                # 羽化：距环 <10mm 的顶点向细分原位回混（消除高度差锯齿）
                for t, i in enumerate(inner_idx):
                    dmin = float(np.min(np.linalg.norm(ringA - innerP[t], axis=1)))
                    w = min(dmin / 0.010, 1.0)
                    try:
                        base = Vector(bm.verts[i].co)
                        tgt = Vector(innerP[t])
                        bm.verts[i].co = base.lerp(tgt, w)
                    except ReferenceError: pass
            loop_report.append(("body_fill", len(inner_idx), method))
        else:
            n_ainner += len(inner_idx)
            if len(innerP):
                for _ in range(2):
                    if len(innerP) < 2: break
                    D = ((innerP[:, None, :] - innerP[None, :, :])**2).sum(-1)
                    np.fill_diagonal(D, 1e9)
                    mask = D < (0.003**2)
                    cnt = mask.sum(1)[:, None]
                    avg = (innerP[None, :, :] * mask[:, :, None]).sum(1) / np.maximum(cnt, 1)
                    upd = cnt[:, 0] > 0
                    innerP[upd] = innerP[upd]*0.6 + avg[upd]*0.4
                # 内缩 0.5mm：向最近臂骨方向
                for t, i in enumerate(inner_idx):
                    side_bones = ARMSIDE["L"] if innerP[t][0] >= 0 else ARMSIDE["R"]
                    bestp = None
                    for n in side_bones:
                        a, b = SEGS[n]
                        ab = b - a
                        q = np.array(innerP[t])
                        qa = np.array([a.x, a.y, a.z]); qb = np.array([b.x, b.y, b.z])
                        abv = qb - qa
                        tt = float(np.dot(q - qa, abv) / max(np.dot(abv, abv), 1e-12))
                        tt = min(max(tt, 0.0), 1.0)
                        pt = qa + abv*tt
                        d = float(np.linalg.norm(q - pt))
                        if bestp is None or d < bestp[0]:
                            bestp = (d, pt)
                    if bestp is not None and bestp[0] > 1e-6:
                        dire = (bestp[1] - innerP[t]) / max(bestp[0], 1e-9)
                        innerP[t] = innerP[t] + dire * 0.0005
                for t, i in enumerate(inner_idx):
                    try:
                        bm.verts[i].co = Vector(innerP[t])
                    except ReferenceError: pass
            loop_report.append(("arm_fill", len(inner_idx)))
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    p(f"SURGERY body_fill_inner={n_binner} arm_fill_inner={n_ainner} methods={body_methods}")

'''
s = s[:start] + new + s[end:]
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("rewritten fill section")
