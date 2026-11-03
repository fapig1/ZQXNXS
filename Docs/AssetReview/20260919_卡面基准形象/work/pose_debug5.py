# 卡面姿态渲染 v3 FINAL（网格手术版）
# 手术（仅渲染副本，rest 态一次）：
#  A. 删除手臂-身体焊接混合面（max armw>0.55 & min<0.45；肩头 0.014m 桥接保留）
#  B. 过渡边 split（跳口部）→ 边界环按侧分解 → 面快照差集捕获填充面 →
#     body 环 N-gon 填充+肚皮锚点UV+Skin材质+细分+geom_inner 球面投影+羽化；
#     arm 环 N-gon 填充+周圈UV拷贝+Skin材质+细分+轻度松弛 → 残余环 contextual 收尾 → 法线一致化
#  C. 权重重分配（armw>0.5 且非肩桥 → 满手臂链逆距离平方；body 侧剥手臂权重）
# 每姿态：clean_weights(保护臂侧) → 姿态 → CS0.85 → 几何QC → 渲染
# 用法: blender --background pet01_work.blend --python pose_render3.py [-- final] [-- debugfill]
import bpy, sys, os, math
from mathutils import Matrix, Vector
import numpy as np

WORK = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work"
TEXDIR = os.path.join(WORK, "tex_variant")
FINAL = "--" in sys.argv and "final" in sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else False
DEBUGFILL = "--" in sys.argv and "debugfill" in sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else False
LOG = open(os.path.join(WORK, "pose_render3_log.txt"), "a", encoding="utf-8")
def p(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.write(s+"\n"); LOG.flush()

rig = bpy.data.objects["Pet01Rig"]
mesh = bpy.data.objects["Pet01_Mesh"]
mesh.hide_render = False
for ob in bpy.data.objects:
    if ob.name in ("Meshy_Proportion_Reference", "PreviewFloor", "Pet01_PreviewCamera"):
        ob.hide_render = True
    if ob.type == 'LIGHT':
        ob.hide_render = True
if rig.animation_data:
    rig.animation_data_clear()
rig.location = (0, 0, 0)

ARM_BONES = ["UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"]
ARMSIDE = {"L": ["UpperArm.L","Forearm.L","Hand.L"], "R": ["UpperArm.R","Forearm.R","Hand.R"]}
arm_data = rig.data
SEGS = {b: (Vector(arm_data.bones[b].head_local), Vector(arm_data.bones[b].tail_local)) for b in ARM_BONES}
SHOULDER = [Vector(arm_data.bones["UpperArm.L"].head_local), Vector(arm_data.bones["UpperArm.R"].head_local)]
ARMSET = set(ARM_BONES)

def dist_seg(p, a, b):
    ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
    t = max(0.0, min(1.0, t))
    return (p - (a + ab*t)).length

def d_zone(p):
    return min(dist_seg(p, *SEGS[n]) for n in ("Forearm.L","Forearm.R","Hand.L","Hand.R"))
def d_shoulder(p):
    return min((p - s).length for s in SHOULDER)
def pos_key(co):
    return (round(co.x, 6), round(co.y, 6), round(co.z, 6))
def is_mouth_co(co):
    return co.z > 0.17 and co.y > 0.01

# ---------------- 网格手术 ----------------
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

def surgery():
    import bmesh
    me = mesh.data
    name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
    N = len(me.vertices)
    armw = np.zeros(N)
    for v in me.vertices:
        armw[v.index] = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
    pos2armw = {pos_key(v.co): float(armw[v.index]) for v in me.vertices}
    cut_faces = set()
    for i, poly in enumerate(me.polygons):
        c = poly.center
        in_zone = (d_zone(c) < 0.030) or \
                  (min(dist_seg(c, *SEGS[n]) for n in ("UpperArm.L","UpperArm.R")) < 0.030)
        if not in_zone: continue
        ws = [armw[me.vertices[me.loops[li].vertex_index].index] for li in range(poly.loop_start, poly.loop_start+poly.loop_total)]
        if max(ws) >= 0.5 and min(ws) < 0.5:
            cut_faces.add(i)
    # Pass B
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

    # 过渡边拆分
    n_split = 0
    for _ in range(4):
        tedges = [e for e in bm.edges if len(e.link_faces) == 1
                  and not is_mouth_co((e.verts[0].co+e.verts[1].co)/2)
                  and vclass(e.verts[0]) != vclass(e.verts[1])]
        if not tedges: break
        bmesh.ops.split_edges(bm, edges=tedges)
        n_split += len(tedges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()

    # 填充前快照
    snapshot = set()
    for f in bm.faces:
        snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
    # 材质槽
    skin_idx = 0
    for si, slot in enumerate(mesh.material_slots):
        if slot.material and slot.material.name == 'Pet01_Skin':
            skin_idx = si; break
    # 填充面材质：皮肤克隆 + 纹理自发光，使掠射角/阴影中的补丁面保持可读肤色
    fill_mat = bpy.data.materials.get('Pet01_SkinFill')
    if fill_mat is None:
        fill_mat = bpy.data.materials['Pet01_Skin'].copy()
        fill_mat.name = 'Pet01_SkinFill'
        fnt = fill_mat.node_tree
        ftex = next(n for n in fnt.nodes if n.type == 'TEX_IMAGE')
        fbsdf = next(n for n in fnt.nodes if n.type == 'BSDF_PRINCIPLED')
        fnt.links.new(ftex.outputs['Color'], fbsdf.inputs['Emission Color'])
        fbsdf.inputs['Emission Strength'].default_value = 0.22
        me.materials.append(fill_mat)
    fill_idx = len(me.materials) - 1
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
    # 手臂侧 UV 源（armw>=0.8 的顶点），供残余膜瓣逐顶点取样
    apos, auv = [], []
    if uvlay is not None:
        for v in bm.verts:
            try:
                if not v.is_valid: continue
            except ReferenceError: continue
            if pos2armw.get(pos_key(v.co), 0.0) < 0.8: continue
            try:
                uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            except ReferenceError: continue
            if not uvs: continue
            apos.append([v.co.x, v.co.y, v.co.z])
            auv.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
    Ap = np.array(apos) if apos else np.zeros((0,3))
    Au = np.array(auv) if auv else np.zeros((0,2))
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

    def nearest_arm_uv(co3):
        if len(Ap) == 0: return arm_anchor
        cow = np.array([co3[0], co3[1], co3[2]*WZ])
        best = None
        for s0 in range(0, len(Ap), 4096):
            Aw = Ap.copy(); Aw[:,2] *= WZ
            d2 = ((Aw[s0:s0+4096] - cow)**2).sum(-1)
            j = int(np.argmin(d2))
            if best is None or d2[j] < best[0]:
                best = (d2[j], Au[s0+j])
        return tuple(best[1])

    # 体内钳制：填充面内点若贴近 body 表面（<12mm），压入最近原始 body 顶点
    # 的局部切平面（体心径向近似外法线）内侧 0.5mm——杜绝膜瓣在肩部等
    # 曲面内凹区穿出体表形成发丝线/肤色斑
    C_b = Bp.mean(axis=0) if len(Bp) else None
    Np = (Bp - C_b) if len(Bp) else None
    if len(Bp):
        Nn = np.maximum(np.linalg.norm(Np, axis=1), 1e-9)[:, None]
        Np = Np / Nn
    def clamp_inside_body(innerP):
        if C_b is None or len(innerP) == 0: return innerP
        P = np.array(innerP)
        for t in range(len(P)):
            d2 = ((Bp - P[t])**2).sum(-1)
            j = int(np.argmin(d2))
            if d2[j] > 0.012**2: continue
            s = float(np.dot(P[t] - Bp[j], Np[j]))
            if s > -0.0005:
                P[t] = P[t] - (s + 0.0005) * Np[j]
        return P

    loop_report = []
    SKIP_EDGES = set()
    BIG_RINGS = []
    web_faces = []
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
        if cls == "arm" and len(vs) > 60:
            # 大臂开口不填充：刚性膜扇在摆臂时必然扫出体表（发丝线/杆状物/背鳍根因）。
            # 开口贴体或朝后，被身体遮挡；缝隙处露出的是 body 侧补丁，暗部读作腋下阴影。
            for e in loop:
                SKIP_EDGES.add(frozenset((pos_key(e.verts[0].co), pos_key(e.verts[1].co))))
            BIG_RINGS.append([pos_key(v.co) for v in vs])
            loop_report.append(("arm_skip_big", len(vs)))
            continue
        bmesh.ops.holes_fill(bm, sides=0, edges=list(loop))
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        # 捕获本环新填充面
        new_faces = [f for f in bm.faces
                     if frozenset(pos_key(v.co) for v in f.verts) not in snapshot]
        if not new_faces:
            # holes_fill 拒填（回路含分支顶点/非单一闭环）→ contextual_create 兜底
            bmesh.ops.contextual_create(bm, geom=list(loop))
            bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
            new_faces = [f for f in bm.faces
                         if frozenset(pos_key(v.co) for v in f.verts) not in snapshot]
        for f in new_faces:
            snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
            try:
                f.material_index = fill_idx
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
                            loop[uvlay].uv = nearest_arm_uv(loop.vert.co)
                    except ReferenceError: pass
        # 细分 → geom_inner 立即转 numpy 位置
        edges = set()
        for f in new_faces:
            try:
                for e in f.edges: edges.add(e)
            except ReferenceError: pass
        cuts = 3 if cls == "body" else (3 if len(vs) > 60 else 2)
        ret = bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=cuts, use_grid_fill=False)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        from bmesh.types import BMVert as _BMVert
        inner_idx, innerP = [], []
        for v in ret.get('geom_inner', []):
            try:
                if not isinstance(v, _BMVert): continue
                if not v.is_valid: continue
                inner_idx.append(v.index)
                innerP.append([v.co.x, v.co.y, v.co.z])
            except ReferenceError: continue
        innerP = np.array(innerP) if innerP else np.zeros((0,3))
        if cls == "body":
            n_binner += len(inner_idx)
            method = "none"
            c = None; r = None
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
                    if len(innerP) < 2 or len(innerP) > 4000: break
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
                    # 防穿出：内点径向距离钳制到最近原始 body 顶点径向水平 - 0.5mm
                    if len(Bp):
                        Bw2 = Bp.copy(); Bw2[:,2] *= WZ
                        Br = np.linalg.norm(Bp - c, axis=1)
                        for t in range(len(innerP)):
                            cow = np.array([innerP[t][0], innerP[t][1], innerP[t][2]*WZ])
                            d2 = ((Bw2 - cow)**2).sum(-1)
                            j = int(np.argmin(d2))
                            rmax = float(Br[j]) - 0.0005
                            v3 = innerP[t] - c
                            vr = float(np.linalg.norm(v3))
                            if vr > rmax and vr > 1e-9:
                                innerP[t] = c + v3/vr * rmax
                    innerP = clamp_inside_body(innerP)
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
            body_methods.append(method)
        else:
            n_ainner += len(inner_idx)
            if len(innerP):
                for _ in range(2):
                    if len(innerP) < 2 or len(innerP) > 4000: break
                    D = ((innerP[:, None, :] - innerP[None, :, :])**2).sum(-1)
                    np.fill_diagonal(D, 1e9)
                    mask = D < (0.003**2)
                    cnt = mask.sum(1)[:, None]
                    avg = (innerP[None, :, :] * mask[:, :, None]).sum(1) / np.maximum(cnt, 1)
                    upd = cnt[:, 0] > 0
                    innerP[upd] = innerP[upd]*0.6 + avg[upd]*0.4
                # 碗状内缩：向最近臂骨方向，深度按环距加权（环边齐平密封、内部渐深2.5mm，
                # 让膜-体相交线贴缝成自然折痕，消除掠射发丝线与共面z-fighting）
                for t, i in enumerate(inner_idx):
                    d_ring = float(np.min(np.linalg.norm(ringA - innerP[t], axis=1)))
                    wgt = min(d_ring / 0.008, 1.0)
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
                        innerP[t] = innerP[t] + dire * (0.0025 * wgt)
                innerP = clamp_inside_body(innerP)
                for t, i in enumerate(inner_idx):
                    try:
                        bm.verts[i].co = Vector(innerP[t])
                    except ReferenceError: pass
            loop_report.append(("arm_fill", len(inner_idx)))
        # 子面落快照（内部顶点已移动到终位）：防止后续环把先前细分子面重复捕获/重复赋UV/重复细分
        bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        for f in bm.faces:
            try:
                k = frozenset(pos_key(v.co) for v in f.verts)
            except ReferenceError:
                continue
            if k not in snapshot:
                snapshot.add(k)
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    p(f"SURGERY body_fill_inner={n_binner} arm_fill_inner={n_ainner} methods={body_methods}")

    # 肩部悬挂网带：大开口肩段臂环逐顶点向体内复制 4mm 新顶点（绑定 Spine/Chest，
    # 即随身体不动），与臂环生成面向外的四边面带。外缘随臂、内缘锚定身体：
    # 静息藏于体内，姿态下自动张紧封住臂环-体表之间的楔形看穿缝，不会扫出体表。
    dl = bm.verts.layers.deform.verify()
    vg_idx = {}
    for vg in mesh.vertex_groups:
        vg_idx[vg.name] = vg.index
    n_web = 0
    vmap = {}
    for v in bm.verts:
        try:
            vmap[pos_key(v.co)] = v
        except ReferenceError:
            continue
    bm.verts.ensure_lookup_table()
    for ring in BIG_RINGS:
        seq_all = [vmap[k] for k in ring if k in vmap]
        for sh in SHOULDER:
            # 有序环上截取距肩头 <28mm 的连续弧段
            seq = []
            for v in seq_all:
                if (v.co - sh).length < 0.028:
                    seq.append(v)
                else:
                    if len(seq) >= 4: break
                    seq = []
            if len(seq) < 4:
                # 环可能跨过弧段边界，做一次环形回绕再试
                rot = None
                for i in range(len(seq_all)):
                    if (seq_all[i].co - sh).length < 0.028:
                        rot = i; break
                if rot is None: continue
                ordered = seq_all[rot:] + seq_all[:rot]
                seq = []
                for v in ordered:
                    if (v.co - sh).length < 0.028:
                        seq.append(v)
                    else:
                        break
            if len(seq) < 4: continue
            dup = {}
            dup_idx = set()
            for v in seq:
                # 内移方向：指向最近臂骨（保证网带藏在本侧手臂体积内，
                # 不会沿体心径向穿透颈肩褶的另一壁）
                side = "L" if v.co.x >= 0 else "R"
                bestp = None
                for n in ARMSIDE[side]:
                    a, b = SEGS[n]
                    abv = b - a
                    tt = (v.co - a).dot(abv) / max(abv.dot(abv), 1e-12)
                    tt = min(max(tt, 0.0), 1.0)
                    pt = a + abv*tt
                    d = (v.co - pt).length
                    if bestp is None or d < bestp[0]:
                        bestp = (d, pt)
                off = min(0.006, max(bestp[0]*0.7, 0.0015)) if bestp else 0.006
                if bestp:
                    dire = (bestp[1] - v.co) / max(bestp[0], 1e-9)
                    nv = bm.verts.new(v.co + dire * off)
                else:
                    nrm = Vector(v.co) - Vector(C_b)
                    nrm.normalize()
                    nv = bm.verts.new(v.co - nrm * 0.006)
                body_bone = "Spine" if v.co.z < 0.13 else "Chest"
                nv[dl][vg_idx[body_bone]] = 1.0
                dup[v.index] = nv
                dup_idx.add(nv.index)
            bm.verts.ensure_lookup_table()
            for a, b in zip(seq[:-1], seq[1:]):
                try:
                    f = bm.faces.new((a, b, dup[b.index], dup[a.index]))
                    f.material_index = fill_idx
                    f.smooth = True
                    if uvlay is not None:
                        for lp in f.loops:
                            if lp.vert.index in dup_idx:
                                lp[uvlay].uv = nearest_body_uv(lp.vert.co)
                            else:
                                lp[uvlay].uv = nearest_arm_uv(lp.vert.co)
                    web_faces.append(f)
                    n_web += 1
                except ValueError:
                    pass
            bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    p(f"shoulder web faces={n_web}")

    # 残余边界收尾（捕获新面并同样处理：材质/平滑/逐顶点UV，避免无UV膜瓣）
    for _ in range(3):
        res_edges = [e for e in bm.edges if len(e.link_faces) == 1
                     and not is_mouth_co((e.verts[0].co+e.verts[1].co)/2)
                     and d_zone((e.verts[0].co+e.verts[1].co)/2) < 0.06
                     and frozenset((pos_key(e.verts[0].co), pos_key(e.verts[1].co))) not in SKIP_EDGES]
        if not res_edges: break
        bmesh.ops.contextual_create(bm, geom=res_edges)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
        res_new = [f for f in bm.faces
                   if frozenset(pos_key(v.co) for v in f.verts) not in snapshot]
        for f in res_new:
            snapshot.add(frozenset(pos_key(v.co) for v in f.verts))
            try:
                f.material_index = fill_idx
                f.smooth = True
            except ReferenceError: pass
            if uvlay is not None:
                try:
                    for lp in f.loops:
                        if pos2armw.get(pos_key(lp.vert.co), 0.0) >= 0.5:
                            lp[uvlay].uv = nearest_arm_uv(lp.vert.co)
                        else:
                            lp[uvlay].uv = nearest_body_uv(lp.vert.co)
                except ReferenceError: pass
        p(f"residual fill round {_}: new_faces={len(res_new)}")
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5)
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.mesh.select_all(action='DESELECT')
    # （试验：翻转 web 法线会使受光面变背光面、裂缝变成黑洞，已回退。
    #  原绕向在裂缝中本为受光面；深褶中的暗斑是 web 背光的自然阴影。）
    post_boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    post_nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bmesh.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode='OBJECT')

    # Pass C：权重
    me = mesh.data
    ARM_SIDE_VERTS.clear()
    n_boost = n_strip = 0
    for v in me.vertices:
        i = v.index
        aw_v = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
        if aw_v > 0.5:
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
        elif aw_v < 0.5:
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
        if frozenset((pos_key(e.verts[0].co), pos_key(e.verts[1].co))) in SKIP_EDGES: continue
        z = "mouth" if is_mouth_co(c) else ("arm_opening_kept" if d_zone(c) < 0.06 else "other")
        zones[z] = zones.get(z, 0) + 1
    bm3.free()
    p(f"SURGERY: pre_boundary={pre_boundary} cut_faces={len(cut_faces)} split={n_split} loops={loop_report}")
    p(f"SURGERY: body_inner={n_binner}({'+'.join(body_methods) if body_methods else 'n/a'}) "
      f"arm_inner={n_ainner} boost={n_boost} strip={n_strip} "
      f"post_boundary={post_boundary} post_nonmanifold={post_nonman}")
    p(f"SURGERY residual boundary by zone: {zones}")
    return post_boundary, post_nonman

