"""Validate the actual skinned asset and optionally exported FBX round trips.

Run with Blender --background --factory-startup --python-exit-code 1
    --python Tools/Blender/validate_pet01.py -- --roundtrip
This does not substitute for a Unity import or an Android device test.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as asset

REPORT_PATH = asset.SOURCE / "validation_report.json"
REPORT = {"schema_version":1,"asset_id":"Pet01","checked_at":datetime.now().isoformat(timespec="seconds"),
          "software":"Blender "+bpy.app.version_string,"checks":[],"clips":{},"roundtrip":{},
          "scope":"Blender source and optional FBX reimport; Unity and Android remain untested."}


def check(name, passed, evidence):
    REPORT["checks"].append({"name":name,"passed":bool(passed),"evidence":evidence})
    print("CHECK", "PASS" if passed else "FAIL", name, json.dumps(evidence,ensure_ascii=False), flush=True)


def positions(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    xyz = np.empty(len(mesh.vertices)*3,dtype=np.float64)
    mesh.vertices.foreach_get("co",xyz)
    xyz = xyz.reshape(-1,3)
    matrix = np.array(evaluated.matrix_world)
    result = xyz @ matrix[:3,:3].T + matrix[:3,3]
    evaluated.to_mesh_clear()
    return result


def cloud_distance(a,b):
    def one_way(source,target):
        tree = KDTree(len(source))
        for index,p in enumerate(source):
            tree.insert(Vector(p),index)
        tree.balance()
        return max(tree.find(Vector(p))[2] for p in target)
    return max(one_way(a,b),one_way(b,a))


def root_matrix(rig):
    return np.array(rig.matrix_world @ rig.pose.bones["Root"].matrix)


def validate_source():
    bpy.ops.wm.open_mainfile(filepath=str(asset.BLEND))
    mesh = bpy.data.objects["Pet01_Mesh"]
    rig = bpy.data.objects["Pet01Rig"]
    rig.animation_data.action = None
    asset.reset_pose(rig)
    bpy.context.view_layer.update()
    rest = positions(mesh)
    dimensions = np.ptp(rest,axis=0)
    skin = []
    invalid_groups = []
    for vertex in mesh.data.vertices:
        weights = [g.weight for g in vertex.groups if g.weight>1e-8]
        skin.append((len(weights),sum(weights)))
        invalid_groups.extend(mesh.vertex_groups[g.group].name for g in vertex.groups
                              if mesh.vertex_groups[g.group].name not in rig.data.bones)
    mesh.data.calc_loop_triangles()
    check("finite_geometry",np.isfinite(rest).all(),{"vertices":len(rest)})
    check("mobile_triangle_budget",len(mesh.data.loop_triangles)<=30000,{"triangles":len(mesh.data.loop_triangles),"ceiling":30000})
    check("normalized_skin_weights",all(1<=n<=4 and abs(w-1)<1e-4 for n,w in skin) and not invalid_groups,
          {"max_influences":max(n for n,w in skin),"max_sum_error":max(abs(w-1) for n,w in skin),
           "unbound_groups":sorted(set(invalid_groups))})
    check("rest_size_and_ground",abs(dimensions[2]-.2)<.0001 and abs(rest[:,2].min())<1e-5,
          {"dimensions_m":dimensions.tolist(),"min_z_m":float(rest[:,2].min())})
    check("root_at_sole_center",rig.data.bones["Root"].head_local.length<1e-8,
          {"bone":"Root","head":list(rig.data.bones["Root"].head_local),"bone_count":len(rig.data.bones)})
    check("identity_object_transforms",all((o.location.length<1e-8 and o.rotation_euler.to_matrix().is_identity
              and max(abs(v-1) for v in o.scale)<1e-8) for o in (mesh,rig)),
          {o.name:{"location":list(o.location),"scale":list(o.scale)} for o in (mesh,rig)})
    check("single_skin_binding",len(mesh.modifiers)==1 and mesh.modifiers[0].type=="ARMATURE"
          and mesh.modifiers[0].object==rig and mesh.parent==rig,
          {"mesh":mesh.name,"rig":rig.name,"modifiers":[m.type for m in mesh.modifiers]})
    check("uv_and_materials",len(mesh.data.uv_layers)==1 and len(mesh.data.materials)==3,
          {"uv_layers":[u.name for u in mesh.data.uv_layers],"materials":[m.name for m in mesh.data.materials]})
    comparison=json.loads((asset.PREVIEWS/"Proportions/proportion_report.json").read_text(encoding="utf-8"))
    candidate_dimensions=np.array(comparison["geometry"]["candidate"]["dimensions_m"])
    error=np.abs(dimensions-candidate_dimensions)
    check("candidate_proportion_dimensions",bool(np.all(error<np.array([.002,.005,.0001]))),
          {"current_dimensions_m":dimensions.tolist(),"candidate_dimensions_m":candidate_dimensions.tolist(),
           "absolute_error_m":error.tolist(),"limits_m":[.002,.005,.0001]})
    matching_hash=(comparison["source_files"]["after"]["sha256"]==hashlib.sha256(asset.BLEND.read_bytes()).hexdigest()
                   and comparison["source_files"]["candidate"]["sha256"]==hashlib.sha256((asset.ROOT/asset.REFERENCE_BOARD).read_bytes()).hexdigest())
    overlap={view:comparison["silhouettes"][view]["after"]["intersection_over_union"] for view in ("front","side")}
    check("candidate_orthographic_silhouettes",matching_hash and all(value>.90 for value in overlap.values()),
          {"source_hashes_match":matching_hash,"intersection_over_union":overlap,"minimum":.90,
           "note":"Guard against major silhouette errors; does not replace visual review or user acceptance."})
    forearms=[rig.data.bones["Forearm."+side] for side in ("L","R")]
    check("bent_arms_at_belly_height",all(.09<b.tail_local.z<.115 and b.tail_local.y>b.head_local.y
          and abs(b.tail_local.x)<abs(b.head_local.x) and b.head_local.z-b.tail_local.z<.020 for b in forearms),
          {b.name:{"elbow_m":list(b.head_local),"wrist_m":list(b.tail_local)} for b in forearms})
    # Only the main skin/cavity must be closed. Eyelid hemispheres and pupil
    # overlays deliberately have open, hidden borders and are separate islands.
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    unseen = set(bm.verts)
    components = []
    while unseen:
        seed=unseen.pop()
        group={seed}
        stack=[seed]
        while stack:
            for edge in stack.pop().link_edges:
                for vertex in edge.verts:
                    if vertex in unseen:
                        unseen.remove(vertex)
                        group.add(vertex)
                        stack.append(vertex)
        components.append(group)
    main = max(components,key=len)
    bad_edges = [e for e in bm.edges if e.verts[0] in main and len(e.link_faces)!=2]
    check("continuous_closed_body_and_mouth",not bad_edges,
          {"main_component_vertices":len(main),"nonmanifold_edges":len(bad_edges),"total_mesh_islands":len(components)})
    bm.free()

    planted = np.where(rest[:,2]<.00001)[0]
    snapshots = {"Rest":{1:rest}}
    for name,last in (("Idle",91),("Eat",97)):
        asset.set_action(rig,name,1)
        first = positions(mesh)
        previous = first
        root_rest = root_matrix(rig)
        max_ground = 0.0
        max_plant_drift = 0.0
        max_step = 0.0
        step_evidence = {}
        max_root = 0.0
        mouth_values = []
        sample_frames = sorted(set([1,15,29,37,49,73,last]))
        snapshots[name] = {}
        for frame in range(1,last+1):
            bpy.context.scene.frame_set(frame)
            xyz = positions(mesh)
            max_ground = min(max_ground,float(xyz[:,2].min()))
            max_plant_drift = max(max_plant_drift,float(np.linalg.norm(xyz[planted]-rest[planted],axis=1).max()))
            steps=np.linalg.norm(xyz-previous,axis=1)
            if float(steps.max())>max_step:
                max_step=float(steps.max())
                vertex=mesh.data.vertices[int(steps.argmax())]
                step_evidence={"frame":frame,"vertex":vertex.index,"rest_position":list(vertex.co),
                    "weights":{mesh.vertex_groups[g.group].name:g.weight for g in vertex.groups}}
            max_root = max(max_root,float(np.abs(root_matrix(rig)-root_rest).max()))
            mouth_values.append(-rig.pose.bones["MouthLower"].location.y)
            if frame in sample_frames:
                snapshots[name][frame]=xyz
            previous=xyz
        endpoint_error = float(np.linalg.norm(previous-first,axis=1).max())
        action=bpy.data.actions[name]
        result={"fps":30,"frames":[1,last],"duration_seconds":(last-1)/30,
                "loop":name=="Idle","endpoint_max_error_m":endpoint_error,
                "max_foot_drift_m":max_plant_drift,"minimum_z_m":max_ground,
                "max_consecutive_frame_motion_m":max_step,"root_matrix_max_error":max_root,
                "largest_step":step_evidence,
                "max_mouth_lower_translation_m":float(max(mouth_values)),
                "design_markers":[{"name":m.name,"frame":m.frame,"time_seconds":(m.frame-1)/30} for m in action.pose_markers]}
        REPORT["clips"][name]=result
        check(name+"_endpoints",endpoint_error<1e-6,{"max_error_m":endpoint_error})
        check(name+"_planted_feet",max_plant_drift<.0001 and max_ground>-.0001,
              {"foot_drift_m":max_plant_drift,"minimum_z_m":max_ground,"plant_vertices":len(planted)})
        check(name+"_smooth_motion_and_fixed_root",max_step<.004 and max_root<1e-6,
              {"max_step_m":max_step,"root_matrix_error":max_root,"largest_step":step_evidence})
        if name=="Eat":
            marks=[m for m in action.pose_markers if m.name=="Consume"]
            active=np.array(mouth_values)>.001
            pulses=sum(bool(active[i] and (i==0 or not active[i-1])) for i in range(len(active)))
            check("Eat_three_chews_one_settlement_marker",pulses==3 and len(marks)==1 and marks[0].frame==73,
                  {"chew_pulses":pulses,"consume_marker_frames":[m.frame for m in marks],
                   "note":"Design markers do not become Unity gameplay events automatically."})
    return snapshots


def validate_roundtrips(snapshots):
    for name in ("Rest","Idle","Eat"):
        file=asset.MODEL_DIR/("Pet01.fbx" if name=="Rest" else "Pet01_"+name+".fbx")
        if not file.exists():
            check(name+"_fbx_exists",False,{"file":str(file)})
            continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.render.fps=30
        bpy.ops.import_scene.fbx(filepath=str(file),use_anim=name!="Rest",use_image_search=False,
                                ignore_leaf_bones=False,automatic_bone_orientation=False)
        meshes=[o for o in bpy.context.scene.objects if o.type=="MESH"]
        rigs=[o for o in bpy.context.scene.objects if o.type=="ARMATURE"]
        unexpected=[o.name for o in bpy.context.scene.objects if o.type not in {"MESH","ARMATURE"}]
        check(name+"_fbx_object_scope",len(meshes)==1 and len(rigs)==1 and not unexpected,
              {"meshes":len(meshes),"armatures":len(rigs),"extra_objects":unexpected})
        if len(meshes)!=1 or len(rigs)!=1:
            continue
        mesh,rig=meshes[0],rigs[0]
        check(name+"_fbx_bones",len(rig.data.bones)==26 and "Root" in rig.data.bones,
              {"bones":len(rig.data.bones)})
        frame_offset=0
        take=None
        if name!="Rest":
            take=rig.animation_data.action if rig.animation_data else None
            check(name+"_fbx_take",take is not None,{"take":take.name if take else None})
            if not take:
                continue
            frame_offset=float(take.frame_range[0])-1
            duration=float(take.frame_range[1]-take.frame_range[0])/30
            check(name+"_fbx_duration",abs(duration-REPORT["clips"][name]["duration_seconds"])<1e-5,
                  {"frames":list(take.frame_range),"duration_seconds":duration})
        errors=[]
        for frame,source_points in snapshots[name].items():
            bpy.context.scene.frame_set(int(frame+frame_offset))
            target_points=positions(mesh)
            errors.append({"source_frame":frame,"max_surface_distance_m":cloud_distance(source_points,target_points)})
        maximum=max(e["max_surface_distance_m"] for e in errors)
        check(name+"_fbx_pose_preservation",maximum<.00002,{"maximum_error_m":maximum,"sample_frames":len(errors)})
        textures=[Path(bpy.path.abspath(i.filepath)) for i in bpy.data.images if i.source=="FILE"]
        check(name+"_fbx_texture_paths",bool(textures) and all(p.exists() for p in textures),
              {"paths":[str(p) for p in textures]})
        REPORT["roundtrip"][name]={"file":str(file.relative_to(asset.ROOT)).replace("\\","/"),
            "bytes":file.stat().st_size,"sha256":hashlib.sha256(file.read_bytes()).hexdigest(),
            "pose_errors":errors,"take":take.name if take else None}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--roundtrip",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    snapshots=validate_source()
    if args.roundtrip:
        validate_roundtrips(snapshots)
    REPORT["passed"]=all(c["passed"] for c in REPORT["checks"])
    REPORT["source_sha256"]=hashlib.sha256(asset.BLEND.read_bytes()).hexdigest()
    REPORT_PATH.write_text(json.dumps(REPORT,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    manifest_path=asset.SOURCE/"asset_manifest.json"
    manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["status"]["blender_source_validated"]=all(c["passed"] for c in REPORT["checks"] if "_fbx_" not in c["name"])
    manifest["status"]["blender_fbx_roundtrip"]=args.roundtrip and REPORT["passed"] and len(REPORT["roundtrip"])==3
    manifest["validation_report"]="ArtSource/Characters/Pet01/validation_report.json"
    manifest["source_sha256"]=REPORT["source_sha256"]
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    if (asset.ART/"asset_manifest.json").exists():
        (asset.ART/"asset_manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    if not REPORT["passed"]:
        raise RuntimeError("Asset validation failed; see "+str(REPORT_PATH))
    print("PET01 VALIDATION PASSED",len(REPORT["checks"]),flush=True)


if __name__=="__main__":
    main()
