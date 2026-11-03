# 试验：用 Blender 自带 Bone Heat 自动权重重算渲染副本的蒙皮，再渲染四款姿态预览
# blender --background pet01_work.blend --python pose_heat.py
import bpy, sys, os, math
from mathutils import Matrix, Vector
import numpy as np

WORK = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work"
LOG = open(os.path.join(WORK, "pose_heat_log.txt"), "a", encoding="utf-8")
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
    rig.location = (0, 0, 0)

def rot(pb, axis, deg):
    m = pb.matrix.copy()
    head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

def aim(pb, target):
    t = Vector(target).normalized()
    m = pb.matrix.copy()
    d = (m.to_3x3() @ Vector((0,1,0)))
    d.normalize()
    R = d.rotation_difference(t).to_matrix().to_4x4()
    head = m.translation
    T = Matrix.Translation(head)
    pb.matrix = T @ R @ T.inverted() @ m

HANG_U = (0.45, 0.08, -0.89); HANG_F = (0.22, 0.03, -0.975)
BEHIND_U = (0.42, -0.42, -0.80); BEHIND_F = (0.12, -0.88, -0.46); BEHIND_H = (0.05, -0.95, -0.31)
RAISE_U = (0.55, 0.78, 0.10); RAISE_F = (0.62, 0.72, 0.06); RAISE_H = (0.65, 0.70, 0.04)
def mir(v): return (-v[0], v[1], v[2])

POSES = {
  "calm": [("rot","Root",'Z',35),("rot","Pelvis",'X',6),("rot","Spine",'X',3),("rot","Head",'Z',10),("rot","Head",'X',-4),
           ("aim","UpperArm.L",HANG_U),("aim","Forearm.L",HANG_F),("aim","Hand.L",HANG_F),
           ("aim","UpperArm.R",mir(HANG_U)),("aim","Forearm.R",mir(HANG_F)),("aim","Hand.R",mir(HANG_F))],
  "rage": [("rot","Root",'Z',-6),
           ("aim","UpperArm.L",RAISE_U),("aim","Forearm.L",RAISE_F),("aim","Hand.L",RAISE_H),
           ("rot","Head",'Z',-16),("rot","Head",'X',5)],
  "divine": [("aim","UpperArm.L",HANG_U),("aim","Forearm.L",HANG_F),("aim","Hand.L",HANG_F),
             ("aim","UpperArm.R",mir(HANG_U)),("aim","Forearm.R",mir(HANG_F)),("aim","Hand.R",mir(HANG_F)),("rot","Head",'X',-2)],
  "classic": [("rot","Root",'Z',-30),("rot","Spine",'X',2),("rot","Head",'X',8),
              ("aim","UpperArm.L",BEHIND_U),("aim","Forearm.L",BEHIND_F),("aim","Hand.L",BEHIND_H),
              ("aim","UpperArm.R",mir(BEHIND_U)),("aim","Forearm.R",mir(BEHIND_F)),("aim","Hand.R",mir(BEHIND_H))],
}

# ---- 重算 Bone Heat 权重 ----
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True); rig.select_set(True)
bpy.context.view_layer.objects.active = rig
p("running ARMATURE_AUTO (bone heat)...")
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
p("bone heat done")
# 清掉除 armature 外的修饰符并加 Corrective Smooth
for m in list(mesh.modifiers):
    if m.type != 'ARMATURE':
        mesh.modifiers.remove(m)
cs = mesh.modifiers.new("CS", 'CORRECTIVE_SMOOTH')
cs.factor = 0.85; cs.smooth_type = 'SIMPLE'

# ---- 场景/渲染 ----
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 24
try: scene.eevee.use_denoising = True
except Exception: pass
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'

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

# 原始贴图（预览不换肤）
def mesh_bbox():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    vs = np.empty(len(ev.data.vertices)*3, dtype=np.float64)
    ev.data.vertices.foreach_get("co", vs)
    vs = vs.reshape(-1,3)
    m44 = np.array(mesh.matrix_world)
    return ((m44 @ np.hstack([vs, np.ones((len(vs),1))]).T).T[:,:3].min(0),
            (m44 @ np.hstack([vs, np.ones((len(vs),1))]).T).T[:,:3].max(0))

for name, spec in POSES.items():
    reset_pose()
    for op in spec:
        if op[0]=="rot": rot(rig.pose.bones[op[1]], op[2], op[3])
        else: aim(rig.pose.bones[op[1]], op[2])
        bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    rig.location.z = -mn[2]
    bpy.context.view_layer.update()
    mn, mx = mesh_bbox()
    cx=(mn[0]+mx[0])/2; cz=(mn[2]+mx[2])/2
    height=mx[2]-mn[2]; width=mx[0]-mn[0]
    vw = 550/825
    need_h = height*1.30; need_w = width*1.38
    D_h = (need_h/2)/math.atan(18.0/cam_d.lens)*1.0/math.radians(1)*math.radians(1)
    D_h = (need_h/2)/math.tan(math.atan(18.0/cam_d.lens))
    D_w = (need_w/2)/math.tan(math.atan(18.0*vw/cam_d.lens))
    D = max(D_h, D_w, 0.45)
    cam.location=(cx,D,cz); cam.rotation_euler=(math.radians(90),0,math.radians(180))
    scene.render.resolution_x, scene.render.resolution_y = 550, 825
    scene.render.filepath = os.path.join(WORK, f"preview_{name}.png")
    bpy.ops.render.render(write_still=True)
    p(f"rendered {name} bbox_w={width:.3f} D={D:.3f}")
LOG.close()
