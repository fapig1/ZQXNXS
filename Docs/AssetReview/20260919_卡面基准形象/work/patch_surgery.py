# 将 pose_render3.py 的 surgery() 替换为 v2：环拆分 + 双侧独立闭合 + 球面投影
src = open("pose_render3.py", encoding="utf-8").read()
start = src.index("# ---------------- 网格手术 ----------------")
end = src.index("# ---------------- 权重清理（保护臂侧） ----------------")
new = r'''# ---------------- 网格手术 ----------------
ARM_SIDE_VERTS = set()

def boundary_loops(bm, edges=None):
    """把边界边组织成环（返回 list of list of bmesh edges）"""
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

def surgery():
    import bmesh
    me = mesh.data
    name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
    ARMSET = set(ARM_BONES)
    # Pass A：顶点手臂权重 + 位置映射 + 待删混合面
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
    # Pass B（edit 态）：删面 → 过渡边拆分 → 逐环独立补洞
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

    n_split = 0
    for _ in range(4):
        tedges = [e for e in bm.edges if len(e.link_faces) == 1 and vclass(e.verts[0]) != vclass(e.verts[1])]
        if not tedges: break
        bmesh.ops.split_edges(bm, edges=tedges)
        n_split += len(tedges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()
    loops = boundary_loops(bm)
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
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    # body 侧填充面：细分 + 球面投影恢复肚皮鼓度
    ringV = [Vector(k) for k in body_ring_pos]
    method = "none"
    n_interior = 0
    if ringV:
        Rp = np.array([[v.x, v.y, v.z] for v in ringV])
        mins = Rp.min(axis=0) - 0.002; maxs = Rp.max(axis=0) + 0.002
        rcent = Rp.mean(axis=0)
        # 细分：body 环顶点参与的面
        vidx_by_pos = {pos_key(v.co): v for v in bm.verts}
        seed_faces = []
        for f in bm.faces:
            fps = {pos_key(v.co) for v in f.verts}
            if fps & set(body_ring_pos):
                seed_faces.append(f)
        if seed_faces:
            sedges = set()
            for f in seed_faces:
                for e in f.edges: sedges.add(e)
            bmesh.ops.subdivide_edges(bm, edges=list(sedges), cuts=3, use_grid_fill=False)
            bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        # 内部顶点：不在两环上、位于 body 环包围盒内、距环 < 0.05
        interior = []
        for v in bm.verts:
            k = pos_key(v.co)
            if k in body_ring_pos or k in arm_ring_pos: continue
            co = np.array([v.co.x, v.co.y, v.co.z])
            if not (np.all(co >= mins) and np.all(co <= maxs)): continue
            dmin = min(float(np.linalg.norm(co - q)) for q in Rp)
            if dmin < 0.05:
                interior.append(v)
        n_interior = len(interior)
        if interior:
            fit_pts = [np.array([v.x, v.y, v.z]) for v in ringV]
            for k in body_ring_pos:
                vv = vidx_by_pos.get(k)
                if vv is None: continue
                for e in vv.link_edges:
                    o = e.other_vert(vv)
                    if pos_key(o.co) in body_ring_pos: continue
                    if pos2armw.get(pos_key(o.co), 0.0) >= 0.5: continue
                    fit_pts.append(np.array([o.co.x, o.co.y, o.co.z]))
            fit = fit_sphere(fit_pts) if len(fit_pts) >= 8 else None
            use_sphere = fit is not None and fit[1] < 0.5 and fit[2] < 0.005
            if use_sphere:
                c, r, rms = fit
                for v in interior:
                    d = Vector(v.co) - Vector(c)
                    if d.length > 1e-9:
                        v.co = Vector(c) + d.normalized() * r
                method = f"sphere(r={r:.4f}, rms={rms*1000:.2f}mm)"
            for _ in range(2):  # Laplacian 平滑（环固定）
                newpos = {}
                for v in interior:
                    n = [e.other_vert(v) for e in v.link_edges]
                    if not n: continue
                    avg = sum((o.co for o in n), Vector()) / len(n)
                    newpos[v.index] = v.co.lerp(avg, 0.5)
                for i, npv in newpos.items():
                    bm.verts[i].co = npv
                bm.verts.ensure_lookup_table()
            if use_sphere:
                for v in interior:
                    d = Vector(v.co) - Vector(c)
                    if d.length > 1e-9:
                        v.co = Vector(c) + d.normalized() * r
            bm.verts.ensure_lookup_table()
    # 残余边界收尾（臂区内的未填环）
    for _ in range(3):
        res_edges = [e for e in bm.edges if len(e.link_faces) == 1
                     and d_zone((e.verts[0].co + e.verts[1].co)/2) < 0.06]
        if not res_edges: break
        bmesh.ops.contextual_create(bm, geom=res_edges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    # 法线一致化
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.mesh.select_all(action='DESELECT')
    post_boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    post_nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bmesh.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Pass C：权重重分配
    me = mesh.data
    pos2idx = {pos_key(v.co): v.index for v in me.vertices}
    arm_ring_idx = {pos2idx[k] for k in arm_ring_pos if k in pos2idx}
    body_ring_idx = {pos2idx[k] for k in body_ring_pos if k in pos2idx}
    ARM_SIDE_VERTS.clear()
    n_boost = n_strip = 0
    for v in me.vertices:
        i = v.index
        aw_v = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
        if i in arm_ring_idx or aw_v > 0.5:
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
        z = "mouth" if (c.z > 0.17 and c.y > 0.01) else ("arm_zone!!" if d_zone(c) < 0.06 else "other")
        zones[z] = zones.get(z, 0) + 1
    bm3.free()
    p(f"SURGERY: pre_boundary={pre_boundary} cut_faces={len(cut_faces)} split_edges={n_split} "
      f"loops={loop_report} body_fill_interior={n_interior} method={method}")
    p(f"SURGERY: arm_ring={len(arm_ring_idx)} body_ring={len(body_ring_idx)} boost={n_boost} strip={n_strip} "
      f"post_boundary={post_boundary} post_nonmanifold={post_nonman}")
    p(f"SURGERY residual boundary by zone: {zones}")
    return post_boundary, post_nonman

'''
open("pose_render3.py", "w", encoding="utf-8").write(src[:start] + new + src[end:])
print("rewritten ok")