# ---------------- 权重清理（保护臂侧） ----------------
def clean_weights(tex_img):
    me = mesh.data
    vg_all = mesh.vertex_groups
    name_by_idx = {vg.index: vg.name for vg in vg_all}
    W, H = tex_img.size
    buf = np.empty(W*H*4, dtype=np.float32)
    tex_img.pixels.foreach_get(buf)
    px = buf.reshape(H, W, 4)
    uvl = me.uv_layers[0].data
    vert_uv = np.zeros((len(me.vertices), 2))
    cnt = np.zeros(len(me.vertices), dtype=np.int32)
    for li, l in enumerate(me.loops):
        u, v = uvl[li].uv
        vert_uv[l.vertex_index, 0] += u; vert_uv[l.vertex_index, 1] += v
        cnt[l.vertex_index] += 1
    cnt[cnt == 0] = 1
    vert_uv /= cnt[:, None]
    n_belly = 0
    for v in me.vertices:
        if v.index in ARM_SIDE_VERTS: continue
        arm_w = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
        if arm_w < 0.02: continue
        u = min(max(vert_uv[v.index,0],0),0.9999); vv = min(max(vert_uv[v.index,1],0),0.9999)
        r, g, b = px[int(vv*H), int(u*W), :3]
        mxv = max(r,g,b); mnv = min(r,g,b)
        if (mxv-mnv)/max(mxv,1e-6) < 0.38 and mxv > 0.60:
            gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
            for n in ARM_BONES:
                if n in gw: vg_all[n].add([v.index], 0.0, 'REPLACE')
            gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
            ssum = sum(gw.values())
            if ssum > 0.05:
                for n, w in gw.items(): vg_all[n].add([v.index], w/ssum, 'REPLACE')
            else:
                vg_all["Spine" if v.co.z < 0.13 else "Chest"].add([v.index], 1.0, 'ADD')
            n_belly += 1
    p(f"clean_weights: belly_stripped={n_belly}")

