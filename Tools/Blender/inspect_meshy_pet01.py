"""Inspect the user-selected Meshy geometry without changing the original GLB."""
import json
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as asset

SOURCE = ROOT / "ArtSource/Characters/Pet01/Candidates/Meshy_AI_Golden_Guardian_0910135714_generate.glb"
OUT = SOURCE.with_suffix("")
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SOURCE))
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
asset.activate(mesh)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
raw = np.array([v.co[:] for v in mesh.data.vertices])
lo, hi = raw.min(axis=0), raw.max(axis=0)
scale = .2 / (hi[2] - lo[2])
# glTF +Z faces Blender -Y; keep project convention +Y forward.
for v in mesh.data.vertices:
    x, y, z = v.co
    v.co = (-(x - (lo[0]+hi[0])/2)*scale,
            -(y - (lo[1]+hi[1])/2)*scale, (z-lo[2])*scale)
for face in mesh.data.polygons:
    face.use_smooth = True
mesh.data.update()
xyz = np.array([v.co[:] for v in mesh.data.vertices])
bm = bmesh.new()
bm.from_mesh(mesh.data)
unseen = set(bm.verts)
components = []
while unseen:
    seed = unseen.pop()
    stack = [seed]
    count = 1
    while stack:
        for edge in stack.pop().link_edges:
            v = edge.other_vert(edge.verts[0])  # traverse both endpoints below
            for v in edge.verts:
                if v in unseen:
                    unseen.remove(v)
                    stack.append(v)
                    count += 1
    components.append(count)
report = {
    "source": str(SOURCE.relative_to(ROOT)).replace("\\", "/"),
    "vertices": len(mesh.data.vertices), "triangles": len(mesh.data.polygons),
    "original_dimensions_blender": (hi-lo).tolist(), "normalization_scale": float(scale),
    "normalized_dimensions_m": np.ptp(xyz, axis=0).tolist(),
    "uv_layers": len(mesh.data.uv_layers), "materials": len(mesh.data.materials),
    "armatures": len([o for o in bpy.data.objects if o.type == "ARMATURE"]),
    "animations": len(bpy.data.actions),
    "boundary_edges": sum(e.is_boundary for e in bm.edges),
    "nonmanifold_edges": sum(not e.is_manifold for e in bm.edges),
    "connected_components_vertices": sorted(components, reverse=True),
    "section_bounds": []
}
for z in np.arange(.005, .201, .005):
    section = xyz[np.abs(xyz[:,2]-z)<.0015]
    if len(section):
        report["section_bounds"].append({"z": round(float(z),4), "n": len(section),
            "min": section.min(axis=0).tolist(), "max": section.max(axis=0).tolist()})
bm.free()
(OUT / "inspection.json").write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
material = bpy.data.materials.new("Inspection_Clay")
material.use_nodes = True
bsdf = material.node_tree.nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (.64,.65,.61,1)
bsdf.inputs["Roughness"].default_value = .6
mesh.data.materials.append(material)
asset.make_studio()
scene = bpy.context.scene
scene.render.resolution_x = 560
scene.render.resolution_y = 680
scene.cycles.samples = 20
scene.render.film_transparent = True
for view in ("front", "side", "back", "hero"):
    asset.set_camera(view)
    scene.render.filepath = str(OUT / ("candidate_"+view+".png"))
    bpy.ops.render.render(write_still=True)
    print("INSPECT_RENDER", view, flush=True)
print("INSPECTION", json.dumps({k:v for k,v in report.items() if k != "section_bounds"}), flush=True)
