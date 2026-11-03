# 重写 pose_render3.py 的 surgery() 为最终版：
# 删混合面 → 过渡边拆分(跳口部) → 逐环独立 holes_fill(捕获新面) →
# body 填充面细分+geom_inner 球面投影恢复鼓度 / arm 填充面细分+轻度松弛 →
# 残余环 contextual 收尾 → 法线一致化 → 权重重分配(肩桥豁免)
src = open("pose_render3.py", encoding="utf-8").read()
start = src.index("# ---------------- 网格手术 ----------------")
end = src.index("# ---------------- 权重清理（保护臂侧） ----------------")
new = r'''# ---------------- 网格手术 ----------------
ARM_SIDE_VERTS = set()

def boundary_loops(bm, edges=None):
    if edges is None:
        edges = [e for e in bm.edges if len(e.link_faces) == 1]
    eset = set(edges)
    adj = {}
    for e in eset:
        adj.setdefault(e.verts[0].index, []).append(e)
        adj.setdefault(e.verts[1].index, []).append(e)
    visited = set()
    loops = []
    for e0 in eset:
        if e0 in visited: continue
        loop = [e0]; visited.add(e0)
        for endpoint in (e0.verts[0], e0.verts[1]):
            cur_v = endpoint; prev_e = e0
            while True:
                nxt = None
                for e in adj.get(cur_v.index, []):
                    if e in visited or e is prev_e: continue
                    nxt = e; break
                if nxt is None: break
                loop.append(nxt); visited.add(nxt)
                cur_v = nxt.verts[0] if nxt.verts[1].index == cur_v.index else nxt.verts[1]
                prev_e = nxt
                if cur_v.index == endpoint.index: break
        loops.append(loop)
    return loops

def loop_verts(loop):
    if len(loop) == 1:
        return list(loop[0].verts)
    vs = list(loop[0].verts)
    for e in loop[1:]:
        nxt = e.verts[0] if e.verts[0].index != vs[-1].index else e.verts[1]
        vs.append(nxt)
    if len(vs) > 2 and vs[0].index == vs[-1].index:
        vs = vs[:-1]
    return vs

def fit_sphere(pts):
    P = np.asarray(pts, dtype=np.float64)
    A = np.hstack([2*P, np.ones((len(P),1))])
    b = (P**2).sum(axis=1)
    try:
        sol, *_ = np.linalg.lstsq(A, b, rcond=None)
    except Exception:
        return None
    c = sol[:3]; r2 = sol[3] + (c**2).sum()
    if r2 <= 0: return None
    r = math.sqrt(r2)
    rms = float(np.sqrt(((np.linalg.norm(P-c, axis=1)-r)**2).mean()))
    return (c, r, rms)

def is_mouth_co(co):
    return co.z > 0.17 and co.y > 0.01

def surgery():
    import bmesh
    me = mesh.data
    name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
    ARMSET = set(ARM_BONES)
    # Pass A：顶点手臂权重 + 待删混合面
    N = len(me.vertices)
    armw = np.zeros(N)
    for v in me.vertices:
        armw[v.index] = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
    pos2armw = {pos_key(v.co): float(armw[v.index]) for v in me.vertices}
    cut_faces = set()
    for i, poly in enumerate(me.polygons):
        c = poly.center
        in_zone = (d_zone(c) < 0.030) or \
                  (min(dist_seg(c, *SEGS[n]) for n in ("UpperArm.L","UpperArm.R")) < 0.030
                   and d_shoulder(c) > 0.014)
        if not in_zone: continue
        ws = [armw[me.vertices[me.loops[li].vertex_index].index] for li in range(poly.loop_start, poly.loop_start+poly.loop_total)]
        if max(ws) > 0.55 and min(ws) < 0.45:
            cut_faces.add(i)
    # Pass B（edit 态）
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()
    pre_boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    cut = [f for f in bm.faces if f.index in cut_faces]
    bmesh.ops.delete(bm, geom=cut, context='FACES')
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()

    def vclass(v):
        return 1 if pos2armw.get(pos_key(v.co), 0.0) >= 0.5 else 0

    # 过渡边拆分（口部除外）
    n_split = 0
    for _ in range(4):
        tedges = [e for e in bm.edges if len(e.link_faces) == 1
                  and not is_mouth_co((e.verts[0].co+e.verts[1].co)/2)
                  and vclass(e.verts[0]) != vclass(e.verts[1])]
        if not tedges: break
        bmesh.ops.split_edges(bm, edges=tedges)
        n_split += len(tedges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()

    def fill_loop(loop):
        """补一个环，返回 (class, new_faces)"""
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
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()

    def subdivide_fill(faces, cuts):
        edges = set()
        for f in faces:
            for e in f.edges: edges.add(e)
        ret = bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=cuts, use_grid_fill=False)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        return ret.get('geom_inner', [])

    # body 填充：细分 + 球面投影（恢复肚皮鼓度）
    body_method = "none"
    n_binner = 0
    if body_fill_faces:
        ring_pos = {pos_key(v.co) for f in body_fill_faces for v in f.verts}
        inner = subdivide_fill(body_fill_faces, 3)
        inner = [v for v in inner if v.is_valid]
        n_binner = len(inner)
        if inner:
            fit_pts = [np.array([v.co.x, v.co.y, v.co.z]) for v in ring_pos]
            vidx = {pos_key(v.co): v for v in bm.verts if v.is_valid}
            for k in list(ring_pos):
                vv = vidx.get(k)
                if vv is None or not vv.is_valid: continue
                for e in vv.link_edges:
                    o = e.other_vert(vv)
                    if not o.is_valid or pos_key(o.co) in ring_pos: continue
                    if pos2armw.get(pos_key(o.co), 0.0) >= 0.5: continue
                    fit_pts.append(np.array([o.co.x, o.co.y, o.co.z]))
            fit = fit_sphere(fit_pts) if len(fit_pts) >= 8 else None
            c = r = rms = None
            if fit is not None and fit[1] < 0.5 and fit[2] < 0.005:
                c, r, rms = fit
                body_method = f"sphere(r={r:.4f}, rms={rms*1000:.2f}mm)"
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
                    d = Vector(v.co) - Vector(c)
                    if d.length > 1e-9:
                        v.co = Vector(c) + d.normalized() * r
                bm.verts.ensure_lookup_table()

    # arm 填充：细分 + 轻度松弛（隐藏膜）
    n_ainner = 0
    if arm_fill_faces:
        inner = subdivide_fill(arm_fill_faces, 2)
        inner = [v for v in inner if v.is_valid]
        n_ainner = len(inner)
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

    # 残余边界收尾
    for _ in range(3):
        res_edges = [e for e in bm.edges if len(e.link_faces) == 1
                     and not is_mouth_co((e.verts[0].co+e.verts[1].co)/2)
                     and d_zone((e.verts[0].co+e.verts[1].co)/2) < 0.06]
        if not res_edges: break
        bmesh.ops.contextual_create(bm, geom=res_edges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.mesh.select_all(action='DESELECT')
    post_boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    post_nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bmesh.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Pass C：权重重分配（肩桥豁免）
    me = mesh.data
    pos2idx = {pos_key(v.co): v.index for v in me.vertices}
    arm_ring_idx = {pos2idx[k] for k in arm_ring_pos if k in pos2idx} if False else set()
    # 臂侧判定改用填充面顶点位置
    arm_side_pos = {pos_key(v.co) for f in arm_fill_faces for v in f.verts} if False else set()
    # 用"armw>0.5 或 位于 body 填充环外的 subdivided 内部"判定；简化：armw>0.5 即臂侧
    ARM_SIDE_VERTS.clear()
    n_boost = n_strip = 0
    for v in me.vertices:
        i = v.index
        aw_v = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
        if aw_v > 0.5 and d_shoulder(v.co) > 0.014:
            side = "L" if v.co.x >= 0 else "R"
            bones = ARMSIDE[side]
            ds = {n: dist_seg(v.co, *SEGS[n]) + 1e-5 for n in bones}
            inv = {n: (1.0/ds[n])**2 for n in bones}
            ssum = sum(inv.values())
            for n in ARM_BONES:
                mesh.vertex_groups[n].add([i], inv[n]/ssum if n in bones else 0.0, 'REPLACE')
            for ge in v.groups:
                n = name_by_idx.get(ge.group)
                if n is not None and n not in ARMSET:
                    mesh.vertex_groups[n].add([i], 0.0, 'REPLACE')
            ARM_SIDE_VERTS.add(i); n_boost += 1
        elif aw_v <= 0.45 and d_shoulder(v.co) > 0.014:
            changed = False
            for ge in v.groups:
                n = name_by_idx.get(ge.group)
                if n in ARMSET:
                    mesh.vertex_groups[n].add([i], 0.0, 'REPLACE'); changed = True
            if changed:
                gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
                ssum = sum(gw.values())
                if ssum > 0.05:
                    for n, w in gw.items():
                        mesh.vertex_groups[n].add([i], w/ssum, 'REPLACE')
                else:
                    fb = "Spine" if v.co.z < 0.13 else "Chest"
                    mesh.vertex_groups[fb].add([i], 1.0, 'ADD')
                n_strip += 1
    # 残余边界归属
    bm3 = bmesh.new(); bm3.from_mesh(me)
    zones = {}
    for e in bm3.edges:
        if len(e.link_faces) != 1: continue
        c = (e.verts[0].co + e.verts[1].co)/2
        z = "mouth" if is_mouth_co(c) else ("arm_zone!!" if d_zone(c) < 0.06 else "other")
        zones[z] = zones.get(z, 0) + 1
    bm3.free()
    p(f"SURGERY: pre_boundary={pre_boundary} cut_faces={len(cut_faces)} loops={loop_report} "
      f"body_inner={n_binner}({body_method}) arm_inner={n_ainner} "
      f"boost={n_boost} strip={n_strip} post_boundary={post_boundary} post_nonmanifold={post_nonman}")
    p(f"SURGERY residual boundary by zone: {zones}")
    return post_boundary, post_nonman

'''
open("pose_render3.py", "w", encoding="utf-8").write(src[:start] + new + src[end:])
print("rewritten final surgery")