# ---------------- 姿态 ----------------
def reset_pose():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.rotation_mode = 'QUATERNION'
    rig.location = (0, 0, 0)

def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def aim(pb, target):
    t = Vector(target).normalized()
    m = pb.matrix.copy()
    d = (m.to_3x3() @ Vector((0,1,0)))
    if d.length < 1e-9: return
    d.normalize()
    R = d.rotation_difference(t).to_matrix().to_4x4()
    head = m.translation
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def mir(v): return (-v[0], v[1], v[2])

HANG_U = (0.45, 0.08, -0.89); HANG_F = (0.22, 0.03, -0.975)
BEHIND_U = (0.42, -0.42, -0.80); BEHIND_F = (0.12, -0.88, -0.46); BEHIND_H = (0.05, -0.95, -0.31)

POSES = {
  "calm": [
    ("rot","Root",'Z',35),("rot","Pelvis",'X',6),("rot","Spine",'X',3),
    ("rot","Head",'Z',10),("rot","Head",'X',-4),
    ("aim","UpperArm.L",HANG_U),("aim","Forearm.L",HANG_F),("aim","Hand.L",HANG_F),
    ("aim","UpperArm.R",mir(HANG_U)),("aim","Forearm.R",mir(HANG_F)),("aim","Hand.R",mir(HANG_F)),
  ],
  "rage": [
    ("rot","Root",'Z',-6),("rot","Pelvis",'X',4),
    ("rot","UpperArm.L",'Y',-32),("rot","UpperArm.L",'X',6),
    ("rot","Head",'Z',-14),("rot","Head",'X',2),
  ],
  "divine": [
    ("rot","Spine",'X',2),("rot","Head",'X',8),
    ("aim","UpperArm.L",BEHIND_U),("aim","Forearm.L",BEHIND_F),("aim","Hand.L",BEHIND_H),
    ("aim","UpperArm.R",mir(BEHIND_U)),("aim","Forearm.R",mir(BEHIND_F)),("aim","Hand.R",mir(BEHIND_H)),
  ],
  "classic": [
    ("rot","Root",'Z',-32),("rot","Spine",'X',2),("rot","Head",'X',8),
  ],
  "classic_bh": [
    ("rot","Root",'Z',-32),("rot","Spine",'X',2),("rot","Head",'X',8),
    ("aim","UpperArm.L",BEHIND_U),("aim","Forearm.L",BEHIND_F),("aim","Hand.L",BEHIND_H),
    ("aim","UpperArm.R",mir(BEHIND_U)),("aim","Forearm.R",mir(BEHIND_F)),("aim","Hand.R",mir(BEHIND_H)),
  ],
}

