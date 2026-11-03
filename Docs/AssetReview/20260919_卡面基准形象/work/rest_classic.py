import bpy, math, os
from mathutils import Vector
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 48
scene.view_settings.view_transform = 'Standard'
scene.render.film_transparent = True
scene.render.image_settings.file_format='PNG'; scene.render.image_settings.color_mode='RGBA'
rig = bpy.data.objects["Pet01Rig"]; mesh = bpy.data.objects["Pet01_Mesh"]
img = bpy.data.images.load("D:/ZengQiangXianShi/Docs/AssetReview/20260919_卡面基准形象/work/tex_variant/Pet01_BaseColor_classic.png")
for mname in ("Pet01_Skin","Pet01_Oral","Pet01_Eyes"):
    m = bpy.data.materials[mname]
    tex = next(n for n in m.node_tree.nodes if n.type=='TEX_IMAGE')
    tex.image = img
if rig.animation_data: rig.animation_data_clear()
for ob in bpy.data.objects:
    if ob.type=='LIGHT': ob.hide_render=True
d = bpy.data.lights.new("K",'AREA'); d.energy=26; d.size=0.5
o = bpy.data.objects.new("K",d); scene.collection.objects.link(o); o.location=(-0.38,-0.42,0.42)
o.rotation_euler = (Vector((0,0,0.10))-Vector(o.location)).to_track_quat('-Z','Y').to_euler()
d2 = bpy.data.lights.new("R",'AREA'); d2.energy=45; d2.size=0.4
o2 = bpy.data.objects.new("R",d2); scene.collection.objects.link(o2); o2.location=(0.05,0.52,0.40)
o2.rotation_euler = (Vector((0,0,0.10))-Vector(o2.location)).to_track_quat('-Z','Y').to_euler()
cam_d = bpy.data.cameras.new("C"); cam_d.lens=85.0
cam = bpy.data.objects.new("C",cam_d); scene.collection.objects.link(cam); scene.camera=cam
# classic 姿态（不手术）
from mathutils import Matrix, Vector as V
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'
def rot(name, axis, deg):
    pb = rig.pose.bones[name]
    m = pb.matrix.copy(); head = m.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(head).inverted() @ m
rot("Root",'Z',-32); rot("Spine",'X',2); rot("Head",'X',8)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(dg)
import numpy as np
nv=len(ev.data.vertices); vs=np.empty(nv*3); ev.data.vertices.foreach_get("co",vs); vs=vs.reshape(-1,3)
mn=vs.min(0); mx=vs.max(0)
rig.location.z = -mn[2]
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg)
nv=len(ev.data.vertices); vs=np.empty(nv*3); ev.data.vertices.foreach_get("co",vs); vs=vs.reshape(-1,3)
mn=vs.min(0); mx=vs.max(0)
cx=(mn[0]+mx[0])/2; cz=(mn[2]+mx[2])/2; h=mx[2]-mn[2]; w=mx[0]-mn[0]
D = max((h*1.30/2)/math.tan(math.atan(18.0/85.0)), (w*1.38/2)/math.tan(math.atan(18.0*550/825/85.0)), 0.45)
cam.location=(cx,D,cz); cam.rotation_euler=(math.radians(90),0,math.radians(180))
scene.render.resolution_x, scene.render.resolution_y = 550,825
scene.render.filepath = os.path.join(os.path.dirname(bpy.data.filepath), "rest_classic_pose.png")
bpy.ops.render.render(write_still=True)
