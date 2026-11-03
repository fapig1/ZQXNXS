# 卡面姿态渲染 v2：
#  - 权重清理 v2：按 UV 采样贴图颜色，把"肚皮色(低饱和高明度)"顶点上的手臂骨骼权重剥掉，
#    棕色手指/绿色身体不受影响；另做距离>0.02m 的杂散权重清理。
#  - 姿态 v2：按 findings_agentA 的基准描述重新设计（calm 单臂下垂侧身 / rage 单臂平抬外伸 /
#    divine 正面对称垂臂 / classic 背手挺立）。
#  - 渲染透明底角色图，脚底贴 z=0，完整含双脚。
# 用法: blender --background pet01_work.blend --python pose_render2.py [-- final]
import bpy, sys, os, math
from mathutils import Matrix, Vector
import numpy as np

WORK = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work"
TEXDIR = os.path.join(WORK, "tex_variant")
FINAL = "--" in sys.argv and "final" in sys.argv[sys.argv.index("--")+1:]
LOGPATH = os.path.join(WORK, "pose_render2_log.txt")
LOG = open(LOGPATH, "a", encoding="utf-8")
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

def reset_pose():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.rotation_mode = 'QUATERNION'
    rig.location = (0, 0, 0)

# ---------- 权重清理 v2 ----------
ARM_BONES = ["UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"]
BODY_FALLBACK = ("Spine", "Chest")

def _dist_point_seg(p, a, b):
    ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
    t = max(0.0, min(1.0, t))
    return (p - (a + ab * t)).length

def strip_arm_weight(v, vg_all, name_by_idx, reason):
    gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
    changed = False
    for n in list(gw):
        if n in ARM_BONES:
            vg_all[n].add([v.index], 0.0, 'REPLACE'); changed = True
    if not changed: return False
    gw2 = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
    s = sum(gw2.values())
    if s > 0.05:
        for n, w in gw2.items():
            vg_all[n].add([v.index], w / s, 'REPLACE')
    else:
        fb = BODY_FALLBACK[0] if v.co.z < 0.13 else BODY_FALLBACK[1]
        vg_all[fb].add([v.index], 1.0, 'ADD')
    return True

def clean_weights(tex_img):
    """肚皮色顶点(UV采样)剥手臂权重 + 距离清理"""
    me = mesh.data
    vg_all = mesh.vertex_groups
    name_by_idx = {vg.index: vg.name for vg in vg_all}
    arm = rig.data
    segs = {b: (Vector(arm.bones[b].head_local), Vector(arm.bones[b].tail_local)) for b in ARM_BONES}
    # 贴图 -> numpy (行自底向上)
    W, H = tex_img.size
    buf = np.empty(W*H*4, dtype=np.float32)
    tex_img.pixels.foreach_get(buf)
    px = buf.reshape(H, W, 4)  # Blender 像素自底向上，与 UV v=0 在底部一致，不要翻转
    # 顶点 -> 平均UV
    uv_layers = me.uv_layers
    vert_uv = np.zeros((len(me.vertices), 2), dtype=np.float64)
    have_uv = len(uv_layers) > 0
    if have_uv:
        uvd = uv_layers[0].data
        cnt = np.zeros(len(me.vertices), dtype=np.int32)
        for li, l in enumerate(me.loops):
            u, v = uvd[li].uv
            vert_uv[l.vertex_index, 0] += u; vert_uv[l.vertex_index, 1] += v
            cnt[l.vertex_index] += 1
        cnt[cnt == 0] = 1
        vert_uv /= cnt[:, None]
    n_belly, n_far = 0, 0
    for v in me.vertices:
        gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups if ge.group in name_by_idx}
        arm_w = sum(w for n, w in gw.items() if n in ARM_BONES and w > 1e-4)
        if arm_w < 0.02: continue
        hit = False
        if have_uv:
            u = min(max(vert_uv[v.index, 0], 0.0), 0.9999)
            vv = min(max(vert_uv[v.index, 1], 0.0), 0.9999)
            r, g, b = px[int(vv*H), int(u*W), :3]
            mxv = max(r, g, b); mnv = min(r, g, b)
            sat = (mxv - mnv) / max(mxv, 1e-6)
            if sat < 0.38 and mxv > 0.60:   # 肚皮色（含四款变体的浅色肚皮）
                hit = True
        if hit:
            if strip_arm_weight(v, vg_all, name_by_idx, "belly"): n_belly += 1
        else:
            far = min(_dist_point_seg(v.co, *segs[n]) for n in ARM_BONES)
            if far > 0.02:
                if strip_arm_weight(v, vg_all, name_by_idx, "far"): n_far += 1
    p(f"clean_weights: belly_stripped={n_belly} far_stripped={n_far}")
    # 注：不再做手部外科——手指与前臂内缘和躯干为同一连续曲面（无底层表面），
    # 任何切断/删除都会开洞。姿态设计改为让手沿体表切向滑动。