CFG = {
  "calm":    dict(tex="Pet01_BaseColor_calm.png",    emissive=False),
  "rage":    dict(tex="Pet01_BaseColor_rage.png",    emissive=False),
  "divine":  dict(tex="Pet01_BaseColor_divine.png",  emissive=True),
  "classic": dict(tex="Pet01_BaseColor_classic.png", emissive=False),
  "classic_bh": dict(tex="Pet01_BaseColor_classic.png", emissive=False),
}

# ---------------- 场景 ----------------
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 96 if FINAL else 24
try: scene.eevee.use_denoising = True
except Exception: pass
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.resolution_percentage = 100

def add_light(name, loc, energy, size, color=(1,1,1)):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = energy; d.size = size; d.color = color
    o = bpy.data.objects.new(name, d); scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector((0,0,0.10)) - Vector(loc)).to_track_quat('-Z','Y').to_euler()
add_light("Key",  (-0.38, -0.42, 0.42), 26, 0.5, (1.0, 0.98, 0.94))
add_light("Fill", ( 0.45, -0.30, 0.22), 10, 0.7, (0.95, 0.97, 1.0))
add_light("Rim",  ( 0.05,  0.52, 0.40), 45, 0.4, (1.0, 1.0, 1.0))
cam_d = bpy.data.cameras.new("CardCam"); cam_d.lens = 85.0
cam = bpy.data.objects.new("CardCam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
world = bpy.data.worlds[0] if bpy.data.worlds else bpy.data.worlds.new("World")
scene.world = world; world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.12,0.12,0.13,1.0); bg.inputs[1].default_value = 0.15

