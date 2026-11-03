"""Repair the frozen Pet01 arm/belly transition for approved exclusive actions.

The original mesh has contiguous belly and hand vertices. The belly-side hand
vertices were 100% weighted to Hand.L/Hand.R, so a large arm rotation stretched
short cross-boundary edges into visible spikes. This script changes only that
transition band, preserving topology, UVs, rest bones and all existing Actions.
The caller must archive the canonical source before running it.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy

SOURCE = Path(r"D:/ZengQiangXianShi/ArtSource/Characters/Pet01/pet01.blend")
EXPECTED_SHA256 = "8dc3dc92e8d24736b63764b0ab7e702d33c885abadcc4cd6c0ac19026617b67f"
EXPECTED_VERTICES = 10434  # historical 20260922 tool: runs only against the archived 8dc3dc92 source
EXPECTED_BONES = 26


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def connected(mesh):
    adjacency = [[] for _ in mesh.data.vertices]
    for edge in mesh.data.edges:
        a, b = edge.vertices
        adjacency[a].append(b)
        adjacency[b].append(a)
    return adjacency


def replace_transition_weights(mesh):
    adjacency = connected(mesh)
    group_index = {group.name: group.index for group in mesh.vertex_groups}
    changed = {}
    for side in ("L", "R"):
        hand_name = "Hand." + side
        hand_index = group_index[hand_name]
        hand_vertices = {
            vertex.index for vertex in mesh.data.vertices
            if any(weight.group == hand_index and weight.weight > 0.5
                   for weight in vertex.groups)
        }
        central = {
            index for index in hand_vertices
            if abs(mesh.data.vertices[index].co.x) < 0.021
            and mesh.data.vertices[index].co.y > 0.025
            and 0.070 < mesh.data.vertices[index].co.z < 0.130
        }
        boundary = {
            index for index in central
            if any(neighbor not in hand_vertices for neighbor in adjacency[index])
        }
        if not boundary:
            raise RuntimeError("No arm/belly boundary found for " + hand_name)

        # Geodesic distance across the hand-side transition. The narrow band
        # changes smoothly, keeping neighbouring vertices within a small
        # rotation-weight delta while the arm itself keeps its original weights.
        distance = {index: 0 for index in boundary}
        frontier = list(boundary)
        while frontier:
            current = frontier.pop(0)
            for neighbor in adjacency[current]:
                if neighbor not in central or neighbor in distance:
                    continue
                distance[neighbor] = distance[current] + 1
                frontier.append(neighbor)

        for index in central:
            hand_weight = min(0.90, 0.05 + 0.10 * distance.get(index, 0))
            vertex = mesh.data.vertices[index]
            torso = "Chest" if vertex.co.z > 0.112 else "Spine"
            torso_index = group_index[torso]
            mesh.vertex_groups[hand_name].add([index], hand_weight, "REPLACE")
            mesh.vertex_groups[torso].add([index], 1.0 - hand_weight, "REPLACE")
            changed[index] = {
                "side": side,
                "distance": distance.get(index, -1),
                "hand_weight": hand_weight,
                "torso": torso,
            }
    return changed


def main():
    if sha(SOURCE) != EXPECTED_SHA256:
        raise RuntimeError("Canonical source hash changed; archive/review the new baseline first")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    rig = bpy.data.objects["Pet01Rig"]
    mesh = bpy.data.objects["Pet01_Mesh"]
    if len(mesh.data.vertices) != EXPECTED_VERTICES or len(rig.data.bones) != EXPECTED_BONES:
        raise RuntimeError("Frozen topology/skeleton count changed")
    changed = replace_transition_weights(mesh)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    report = {
        "shape_revision": "20260922_arm_belly_weight_transition",
        "source_sha256_before": EXPECTED_SHA256,
        "source_sha256_after": sha(SOURCE),
        "vertices_changed": len(changed),
        "changed_vertices": changed,
        "geometry_counts": {"vertices": len(mesh.data.vertices), "bones": len(rig.data.bones)},
        "note": "Only central belly-side Hand.L/Hand.R transition weights changed; topology, UVs, rest bones and existing actions were preserved.",
    }
    out = SOURCE.parent / "weight_revision_20260922.json"
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("WEIGHT_REVISION", json.dumps({k: report[k] for k in ("shape_revision", "source_sha256_after", "vertices_changed")}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