def cut_hand_bridges(me, name_by_idx):
    """切断"手指-肚皮"焊接桥接面：删除同时含 Hand 权重顶点与无手臂权重顶点的面。
    两个表面各自保留；手指随手离开，肚皮表面保持完整。只改渲染副本。"""
    import bmesh
    N = len(me.vertices)
    hw = np.zeros(N); aw = np.zeros(N)
    for v in me.vertices:
        for ge in v.groups:
            n = name_by_idx.get(ge.group)
            if not n: continue
            if n.startswith("Hand."): hw[v.index] += ge.weight
            if n in ARM_BONES: aw[v.index] += ge.weight
    arm = rig.data
    hseg = ((Vector(arm.bones["Hand.L"].head_local), Vector(arm.bones["Hand.L"].tail_local)),
            (Vector(arm.bones["Hand.R"].head_local), Vector(arm.bones["Hand.R"].tail_local)))
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    def _near_hand(co):
        return min(_dist_point_seg(co, a, b) for a, b in hseg) < 0.035
    n_cut = 0
    for f in bm.faces:
        hmax = max(hw[v.index] for v in f.verts)
        amin = min(aw[v.index] for v in f.verts)
        mid = f.calc_center_median()
        if hmax > 0.25 and amin < 0.10 and _near_hand(mid):
            f.select = True; n_cut += 1
        else:
            f.select = False
    bmesh.update_edit_mesh(me)
    if n_cut:
        bpy.ops.mesh.delete(type='FACE')
    bpy.ops.object.mode_set(mode='OBJECT')
    p(f"cut_hand_bridges: cut {n_cut} bridge faces")

def remove_fingers_and_fill(me, vg_all, name_by_idx):
    """手部外科：删除所有 Hand.* 权重>=0.02 的顶点（手指+掌部融合环），
    再对生成的开放边界 fill 补洞（腕端盖继承前臂权重，肚皮区继承身体权重）。
    原因：手指几何与肚皮表面在源资产里是焊接的，任何大幅摆臂都会撕扯桥接面。
    只改渲染副本。"""
    import bmesh
    doomed = set()
    for v in me.vertices:
        for ge in v.groups:
            if name_by_idx.get(ge.group, "").startswith("Hand.") and ge.weight >= 0.02:
                doomed.add(v.index); break
    p(f"remove_fingers: deleting {len(doomed)} verts")
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    for v in bm.verts:
        v.select = v.index in doomed
    bmesh.update_edit_mesh(me)
    bpy.ops.mesh.delete(type='VERT')
    # 边界补洞（只补手部/腕部附近新产生的边界，不碰模型原有开放边）
    bpy.ops.mesh.select_all(action='DESELECT')
    bm = bmesh.from_edit_mesh(me)
    arm = rig.data
    hseg = (Vector(arm.bones["Hand.L"].head_local), Vector(arm.bones["Hand.L"].tail_local))
    hseg2 = (Vector(arm.bones["Hand.R"].head_local), Vector(arm.bones["Hand.R"].tail_local))
    fseg = (Vector(arm.bones["Forearm.L"].head_local), Vector(arm.bones["Forearm.L"].tail_local))
    fseg2 = (Vector(arm.bones["Forearm.R"].head_local), Vector(arm.bones["Forearm.R"].tail_local))
    def _d(p, a, b):
        ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
        t = max(0.0, min(1.0, t))
        return (p - (a + ab * t)).length
    n_bnd = 0
    for e in bm.edges:
        if len(e.link_faces) != 1: continue
        mid = (e.verts[0].co + e.verts[1].co) / 2
        near = min(_d(mid, *s) for s in (hseg, hseg2, fseg, fseg2))
        e.select = near < 0.045
        n_bnd += e.select
    bmesh.update_edit_mesh(me)
    p(f"remove_fingers: boundary edges(near hand)={n_bnd}")
    if n_bnd:
        bpy.ops.mesh.fill()
    bm = bmesh.from_edit_mesh(me)
    bmesh.update_edit_mesh(me)
    # 新填的面对应顶点需要归一权重？fill 继承边界顶点权重，无需处理
    bpy.ops.object.mode_set(mode='OBJECT')
    p("remove_fingers: done")