# ---------------- 手术 + CS ----------------
surgery()
if "CS" not in mesh.modifiers:
    cs = mesh.modifiers.new("CS", 'CORRECTIVE_SMOOTH')
    cs.factor = 0.85; cs.smooth_type = 'SIMPLE'

EDGES = np.array([[e.vertices[0], e.vertices[1]] for e in mesh.data.edges], dtype=np.int64)
def eval_positions():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    nv = len(ev.data.vertices)
    vs = np.empty(nv*3, dtype=np.float64)
    ev.data.vertices.foreach_get("co", vs); vs = vs.reshape(-1,3)
    m44 = np.array(mesh.matrix_world)
    return (m44 @ np.hstack([vs, np.ones((nv,1))]).T).T[:,:3]

REST_VS = eval_positions()
REST_LEN = np.linalg.norm(REST_VS[EDGES[:,0]] - REST_VS[EDGES[:,1]], axis=1)

# 手术后的 rest 态网格（含 CS 修饰器）另存一份，供哈希记录与追溯；不影响后续流程
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "pet01_surgery.blend"), compress=False)
p("saved post-surgery mesh: work/pet01_surgery.blend")

# ---------------- 几何 QC ----------------
def geom_qc(tag):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(mesh.data)
    n_bnd = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    n_nm  = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()
    vs = eval_positions()
    a = vs[EDGES[:,0]]; b = vs[EDGES[:,1]]
    L = np.linalg.norm(a-b, axis=1)
    stretch = L / np.maximum(REST_LEN, 1e-9)
    over = stretch > 1.30
    n_over = int(over.sum()); n_over_arm = 0
    over_c = (a[over]+b[over])/2
    for c in over_c:
        if d_zone(Vector(c)) < 0.05 or d_shoulder(Vector(c)) < 0.03:
            n_over_arm += 1
    top = np.argsort(-stretch)[:6]
    topinfo = [(round(float(stretch[i]),2), tuple(round(float(x),3) for x in (a[i]+b[i])/2)) for i in top]
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    nf = len(ev.data.polygons)
    fn = np.empty(nf*3, dtype=np.float64); ev.data.polygons.foreach_get("normal", fn); fn = fn.reshape(-1,3)
    cent = np.empty(nf*3, dtype=np.float64); ev.data.polygons.foreach_get("center", cent); cent = cent.reshape(-1,3)
    area = np.empty(nf, dtype=np.float64); ev.data.polygons.foreach_get("area", area)
    n_degen = int((area < 1e-8).sum())
    rng = np.random.default_rng(7)
    sample = rng.choice(nf, size=min(1500, nf), replace=False)
    C = cent[sample]
    n_flip = 0
    for s0 in range(0, len(C), 128):
        chunk = C[s0:s0+128]
        d2 = ((cent[None,:,:] - chunk[:,None,:])**2).sum(-1)
        for r in range(len(chunk)):
            fi = sample[s0+r]
            if area[fi] < 1e-8: continue
            nn = np.argpartition(d2[r], 7)[:7]
            nn = nn[nn != fi]
            if len(nn) == 0: continue
            m = fn[nn].mean(axis=0)
            m = m / max(np.linalg.norm(m), 1e-9)
            if float(np.dot(fn[fi], m)) < -0.5:
                n_flip += 1
    p(f"QC[{tag}] boundary={n_bnd} nonmanifold={n_nm} stretch_gt30={n_over} (arm/shoulder_zone={n_over_arm}) "
      f"flip_est={n_flip}/{len(sample)} degen={n_degen}")
    p(f"QC[{tag}] top_stretch={topinfo}")

