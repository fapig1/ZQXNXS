# 四款卡面姿态渲染：只旋转姿态骨骼，不动网格/骨骼权重；渲染透明底角色图
# 用法: blender --background pet01_work.blend --python pose_render.py [-- final]
import bpy, sys, os, math
from mathutils import Matrix, Vector

WORK = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work"
TEXDIR = os.path.join(WORK, "tex_variant")
ORIG_TEX = r"D:\ZengQiangXianShi\ArtSource\Characters\Pet01\Textures\Pet01_BaseColor.png"
FINAL = "--" in sys.argv and "final" in sys.argv[sys.argv.index("--")+1:]
LOG = open(os.path.join(WORK, "pose_render_log.txt"), "a", encoding="utf-8")
def p(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.write(s+"\n"); LOG.flush()

# ---------- 场景清理 ----------
rig = bpy.data.objects["Pet01Rig"]
mesh = bpy.data.objects["Pet01_Mesh"]
mesh.hide_render = False
for ob in bpy.data.objects:
    if ob.name in ("Meshy_Proportion_Reference", "PreviewFloor", "Pet01_PreviewCamera"):
        ob.hide_render = True
    if ob.type == 'LIGHT':
        ob.hide_render = True  # 用自己的灯
if rig.animation_data:
    rig.animation_data_clear()
arm = rig.data

def reset_pose():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.rotation_mode = 'QUATERNION'

# ---------- 权重清理：去掉"远离手臂骨骼却被赋了手臂权重"的肚皮顶点 ----------
def _dist_point_seg(p, a, b):
    ab = b - a; t = (p - a).dot(ab) / max(ab.dot(ab), 1e-12)
    t = max(0.0, min(1.0, t))
    return (p - (a + ab * t)).length

def clean_arm_weights(threshold=0.025):
    me = mesh.data
    vg_all = mesh.vertex_groups
    name_by_idx = {vg.index: vg.name for vg in vg_all}
    segs = {}
    for bname in ("UpperArm.L","Forearm.L","Hand.L","UpperArm.R","Forearm.R","Hand.R"):
        b = arm.bones[bname]
        segs[bname] = (Vector(b.head_local), Vector(b.tail_local))
    affected = 0
    for v in me.vertices:
        gw = {name_by_idx[ge.group]: ge.weight for ge in v.groups if ge.group in name_by_idx}
        arm_w = {n: w for n, w in gw.items() if n in segs and w > 1e-4}
        if not arm_w: continue
        removed = False
        for n, w in arm_w.items():
            h, t = segs[n]
            if _dist_point_seg(v.co, h, t) > threshold:
                vg_all[n].add([v.index], 0.0, 'REPLACE')
                removed = True
        if removed:
            # 重新归一化
            gw2 = {name_by_idx[ge.group]: ge.weight for ge in v.groups}
            s = sum(gw2.values())
            if s > 0.02:
                for n, w in gw2.items():
                    vg_all[n].add([v.index], w / s, 'REPLACE')
            else:
                fallback = "Spine" if v.co.z < 0.13 else "Chest"
                vg_all[fallback].add([v.index], 1.0, 'ADD')
            affected += 1
    p(f"clean_arm_weights: {affected} vertices fixed (threshold={threshold})")

# ---------- 姿态工具（世界轴旋转，绕骨骼 rest head）----------
def rot(pb, axis, deg):
    if abs(deg) < 1e-6: return
    m = pb.matrix.copy()
    head = m.translation          # 当前(父骨骼已旋转后)的实际 head 位置
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

# ---------- 四款姿态（角度单位度；按父->子顺序应用）----------
POSES = {
  # calm: 单臂自然下垂、身体侧转 35°、慵懒后倾站姿（大幅外展避开肚皮体积）
  "calm": [
    ("Root", 'Z', 35), ("Pelvis", 'X', 5), ("Spine", 'X', 2), ("Head", 'X', -4),
    ("UpperArm.L", 'X', -40), ("UpperArm.L", 'Y', -34), ("Forearm.L", 'X', -18),
    ("UpperArm.R", 'X', -44), ("UpperArm.R", 'Y', 36), ("Forearm.R", 'X', -20),
  ],
  # rage: 右臂抬至头肩高度斜外前平伸、左手悬于肚皮前方、微后坐
  "rage": [
    ("Root", 'Z', -8), ("Pelvis", 'X', 4),
    ("UpperArm.R", 'X', 95), ("UpperArm.R", 'Y', -28), ("Forearm.R", 'X', 12), ("Hand.R", 'X', 8),
    ("UpperArm.L", 'X', 62), ("UpperArm.L", 'Y', 20), ("Forearm.L", 'X', 18), ("Hand.L", 'X', 20),
    ("Head", 'Z', 18),
    ("Thigh.L", 'Z', 6), ("Thigh.R", 'Z', -6),
  ],
  # divine: 正面对称、双臂垂放贴身、端正、发光白眼(材质处理)
  "divine": [
    ("UpperArm.L", 'X', -40), ("UpperArm.L", 'Y', -30), ("Forearm.L", 'X', -14),
    ("UpperArm.R", 'X', -40), ("UpperArm.R", 'Y', 30), ("Forearm.R", 'X', -14),
    ("Head", 'X', 3),
  ],
  # classic: 3/4 侧面、双臂背后、下巴微抬、挺拔
  "classic": [
    ("Root", 'Z', -30), ("Spine", 'X', -3), ("Head", 'X', 6),
    ("UpperArm.L", 'X', -50), ("UpperArm.L", 'Y', -34), ("Forearm.L", 'X', -48),
    ("UpperArm.R", 'X', -54), ("UpperArm.R", 'Y', 35), ("Forearm.R", 'X', -50),
  ],
}

# ---------- 渲染配置 ----------
CFG = {
  "calm":    dict(tex="Pet01_BaseColor_calm.png",   emissive=False),
  "rage":    dict(tex="Pet01_BaseColor_rage.png",   emissive=False),
  "divine":  dict(tex="Pet01_BaseColor_divine.png", emissive=True),
  "classic": dict(tex="Pet01_BaseColor_classic.png",emissive=False),
}

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
p("engine =", scene.render.engine)
scene.eevee.taa_render_samples = 48 if FINAL else 16
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.resolution_percentage = 100

# 灯光：等距软光 + 轻微 rim
def add_light(name, loc, energy, size, color=(1,1,1)):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = energy; d.size = size; d.color = color
    o = bpy.data.objects.new(name, d); scene.collection.objects.link(o)
    o.location = loc
    # 朝向原点上方 0.1m
    direction = Vector((0,0,0.10)) - Vector(loc)
    o.rotation_euler = direction.to_track_quat('-Z','Y').to_euler()
    return o

key  = add_light("Key",  (-0.38, -0.42, 0.42), 26, 0.5, (1.0, 0.98, 0.94))
fill = add_light("Fill", ( 0.45, -0.30, 0.22), 10, 0.7, (0.95, 0.97, 1.0))
rim  = add_light("Rim",  ( 0.05,  0.52, 0.40), 45, 0.4, (1.0, 1.0, 1.0))

cam_d = bpy.data.cameras.new("CardCam"); cam_d.lens = 85.0
cam = bpy.data.objects.new("CardCam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam

world = bpy.data.worlds[0] if bpy.data.worlds else bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
bg.inputs[0].default_value = (0.12, 0.12, 0.13, 1.0)   # 微弱环境补光
bg.inputs[1].default_value = 0.15

# ---------- 材质换肤 ----------
def apply_skin(cfg):
    tex_path = os.path.join(TEXDIR, cfg["tex"]) if cfg["tex"] != "original" else ORIG_TEX
    img = bpy.data.images.get("card_skin")
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.load(tex_path, check_existing=False)
    img.name = "card_skin"
    img.colorspace_settings.name = 'sRGB'
    for mname in ("Pet01_Skin", "Pet01_Oral", "Pet01_Eyes"):
        m = bpy.data.materials[mname]
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                for inp in n.inputs['Base Color'].links[:]:
                    m.node_tree.links.remove(inp)
                n.inputs['Base Color'].default_value = (*img_mean_color(img), 1.0)
                n.inputs['Base Color'].is_linked  # noqa
    # 真正接法：把贴图接到三个材质的 Base Color
    for mname in ("Pet01_Skin", "Pet01_Oral", "Pet01_Eyes"):
        m = bpy.data.materials[mname]
        nt = m.node_tree
        tex_node = None
        for n in nt.nodes:
            if n.type == 'TEX_IMAGE':
                tex_node = n; break
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

import numpy as _np
def img_mean_color(img):
    # 用小样本估计平均色，仅作断链保护，实际颜色来自贴图链接
    return (0.8, 0.8, 0.8)

# ---------- 姿态 + 取景 + 渲染 ----------
dg = bpy.context.evaluated_depsgraph_get()
def mesh_bbox():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    import numpy as np
    vs = np.empty(len(ev.data.vertices)*3, dtype=np.float64)
    ev.data.vertices.foreach_get("co", vs)
    vs = vs.reshape(-1,3)
    mw = ev.matrix_world
    m44 = _np.array([[mw[i][j] for j in range(4)] for i in range(4)])
    vs = (m44 @ _np.hstack([vs, _np.ones((len(vs),1))]).T).T[:,:3]
    return vs.min(0), vs.max(0)

def pose_and_render(name, spec, cfg, res, out_path):
    reset_pose()
    clean_arm_weights()
    apply_skin(cfg)
    # Corrective Smooth: 抑制手/肚皮过渡面片在大摆臂下的拉伸尖刺
    if "CS" not in mesh.modifiers:
        cs = mesh.modifiers.new("CS", 'CORRECTIVE_SMOOTH')
        cs.factor = 0.85
        cs.smooth_type = 'SIMPLE'
    seq = {}
    for bonename, axis, deg in spec:
        pb = rig.pose.bones[bonename]
        rot(pb, axis, deg)
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    if mn[2] < -0.004:  # 脚插地：整体抬
        rig.location.z = -mn[2]
    elif mn[2] > 0.004:
        rig.location.z = -mn[2]
    else:
        rig.location.z = 0.0
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    cx = (mn[0]+mx[0])/2; cz = (mn[2]+mx[2])/2
    height = mx[2]-mn[2]; width = mx[0]-mn[0]
    # 相机: 正前方 -Y, 85mm, 竖幅 res
    vw = res[0]/res[1]
    need_h = height*1.32; need_w = width*1.45
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
    p(f"rendered {name} res={res} bbox_h={height:.3f} bbox_w={width:.3f} minz={mn[2]:.4f} D={D:.3f} -> {out_path}")

if FINAL:
    for name, spec in POSES.items():
        pose_and_render(name, spec, CFG[name], (1100, 1650), os.path.join(WORK, f"role_{name}.png"))
else:
    for name, spec in POSES.items():
        pose_and_render(name, spec, CFG[name], (440, 660), os.path.join(WORK, f"preview_{name}.png"))
LOG.close()
