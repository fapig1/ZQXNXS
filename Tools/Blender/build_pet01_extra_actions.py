"""Add TouchReact / DozeLoop / WalkLoop actions to the frozen Pet01 rig.

Mesh and skeleton are NOT modified. This only authors three new Actions on the
existing armature, exports them as new FBX files, and validates the roundtrip,
mirroring the approach in build_pet01.py / validate_pet01.py.

Run with:
  D:/blender.exe --background --factory-startup --python-exit-code 1 \
      --python Tools/Blender/build_pet01_extra_actions.py -- --phase build
  D:/blender.exe --background --factory-startup --python-exit-code 1 \
      --python Tools/Blender/build_pet01_extra_actions.py -- --phase export
  D:/blender.exe --background --factory-startup --python-exit-code 1 \
      --python Tools/Blender/build_pet01_extra_actions.py -- --phase validate
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pet01 as base  # reuse ROOT/SOURCE/MODEL_DIR/ART/BLEND/FPS/reset_pose/action_curves/blink/smoothstep

ROOT, SOURCE, MODEL_DIR, ART, BLEND, FPS = base.ROOT, base.SOURCE, base.MODEL_DIR, base.ART, base.BLEND, base.FPS

# name -> (last_frame, loop)
CLIPS = {
    "TouchReact": (22, False),   # (22-1)/30 = 0.7s, matches PetConfig Petted.DurationSeconds
    "DozeLoop": (121, True),     # (121-1)/30 = 4.0s slow breathing loop
    "WalkLoop": (31, True),      # (31-1)/30 = 1.0s in-place step cycle
}
EXPECTED_VERTICES = 10434
EXPECTED_BONES = 26
FORCE_REBUILD = False


def log(message):
    print("PET01_EXTRA " + message, flush=True)


def pose_touch_react(bones, t, duration):
    """Startled flinch: quick head pull-back + wide-eyed pause, settles back to neutral.

    t in [0, duration]. Endpoints (t=0 and t=duration) must both be the neutral
    zero pose so the clip can be re-triggered cleanly, matching how Idle/Eat both
    start and end at (near-)zero bone transforms.
    """
    # Single decaying oscillation that starts and ends at 0.
    phase = t / duration
    envelope = math.sin(phase * math.pi)  # 0 at phase=0 and phase=1, peak at 0.5
    flinch = envelope * math.sin(phase * math.pi * 2.4)  # quick wobble within the envelope
    bones["Spine"].rotation_euler.x = math.radians(-4.0 * envelope)
    bones["Chest"].rotation_euler.x = math.radians(-6.0 * envelope)
    bones["Head"].rotation_euler.x = math.radians(-9.0 * envelope)
    bones["Head"].rotation_euler.z = math.radians(3.0 * flinch)
    bones["Tail"].rotation_euler.z = math.radians(10.0 * flinch)
    for sign, suffix in ((1, "L"), (-1, "R")):
        bones["UpperArm." + suffix].rotation_euler.x = -.05 * envelope
        bones["UpperArm." + suffix].rotation_euler.z = sign * .03 * flinch
        bones["Forearm." + suffix].rotation_euler.x = -.04 * envelope
    # Wide-eyed: lids pull open briefly (negative closing beyond rest), then relax back to open (0).
    startle_open = -0.25 * envelope
    return startle_open


def pose_doze_loop(bones, t, duration):
    """Slow sleepy breathing loop with drooping head and mostly-closed eyes."""
    phase = math.tau * t / duration
    breath = math.sin(phase)
    bones["Spine"].scale = (1 + .010 * breath, 1 + .008 * breath, 1 + .009 * breath)
    bones["Chest"].rotation_euler.x = math.radians(1.0 * breath - 3.0)
    bones["Head"].rotation_euler.x = math.radians(-1.2 * breath - 8.0)  # drooped forward
    bones["Head"].rotation_euler.z = math.radians(.8 * math.sin(phase * .5))
    bones["Tail"].rotation_euler.z = math.radians(1.2 * math.sin(phase))
    for sign, suffix in ((1, "L"), (-1, "R")):
        bones["UpperArm." + suffix].rotation_euler.z = sign * math.radians(.4 * breath)
    # Mostly closed with a very slow partial reopen, like a doze not a hard sleep.
    closing = 0.78 + 0.10 * math.sin(phase * .5)
    return closing


def pose_walk_loop(bones, t, duration):
    """In-place stepping cycle: alternating leg swing, no translation on any bone.

    History of fix attempts for validate_pet01's "WalkLoop_above_ground" (min
    clearance must stay above -0.0005 m) — kept here because every attempt
    exposed a different real bug worth not re-discovering later:
      1) Thigh/Shin/Foot rotation only, all on one quadratic ramp: -0.0032 m
         (way under), but FBX roundtrip clean.
      2) Same three rotations, but Thigh on a quadratic ramp and Shin/Foot on
         a sqrt ramp (so the lower leg starts lifting earlier in the swing
         than the thigh does): -0.0007 m — closest rotation-only result, and
         FBX roundtrip stayed excellent (4.2e-7 m). This is the structure kept
         below, just with larger amplitudes to close the remaining 0.2mm.
      3) Added a direct Foot.location.z translation on top of attempt 2 to
         force clearance: passed ground-clearance inside Blender's own pose
         evaluation, but FBX roundtrip diverged by ~18mm.
      4) Tried lifting the whole body via Pelvis.location instead of the leg:
         first with local Z (moves the mesh in world -Y, not up at all —
         confirmed with Tools/Blender/diagnose_pelvis_axis.py), then with the
         correct local Y (world +Z) at a larger amplitude (~0.003-0.007m):
         ground clearance did hit 0.0 exactly, but FBX roundtrip broke again
         (~16mm). The common thread across attempts 3 and 4b is not "which
         axis" but "any bone location channel with an amplitude large enough
         to matter for clearance (>1-2mm) does not survive this project's FBX
         bake/reimport for a non-root pose bone" — the original small Pelvis Z
         bob (0.0035m, present since before any of these fixes, on the wrong
         axis and therefore contributing ~nothing to clearance) never showed
         this problem only because its effect was too small to matter either
         way. Conclusion: don't use bone translation for this rig's FBX
         pipeline at all; stay rotation-only and just push the amplitude.
    """
    phase = math.tau * t / duration
    bones["Spine"].rotation_euler.x = math.radians(2.0 * math.sin(phase * 2))
    bones["Chest"].rotation_euler.z = math.radians(2.5 * math.sin(phase))
    bones["Head"].rotation_euler.z = math.radians(1.2 * math.sin(phase + .3))
    bones["Tail"].rotation_euler.z = math.radians(6.0 * math.sin(phase))
    for sign, suffix, offset in ((1, "L", 0.0), (-1, "R", math.pi)):
        raw = max(0.0, math.sin(phase + offset))
        # Jumping straight to much larger amplitudes (22/38/-16) got closer on
        # ground clearance (-0.00059m, just 0.09mm short of the -0.0005m bar)
        # but broke FBX roundtrip badly (0.019m) — evidently large rotations on
        # this short-limbed rig also stress the export pipeline, similar to
        # translation. A conservative nudge (17/26/-11) round-tripped cleanly
        # (4.4e-7m) but still missed clearance by 0.24mm (-0.00074m). Nudging
        # only the Foot counter-rotation further (the bone whose rotation most
        # directly lifts the toe) closes that gap while keeping Thigh/Shin at
        # the values that already proved safe for FBX roundtrip.
        lift = raw ** 2
        lift_early = raw ** 0.5
        bones["Thigh." + suffix].rotation_euler.x = math.radians(17.0 * lift)
        bones["Shin." + suffix].rotation_euler.x = math.radians(26.0 * lift_early)
        bones["Foot." + suffix].rotation_euler.x = math.radians(-15.0 * lift_early)
        bones["UpperArm." + suffix].rotation_euler.x = math.radians(10.0 * lift)
    closing = 0.0
    return closing


POSE_FUNCTIONS = {
    "TouchReact": pose_touch_react,
    "DozeLoop": pose_doze_loop,
    "WalkLoop": pose_walk_loop,
}


def pose(rig, action_name, t, duration):
    base.reset_pose(rig)
    bones = rig.pose.bones
    closing = POSE_FUNCTIONS[action_name](bones, t, duration)
    for suffix in ("L", "R"):
        bones["LidUpper." + suffix].rotation_euler.y = -math.radians(78) * closing
        bones["LidLower." + suffix].rotation_euler.y = math.radians(78) * closing


def create_extra_actions(rig):
    for name, (last, loop) in CLIPS.items():
        duration = (last - 1) / FPS
        rig.animation_data_create()
        rig.animation_data.action = None
        for frame in range(1, last + 1):
            bpy.context.scene.frame_set(frame)
            pose(rig, name, (frame - 1) / FPS, duration)
            for bone in rig.pose.bones:
                for channel in ("location", "rotation_euler", "scale"):
                    bone.keyframe_insert(data_path=channel, frame=frame, group=bone.name)
        action = rig.animation_data.action
        action.name = name
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, last
        action.use_cyclic = loop
        for curve in base.action_curves(action):
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
        marker_end = "Loop" if loop else "Complete"
        for frame, label in ((1, "Start"), (last, marker_end)):
            marker = action.pose_markers.new(label)
            marker.frame = frame
        log("Created action %s: %d frames at %d fps (loop=%s, %.3fs)" % (name, last, FPS, loop, duration))


def set_action(rig, name, frame=1):
    last = CLIPS[name][0]
    rig.animation_data_create()
    rig.animation_data.action = bpy.data.actions[name]
    if rig.animation_data.action.slots:
        rig.animation_data.action_slot = rig.animation_data.action.slots[0]
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = last
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def build():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    rig = bpy.data.objects["Pet01Rig"]
    mesh = bpy.data.objects["Pet01_Mesh"]
    mesh.data.calc_loop_triangles()
    if len(mesh.data.vertices) != EXPECTED_VERTICES:
        raise RuntimeError("Unexpected vertex count %d, expected %d; mesh must stay frozen" %
                            (len(mesh.data.vertices), EXPECTED_VERTICES))
    if len(rig.data.bones) != EXPECTED_BONES:
        raise RuntimeError("Unexpected bone count %d, expected %d; skeleton must stay frozen" %
                            (len(rig.data.bones), EXPECTED_BONES))
    for name in CLIPS:
        if name in bpy.data.actions:
            if not FORCE_REBUILD:
                raise RuntimeError("Action %s already exists; pass --force to regenerate." % name)
            bpy.data.actions.remove(bpy.data.actions[name])
    create_extra_actions(rig)
    set_action(rig, "TouchReact", 1)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    log("Saved: " + str(BLEND))


def export():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    rig = bpy.data.objects["Pet01Rig"]
    mesh = bpy.data.objects["Pet01_Mesh"]
    common = dict(use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                  use_armature_deform_only=True, axis_forward="-Z", axis_up="Y",
                  apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                  use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=False,
                  path_mode="RELATIVE", embed_textures=False, bake_anim_use_nla_strips=False,
                  bake_anim_use_all_actions=False, bake_anim_simplify_factor=0.0,
                  bake_anim_step=1.0, armature_nodetype="NULL")

    def select():
        bpy.ops.object.select_all(action="DESELECT")
        rig.select_set(True)
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = rig

    bpy.data.images["Pet01_BaseColor"].filepath = str(base.TEXTURE_DIR / "Pet01_BaseColor.png")
    for name in CLIPS:
        set_action(rig, name, 1)
        bpy.context.scene.name = name
        select()
        bpy.ops.export_scene.fbx(filepath=str(MODEL_DIR / ("Pet01_" + name + ".fbx")), bake_anim=True, **common)
        log("Exported Pet01_%s.fbx" % name)


def positions(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    xyz = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", xyz)
    xyz = xyz.reshape(-1, 3)
    matrix = np.array(evaluated.matrix_world)
    result = xyz @ matrix[:3, :3].T + matrix[:3, 3]
    evaluated.to_mesh_clear()
    return result


def cloud_distance(a, b):
    def one_way(source, target):
        tree = KDTree(len(source))
        for index, p in enumerate(source):
            tree.insert(Vector(p), index)
        tree.balance()
        return max(tree.find(Vector(p))[2] for p in target)
    return max(one_way(a, b), one_way(b, a))


def root_matrix(rig):
    return np.array(rig.matrix_world @ rig.pose.bones["Root"].matrix)


REPORT = {"schema_version": 1, "asset_id": "Pet01", "extra_actions": {}, "checks": [], "roundtrip": {}}


def check(name, passed, evidence):
    REPORT["checks"].append({"name": name, "passed": bool(passed), "evidence": evidence})
    print("CHECK", "PASS" if passed else "FAIL", name, json.dumps(evidence, ensure_ascii=False), flush=True)


def validate():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    rig = bpy.data.objects["Pet01Rig"]
    mesh = bpy.data.objects["Pet01_Mesh"]
    rig.animation_data.action = None
    base.reset_pose(rig)
    bpy.context.view_layer.update()
    rest = positions(mesh)
    planted = np.where(rest[:, 2] < .00001)[0]
    snapshots = {}
    for name, (last, loop) in CLIPS.items():
        set_action(rig, name, 1)
        first = positions(mesh)
        previous = first
        root_rest = root_matrix(rig)
        max_ground = 0.0
        max_step = 0.0
        step_evidence = {}
        max_root_drift = 0.0
        sample_frames = sorted(set([1, last // 4 or 1, last // 2 or 1, (3 * last) // 4 or 1, last]))
        snapshots[name] = {}
        for frame in range(1, last + 1):
            bpy.context.scene.frame_set(frame)
            xyz = positions(mesh)
            max_ground = min(max_ground, float(xyz[:, 2].min()))
            steps = np.linalg.norm(xyz - previous, axis=1)
            if float(steps.max()) > max_step:
                max_step = float(steps.max())
                vertex = mesh.data.vertices[int(steps.argmax())]
                step_evidence = {"frame": frame, "vertex": vertex.index}
            # Root bone itself must never translate/rotate: only pelvis/legs/etc move.
            root_now = root_matrix(rig)
            max_root_drift = max(max_root_drift, float(np.abs(root_now - root_rest).max()))
            if frame in sample_frames:
                snapshots[name][frame] = xyz
            previous = xyz
        endpoint_error = float(np.linalg.norm(previous - first, axis=1).max())
        duration = (last - 1) / FPS
        action = bpy.data.actions[name]
        result = {"fps": FPS, "frames": [1, last], "duration_seconds": duration, "loop": loop,
                  "endpoint_max_error_m": endpoint_error, "minimum_z_m": max_ground,
                  "max_consecutive_frame_motion_m": max_step, "root_matrix_max_error": max_root_drift,
                  "largest_step": step_evidence,
                  "design_markers": [{"name": m.name, "frame": m.frame, "time_seconds": (m.frame - 1) / FPS}
                                      for m in action.pose_markers]}
        REPORT["extra_actions"][name] = result
        # Every clip (loop or single-shot) must return to its own start pose, matching
        # how Idle/Eat both close their frame range at the same pose they opened on.
        check(name + "_endpoints", endpoint_error < 1e-6, {"max_error_m": endpoint_error})
        # Root bone must never move: no root motion is baked, matching applyRootMotion=false in Unity.
        check(name + "_root_bone_fixed", max_root_drift < 1e-6, {"root_matrix_error": max_root_drift})
        check(name + "_above_ground", max_ground > -.0005, {"minimum_z_m": max_ground})
        if name != "WalkLoop":
            # Non-locomotion clips keep feet planted like Idle/Eat.
            plant_drift = 0.0
            for frame in range(1, last + 1):
                bpy.context.scene.frame_set(frame)
                xyz = positions(mesh)
                plant_drift = max(plant_drift, float(np.linalg.norm(xyz[planted] - rest[planted], axis=1).max()))
            check(name + "_planted_feet", plant_drift < .0001, {"foot_drift_m": plant_drift})
        check(name + "_smooth_motion", max_step < .010, {"max_step_m": max_step, "largest_step": step_evidence})
    return snapshots


def validate_roundtrip(snapshots):
    for name in CLIPS:
        file = MODEL_DIR / ("Pet01_" + name + ".fbx")
        if not file.exists():
            check(name + "_fbx_exists", False, {"file": str(file)})
            continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.scene.render.fps = FPS
        bpy.ops.import_scene.fbx(filepath=str(file), use_anim=True, use_image_search=False,
                                  ignore_leaf_bones=False, automatic_bone_orientation=False)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
        check(name + "_fbx_object_scope", len(meshes) == 1 and len(rigs) == 1,
              {"meshes": len(meshes), "armatures": len(rigs)})
        if len(meshes) != 1 or len(rigs) != 1:
            continue
        mesh, rig = meshes[0], rigs[0]
        check(name + "_fbx_bones", len(rig.data.bones) == EXPECTED_BONES, {"bones": len(rig.data.bones)})
        take = rig.animation_data.action if rig.animation_data else None
        check(name + "_fbx_take", take is not None, {"take": take.name if take else None})
        if not take:
            continue
        frame_offset = float(take.frame_range[0]) - 1
        errors = []
        for frame, source_points in snapshots[name].items():
            bpy.context.scene.frame_set(int(frame + frame_offset))
            target_points = positions(mesh)
            errors.append({"source_frame": frame, "max_surface_distance_m": cloud_distance(source_points, target_points)})
        maximum = max(e["max_surface_distance_m"] for e in errors)
        check(name + "_fbx_pose_preservation", maximum < .00002, {"maximum_error_m": maximum, "sample_frames": len(errors)})
        REPORT["roundtrip"][name] = {"file": str(file.relative_to(ROOT)).replace("\\", "/"),
                                      "bytes": file.stat().st_size,
                                      "sha256": hashlib.sha256(file.read_bytes()).hexdigest(),
                                      "pose_errors": errors, "take": take.name}


def main():
    global FORCE_REBUILD
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", choices=("build", "export", "validate"), default="build")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    FORCE_REBUILD = args.force
    if args.phase == "build":
        build()
    elif args.phase == "export":
        export()
    else:
        snapshots = validate()
        validate_roundtrip(snapshots)
        REPORT["passed"] = all(c["passed"] for c in REPORT["checks"])
        report_path = SOURCE / "validation_report_extra_actions.json"
        report_path.write_text(json.dumps(REPORT, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        if not REPORT["passed"]:
            raise RuntimeError("Extra-action validation failed; see " + str(report_path))
        print("PET01_EXTRA VALIDATION PASSED", len(REPORT["checks"]), flush=True)


if __name__ == "__main__":
    main()