# ---------------- 材质 ----------------
def apply_skin(cfg):
    tex_path = os.path.join(TEXDIR, cfg["tex"])
    img = bpy.data.images.get("card_skin")
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.load(tex_path, check_existing=False)
    img.name = "card_skin"
    img.colorspace_settings.name = 'sRGB'
    for mname in ("Pet01_Skin", "Pet01_Oral", "Pet01_Eyes"):
        m = bpy.data.materials[mname]
        nt = m.node_tree
        tex_node = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None)
        if tex_node is None:
            tex_node = nt.nodes.new('ShaderNodeTexImage')
        tex_node.image = img
        tex_node.interpolation = 'Cubic'
        bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
        nt.links.new(tex_node.outputs['Color'], bsdf.inputs['Base Color'])
        if cfg["emissive"] and mname == "Pet01_Eyes":
            for inp in bsdf.inputs['Base Color'].links[:]:
                nt.links.remove(inp)
            bsdf.inputs['Base Color'].default_value = (1.0, 1.0, 1.0, 1.0)
            bsdf.inputs['Emission Color'].default_value = (1.0, 1.0, 1.0, 1.0)
            bsdf.inputs['Emission Strength'].default_value = 6.0
        else:
            bsdf.inputs['Emission Strength'].default_value = 0.0
    fm = bpy.data.materials.get('Pet01_SkinFill')
    if fm:
        fnt = fm.node_tree
        ftex = next(n for n in fnt.nodes if n.type == 'TEX_IMAGE')
        ftex.image = img
    return img

