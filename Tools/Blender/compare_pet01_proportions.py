"""Capture old, selected candidate and revised geometry with identical cameras.

Run in Blender, then run analyze_pet01_proportions.py with system Python.
The candidate remains an unmodified source file; only height-normalized copies
are used for comparison. No alignment is fitted to improve the overlap score.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as asset

OUT = asset.PREVIEWS / "Proportions"
OUT.mkdir(parents=True, exist_ok=True)


def save_geometry(mesh, label):
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = evaluated.to_mesh()
    data.calc_loop_triangles()
    vertices = np.array([v.co[:] for v in data.vertices], dtype=np.float64)
    matrix = np.array(evaluated.matrix_world)
    vertices = vertices @ matrix[:3,:3].T + matrix[:3,3]
    triangles = np.array([t.vertices[:] for t in data.loop_triangles], dtype=np.int32)
    evaluated.to_mesh_clear()
    np.savez_compressed(OUT / (label+"_geometry.npz"), vertices=vertices, triangles=triangles)
    return {"vertices": len(vertices), "triangles": len(triangles),
            "bounds_min": vertices.min(axis=0).tolist(), "bounds_max": vertices.max(axis=0).tolist(),
            "dimensions_m": np.ptp(vertices,axis=0).tolist()}


def render(label):
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 704
    scene.cycles.samples = 24
    scene.render.film_transparent = True
    for view in ("front", "side"):
        asset.set_camera(view)
        scene.render.filepath = str(OUT / (label+"_"+view+".png"))
        bpy.ops.render.render(write_still=True)


def candidate():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(asset.ROOT / asset.REFERENCE_BOARD))
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    asset.activate(mesh)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    raw = np.array([v.co[:] for v in mesh.data.vertices])
    lo,hi=raw.min(axis=0),raw.max(axis=0)
    scale=.2/(hi[2]-lo[2])
    for v in mesh.data.vertices:
        x,y,z=v.co
        v.co=(-(x-(lo[0]+hi[0])/2)*scale, -(y-(lo[1]+hi[1])/2)*scale, (z-lo[2])*scale)
    for face in mesh.data.polygons:
        face.use_smooth=True
    mat=bpy.data.materials.new("Meshy_Inspection_Clay")
    mat.use_nodes=True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(.64,.65,.61,1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value=.60
    mesh.data.materials.append(mat)
    asset.make_studio()
    bpy.context.view_layer.update()
    return mesh


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--geometry-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    manifest={"revision":asset.SHAPE_REVISION,"comparison_height_m":.2,
              "camera":"Orthographic, identical camera positions and scale for all three assets",
              "alignment":"Ground z=0; candidate bounding-box horizontal center at origin; no fitted deformation",
              "files":{},"geometry":{}}
    for label,file in (("before",asset.SOURCE/"Archive/20260910_before_meshy_proportions/pet01.blend"),
                       ("candidate",asset.ROOT/asset.REFERENCE_BOARD), ("after",asset.BLEND)):
        if label=="candidate":
            mesh=candidate()
        else:
            bpy.ops.wm.open_mainfile(filepath=str(file))
            rig=bpy.data.objects["Pet01Rig"]
            rig.animation_data.action=None
            asset.reset_pose(rig)
            bpy.context.view_layer.update()
            mesh=bpy.data.objects["Pet01_Mesh"]
        manifest["files"][label]={"file":str(file.relative_to(asset.ROOT)).replace("\\","/"),
                                  "sha256":hashlib.sha256(file.read_bytes()).hexdigest()}
        manifest["geometry"][label]=save_geometry(mesh,label)
        if not args.geometry_only:
            render(label)
    (OUT/"capture_manifest.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    print("PROPORTION CAPTURE",json.dumps(manifest["geometry"]),flush=True)


if __name__=="__main__":
    main()