def smooth_arm_field(me, vg_all, name_by_idx, iters=14, alpha=0.5):
    """拉普拉斯平滑手臂权重标量场：让手指-肚皮焊接环形成渐变，避免抬臂时撕扯。
    w(v)=手臂链总权重；平滑后按原始的手臂/身体分布重新分配。"""
    N = len(me.vertices)
    w = np.zeros(N, dtype=np.float64)
    arm_dist = [None]*N   # 原始手臂分布
    body_dist = [None]*N  # 原始身体分布
    for v in me.vertices:
        ad, bd = {}, {}
        for ge in v.groups:
            n = name_by_idx.get(ge.group)
            if not n or ge.weight <= 1e-4: continue
            if n in ARM_BONES: ad[n] = ge.weight
            else: bd[n] = ge.weight
        if ad:
            arm_dist[v.index] = ad
            w[v.index] = sum(ad.values())
            body_dist[v.index] = bd
    # 邻接
    neigh_a = np.array([e.vertices for e in me.edges], dtype=np.int64)
    deg = np.zeros(N, dtype=np.float64)
    np.add.at(deg, neigh_a[:, 0], 1.0); np.add.at(deg, neigh_a[:, 1], 1.0)
    deg[deg == 0] = 1.0
    for _ in range(iters):
        s = np.zeros(N, dtype=np.float64)
        np.add.at(s, neigh_a[:, 0], w[neigh_a[:, 1]])
        np.add.at(s, neigh_a[:, 1], w[neigh_a[:, 0]])
        mean = s / deg
        w = (1-alpha)*w + alpha*mean
    changed = 0
    for i in range(N):
        if arm_dist[i] is None: continue
        wi = float(np.clip(w[i], 0.0, 1.0))
        wo = sum(arm_dist[i].values())
        if abs(wi - wo) < 0.01: continue
        changed += 1
        for n, ow in arm_dist[i].items():
            vg_all[n].add([i], wi * ow / wo, 'REPLACE')
        bd = body_dist[i]
        sb = sum(bd.values()) if bd else 0.0
        if sb > 1e-4:
            for n, ow in bd.items():
                vg_all[n].add([i], (1-wi) * ow / sb, 'REPLACE')
        else:
            fb = "Spine" if me.vertices[i].co.z < 0.13 else "Chest"
            vg_all[fb].add([i], 1-wi, 'ADD')
    p(f"smooth_arm_field: iters={iters} changed={changed}")

# ---------- 姿态工具 ----------
def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def aim(pb, target):
    """把骨骼指向世界方向 target（绕其 head 旋转，不改变长度）"""
    t = Vector(target).normalized()
    m = pb.matrix.copy()
    d_cur = (m.to_3x3() @ Vector((0, 1, 0)))
    if d_cur.length < 1e-9: return
    d_cur.normalize()
    q = d_cur.rotation_difference(t)
    R = q.to_matrix().to_4x4()
    head = m.translation
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

# 方向常量（世界系；实测：+X 出现在画面左侧）
HANG_U = (0.45, 0.08, -0.89)   # 上臂：向下略外
HANG_F = (0.22, 0.03, -0.975)  # 前臂/手：近竖直向下、稍外
BEHIND_U = (0.45, -0.18, -0.875)  # 上臂：向下略后（手贴体侧）
BEHIND_F = (0.28, -0.45, -0.85)   # 前臂：贴体侧
BEHIND_H = (0.22, -0.50, -0.84)   # 手：体侧微后

def mir(v): return (-v[0], v[1], v[2])

POSES = {
  # calm: 身体侧转35°朝画面右、慵懒后倾、双臂自然垂到体侧（梗图只见单臂轮廓）
  "calm": [
    ("rot", "Root", 'Z', 35), ("rot", "Pelvis", 'X', 6), ("rot", "Spine", 'X', 3),
    ("rot", "Head", 'Z', 10), ("rot", "Head", 'X', -4),
    ("aim", "UpperArm.L", HANG_U), ("aim", "Forearm.L", HANG_F), ("aim", "Hand.L", HANG_F),
    ("aim", "UpperArm.R", mir(HANG_U)), ("aim", "Forearm.R", mir(HANG_F)), ("aim", "Hand.R", mir(HANG_F)),
  ],
  # rage: 左臂肘部外撑撑开、手掌沿肚皮表面滑到体侧（掌心向外的致敬变体）、
  #       右手贴肚皮、头转向撑肘侧、微后坐。注：源资产前臂内缘与躯干焊接，
  #       前臂无法整体抬离体表，故采用"贴面滑动"式外撑。
  "rage": [
    ("rot", "Root", 'Z', -6), ("rot", "Pelvis", 'X', 4),
    ("rot", "UpperArm.L", 'Y', -32), ("rot", "UpperArm.L", 'X', 6),
    ("rot", "Head", 'Z', -14), ("rot", "Head", 'X', 2),
  ],
  # divine: 正面对称、双臂垂放贴身、端正如钟
  "divine": [
    ("aim", "UpperArm.L", HANG_U), ("aim", "Forearm.L", HANG_F), ("aim", "Hand.L", HANG_F),
    ("aim", "UpperArm.R", mir(HANG_U)), ("aim", "Forearm.R", mir(HANG_F)), ("aim", "Hand.R", mir(HANG_F)),
    ("rot", "Head", 'X', -2),
  ],
  # classic: 3/4 侧面朝画面左、双臂保持原生的扶肚姿态(ref_standing 式日常站姿)、下巴微抬、挺拔
  # 注：背手/后摆会撕扯与前腹焊接的手部曲面，故改用零位移的扶肚站姿（妥协，见报告）
  "classic": [
    ("rot", "Root", 'Z', -32), ("rot", "Spine", 'X', 2), ("rot", "Head", 'X', 8),
  ],
}