def mesh_bbox():
    vs = eval_positions()
    return vs.min(0), vs.max(0)

def pose_and_render(name, spec, cfg, res, out_path):
    reset_pose()
    img = apply_skin(cfg)
    clean_weights(img)
    for op in spec:
        if op[0] == "rot":
            _, bonename, axis, deg = op
            rot(rig.pose.bones[bonename], axis, deg)
        else:
            _, bonename, target = op
            aim(rig.pose.bones[bonename], target)
        bpy.context.view_layer.update()
    geom_qc(name)
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    rig.location.z = -mn[2]
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    cx = (mn[0]+mx[0])/2; cz = (mn[2]+mx[2])/2
    height = mx[2]-mn[2]; width = mx[0]-mn[0]
    vw = res[0]/res[1]
    need_h = height*1.30; need_w = width*1.38
    D_h = (need_h/2)/math.tan(math.atan(18.0/cam_d.lens))
    D_w = (need_w/2)/math.tan(math.atan(18.0*vw/cam_d.lens))
    D = max(D_h, D_w, 0.45)
    cam.location = (cx, D, cz)
    cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)
    p(f"rendered {name} res={res} bbox_h={height:.3f} bbox_w={width:.3f} aspect={width/height:.3f} D={D:.3f}")

geom_qc("REST")

