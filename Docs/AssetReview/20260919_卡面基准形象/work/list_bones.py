# 列出 pet01_work.blend 的骨骼、材质、网格、动画，供姿态脚本编写参考
import bpy, sys

log = open(r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象\work\list_bones_log.txt", "w", encoding="utf-8")
def p(*a):
    s = " ".join(str(x) for x in a)
    print(s)
    log.write(s + "\n")

p("=== Objects ===")
for ob in bpy.data.objects:
    p(f"obj: {ob.name!r} type={ob.type} parent={ob.parent.name if ob.parent else None} "
      f"loc=({ob.location.x:.4f},{ob.location.y:.4f},{ob.location.z:.4f}) rot=({ob.rotation_euler.x:.4f},{ob.rotation_euler.y:.4f},{ob.rotation_euler.z:.4f}) scale=({ob.scale.x:.3f},{ob.scale.y:.3f},{ob.scale.z:.3f}) hide_render={ob.hide_render}")

for arm in bpy.data.armatures:
    p(f"\n=== Armature {arm.name} ({len(arm.bones)} bones) ===")
    for b in arm.bones:
        head = b.head_local; tail = b.tail_local
        p(f"bone: {b.name:24s} parent={b.parent.name if b.parent else '-':20s} "
          f"head=({head.x:+.4f},{head.y:+.4f},{head.z:+.4f}) tail=({tail.x:+.4f},{tail.y:+.4f},{tail.z:+.4f})")

p("\n=== Materials ===")
for m in bpy.data.materials:
    nodes_info = []
    if m.use_nodes and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                bc = n.inputs['Base Color']
                val = None
                if bc.is_linked:
                    ln = bc.links[0].from_node
                    val = f"linked:{ln.type}"
                    if ln.type == 'TEX_IMAGE' and ln.image:
                        val += f" img={ln.image.name} {ln.image.filepath}"
                else:
                    val = tuple(round(c, 3) for c in bc.default_value)
                emis = n.inputs['Emission Color']
                ev = tuple(round(c, 3) for c in emis.default_value) if not emis.is_linked else "linked"
                es = n.inputs['Emission Strength'].default_value
                nodes_info.append(f"BSDF base={val} emis={ev} strength={es} metal={n.inputs['Metallic'].default_value:.2f} rough={n.inputs['Roughness'].default_value:.2f}")
    p(f"mat: {m.name} use_nodes={m.use_nodes} :: {nodes_info}")

p("\n=== Actions ===")
for a in bpy.data.actions:
    p(f"action: {a.name} frames {a.frame_range[0]:.0f}-{a.frame_range[1]:.0f}")

p("\n=== Meshes ===")
for me in bpy.data.meshes:
    p(f"mesh: {me.name} verts={len(me.vertices)} tris={sum(len(p.vertices)-2 for p in me.polygons)} material_slots={[ms.name if ms.material else None for ms in me.materials]}")

log.close()