CFG = {
  "calm":    dict(tex="Pet01_BaseColor_calm.png",    emissive=False),
  "rage":    dict(tex="Pet01_BaseColor_rage.png",    emissive=False),
  "divine":  dict(tex="Pet01_BaseColor_divine.png",  emissive=True),
  "classic": dict(tex="Pet01_BaseColor_classic.png", emissive=False),
}

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
p("engine =", scene.render.engine)
scene.eevee.taa_render_samples = 64 if FINAL else 24
try:
    scene.eevee.use_denoising = True
except Exception as e:
    p("denoise attr:", e)
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
    direction = Vector((0,0,0.10)) - Vector(loc)
    o.rotation_euler = direction.to_track_quat('-Z','Y').to_euler()
    return o

add_light("Key",  (-0.38, -0.42, 0.42), 26, 0.5, (1.0, 0.98, 0.94))
add_light("Fill", ( 0.45, -0.30, 0.22), 10, 0.7, (0.95, 0.97, 1.0))
add_light("Rim",  ( 0.05,  0.52, 0.40), 45, 0.4, (1.0, 1.0, 1.0))

cam_d = bpy.data.cameras.new("CardCam"); cam_d.lens = 85.0
cam = bpy.data.objects.new("CardCam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam

world = bpy.data.worlds[0] if bpy.data.worlds else bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.12, 0.12, 0.13, 1.0)
bg.inputs[1].default_value = 0.15

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
    return img

def mesh_bbox():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    vs = np.empty(len(ev.data.vertices)*3, dtype=np.float64)
    ev.data.vertices.foreach_get("co", vs)
    vs = vs.reshape(-1,3)
    m44 = np.array(mesh.matrix_world)
    vs = (m44 @ np.hstack([vs, np.ones((len(vs),1))]).T).T[:,:3]
    return vs.min(0), vs.max(0)

def pose_and_render(name, spec, cfg, res, out_path):
    reset_pose()
    img = apply_skin(cfg)
    clean_weights(img)
    if "CS" not in mesh.modifiers:
        cs = mesh.modifiers.new("CS", 'CORRECTIVE_SMOOTH')
        cs.factor = 0.85
        cs.smooth_type = 'SIMPLE'
    for op in spec:
        if op[0] == "rot":
            _, bonename, axis, deg = op
            rot(rig.pose.bones[bonename], axis, deg)
        else:
            _, bonename, target = op
            aim(rig.pose.bones[bonename], target)
        bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    rig.location.z = -mn[2]
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    cx = (mn[0]+mx[0])/2; cz = (mn[2]+mx[2])/2
    height = mx[2]-mn[2]; width = mx[0]-mn[0]
    vw = res[0]/res[1]
    need_h = height*1.30; need_w = width*1.38
    half_fov_y = math.atan(18.0/cam_d.lens)
    D_h = (need_h/2)/math.tan(half_fov_y)
    half_fov_x = math.atan(18.0*vw/cam_d.lens)
    D_w = (need_w/2)/math.tan(half_fov_x)
    D = max(D_h, D_w, 0.45)
    cam.location = (cx, D, cz)
    cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)
    p(f"rendered {name} res={res} bbox_h={height:.3f} bbox_w={width:.3f} aspect={width/height:.3f} minz={mn[2]:.4f} D={D:.3f} -> {out_path}")

if FINAL:
    for name, spec in POSES.items():
        pose_and_render(name, spec, CFG[name], (1100, 1650), os.path.join(WORK, f"role_{name}.png"))
else:
    for name, spec in POSES.items():
        pose_and_render(name, spec, CFG[name], (550, 825), os.path.join(WORK, f"preview_{name}.png"))
LOG.close()