if DEBUGFILL:
    me = mesh.data
    def _flat(name, rgb):
        m = bpy.data.materials.new(name); m.use_nodes = True
        bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bs.inputs["Base Color"].default_value = rgb
        return m
    gmat = _flat("dbg_green", (0, 1, 0, 1))
    mmat = _flat("dbg_magenta", (1, 0, 1, 1))
    me.materials.append(gmat); me.materials.append(mmat)
    gi = len(me.materials) - 2; mi = len(me.materials) - 1
    name_by_idx = {vg.index: vg.name for vg in mesh.vertex_groups}
    vw = {}
    for v in me.vertices:
        vw[v.index] = sum(ge.weight for ge in v.groups if name_by_idx.get(ge.group) in ARMSET)
    import bmesh as _bmm
    b2 = _bmm.new(); b2.from_mesh(me)
    b2.faces.ensure_lookup_table()
    aw2 = [0.0]*len(b2.verts)
    for v in me.vertices: aw2[v.index] = vw[v.index]
    n_g = 0
    for f in b2.faces:
        ws = [aw2[v.index] for v in f.verts]
        if max(ws) < 0.2:
            me.polygons[f.index].material_index = gi; n_g += 1
    b2.free()
    p(f"DEBUG: green(body-lowweight) faces={n_g}")
    reset_pose()
    apply_skin(CFG["rage"])
    for op in POSES["rage"]:
        if op[0] == "rot": rot(rig.pose.bones[op[1]], op[2], op[3])
        else: aim(rig.pose.bones[op[1]], op[2])
        bpy.context.view_layer.update()
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    rig.location.z = -mn[2]
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    cam.location = ((mn[0]+mx[0])/2, 0.62, (mn[2]+mx[2])/2)
    cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    scene.render.resolution_x, scene.render.resolution_y = 550, 825
    scene.render.filepath = os.path.join(WORK, "debug_fill_rage.png")
    bpy.ops.render.render(write_still=True)
    p("debug render done")
    LOG.close()
    sys.exit(0)

pose_and_render("classic", POSES["classic"], CFG["classic"], (1100, 1650), os.path.join(WORK, "dbg5_classic.png"))
LOG.close()
