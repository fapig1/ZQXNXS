# rest 状态手部-肚皮连接拓扑特写（线框+实体对比）
import bpy, math
from mathutils import Vector

mesh = bpy.data.objects["Pet01_Mesh"]
rig = bpy.data.objects["Pet01Rig"]
scene = bpy.context.scene
if rig.animation_data: rig.animation_data_clear()
for pb in rig.pose.bones:
    pb.matrix_basis = __import__("mathutils").Matrix.Identity(4)

scene.render.engine = 'BLENDER_EEVEE'
scene.render.film_transparent = False
scene.render.resolution_x, scene.render.resolution_y = 900, 900
scene.view_settings.view_transform = 'Standard'

# 覆写皮肤材质显示为线框灰体：直接切 object 显示类型
mesh.hide_render = False

cam_d = bpy.data.cameras.new("ZoomCam"); cam_d.lens = 50
cam = bpy.data.objects.new("ZoomCam", cam_d)
scene.collection.objects.link(cam); scene.camera = cam
# 手部特写：对准 (0, 0.049, 0.093) 前方
cam.location = (0.0, 0.28, 0.095)
cam.rotation_euler = (math.radians(90), 0, math.radians(180))

key = bpy.data.lights.new("K", 'AREA'); key.energy = 60; key.size = 0.3
ko = bpy.data.objects.new("K", key); scene.collection.objects.link(ko)
ko.location = (0.1, 0.25, 0.2)
d = Vector((0,0.049,0.093)) - Vector(ko.location)
ko.rotation_euler = d.to_track_quat('-Z','Y').to_euler()

# 用 Open GL 线框不容易；改用 bmesh 给肚皮区域"开窗"？—— 直接渲染实体特写 + 顶点点大小
# EEVEE 下用 wireframe render 不可行，改 Workbench 引擎带线框
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'
sh.color_type = 'SINGLE'
sh.single_color = (0.8, 0.8, 0.8)
sh.show_cavity = True
sh.background_type = 'VIEWPORT'
sh.background_color = (1, 1, 1)

# 相机对准手部 (0, 0.049, 0.093)，距离 0.12
cam.location = (0.0, 0.14, 0.095)
cam.rotation_euler = (math.radians(90), 0, math.radians(180))
scene.render.filepath = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work\wire_hand.png"
bpy.ops.render.render(write_still=True)

# 肩部到手的整条前臂视角
cam.location = (0.06, 0.24, 0.12)
d = Vector((0.025,0.03,0.105)) - Vector(cam.location)
cam.rotation_euler = d.to_track_quat('-Z','Y').to_euler()
scene.render.filepath = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work\wire_arm.png"
bpy.ops.render.render(write_still=True)
print("wire renders done")
