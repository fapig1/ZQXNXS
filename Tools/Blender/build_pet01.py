"""Build the project's first editable, skinned Pet01 asset with Blender 5.1.

Run with D:/blender.exe --background --factory-startup --python-exit-code 1
    --python Tools/Blender/build_pet01.py -- --phase build
Other phases: preview, sequence, export. Paths are relative to this repository.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import sys
import zlib
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Characters/Pet01"
PREVIEWS = SOURCE / "Previews"
ART = ROOT / "ARPet/Assets/_Project/Art/Characters/Pet01"
MODEL_DIR = ART / "Models"
TEXTURE_DIR = ART / "Textures"
BLEND = SOURCE / "pet01.blend"
FPS = 30
REFERENCE_BOARD = "ArtSource/Characters/Pet01/Candidates/Meshy_AI_Golden_Guardian_0910135714_generate.glb"
IMAGE_REFERENCE_BOARD = "Docs/AssetReview/20260908_naiwa/expression_frames.jpg"
REFERENCE_FRAMES = [
    "ArtSource/References/20260908_naiwa_assets/video_frames/mfstream_t1.png",
    "ArtSource/References/20260908_naiwa_assets/video_frames/mfstream_t6.png",
]
SHAPE_REVISION = "20260911_meshy_proportions"
# z, half-width, half-depth, front/back center, all in meters.
# The user-selected Meshy candidate is 0.1111 m wide and 0.0992 m deep after
# height normalization to 0.2 m. Match its longitudinal pear silhouette and
# belly-height bent arms. These are authored profiles, not a copied mesh.
BODY_ROWS = [
    (.0510, .0010, .0010, -.0040),
    (.0540, .0150, .0190, -.0040),
    (.0580, .0270, .0290, -.0035),
    (.0650, .0368, .0385, -.0027),
    (.0780, .0456, .0455, -.0025),
    (.0910, .0478, .0473, -.0022),
    (.1040, .0458, .0467, -.0028),
    (.1160, .0412, .0430, -.0035),
    (.1280, .0355, .0370, -.0045),
    (.1400, .0300, .0305, -.0050),
    (.1500, .0275, .0278, -.0055),
    (.1600, .0249, .0250, -.0040),
    (.1700, .0227, .0232,  .0003),
    (.1775, .0219, .0230,  .0030),
    (.1840, .0206, .0228,  .0060),
    (.1910, .0180, .0205,  .0066),
    (.1955, .0135, .0154,  .0069),
    (.1985, .0085, .0095,  .0068),
    (.1997, .0035, .0039,  .0068),
    (.2000, .0001, .0001,  .0068),
]
MOUTH_BOTTOM = .17730
MOUTH_TOP = .17785
MOUTH_CENTER = (MOUTH_BOTTOM+MOUTH_TOP)/2
SKIN_JOIN_LOWER = .165
SKIN_JOIN_UPPER = .167
N_BODY = 64
MOUTH_HALF_COLUMNS = 5
MOUTH_SPAN = MOUTH_HALF_COLUMNS * math.tau / N_BODY
BODY = "Pet01_Skin"
EYES = "Pet01_Eyes"
ORAL = "Pet01_Oral"
COMPONENTS = []
MATERIALS = {}
COLLECTION = None


def log(message):
    print("PET01 " + message, flush=True)


def smoothstep(a, b, value):
    t = max(0.0, min(1.0, (value - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def interp_profile(z):
    for i in range(len(BODY_ROWS) - 1):
        a, b = BODY_ROWS[i], BODY_ROWS[i + 1]
        if z <= b[0]:
            t = max(0.0, (z - a[0]) / (b[0] - a[0]))
            result = []
            for j in range(1, 4):
                if i:
                    slope_a = (b[j] - BODY_ROWS[i - 1][j]) / (b[0] - BODY_ROWS[i - 1][0])
                else:
                    slope_a = (b[j] - a[j]) / (b[0] - a[0])
                if i + 2 < len(BODY_ROWS):
                    c = BODY_ROWS[i + 2]
                    slope_b = (c[j] - a[j]) / (c[0] - a[0])
                else:
                    slope_b = (b[j] - a[j]) / (b[0] - a[0])
                h = b[0] - a[0]
                result.append(
                    (2*t**3-3*t*t+1)*a[j] + (t**3-2*t*t+t)*slope_a*h
                    + (-2*t**3+3*t*t)*b[j] + (t**3-t*t)*slope_b*h
                )
            return result
    return list(BODY_ROWS[-1][1:])


def rgb(hex_color):
    return np.array([int(hex_color[i:i+2], 16) for i in (0, 2, 4)], dtype=float)


def write_png(path, pixels):
    """A deterministic RGB PNG writer; no external Pillow dependency in Blender."""
    h, w, channels = pixels.shape
    assert channels == 3
    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xffffffff)
    raw = b"".join(b"\0" + row.tobytes() for row in pixels.astype(np.uint8))
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n" +
        chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) +
        chunk(b"sRGB", b"\0") +
        chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
    )


def make_texture():
    size = 2048
    u, v = np.meshgrid((np.arange(size)+.5)/size, 1-(np.arange(size)+.5)/size)
    yellow, cream, olive = rgb("F7C34A"), rgb("F8EACD"), rgb("625137")
    atlas = np.broadcast_to(yellow, (size, size, 3)).copy()
    theta = (u / .5 - .5) * math.tau
    z = v * .205
    radius = np.interp(z, [p[0] for p in BODY_ROWS], [p[1] for p in BODY_ROWS])
    x = np.sin(theta) * radius
    ellipse = (x/.0365)**2 + ((z-.107)/.0390)**2
    border = np.clip((1.02-ellipse)/.045, 0, 1)
    border = border*border*(3-2*border)
    border *= (np.cos(theta) > .28) & (u < .5)
    atlas = atlas*(1-border[..., None]) + cream*border[..., None]
    ramp = np.clip((u-.52)/.44, 0, 1)
    ramp = ramp*ramp*(3-2*ramp)
    ramp_area = (u >= .5) & (v >= .5)
    gradient = yellow[None, None, :]*(1-ramp[..., None]) + olive*ramp[..., None]
    atlas[ramp_area] = gradient[ramp_area]
    iris_area = (u >= .5) & (u < .75) & (v >= .25) & (v < .5)
    ix, iy = (u-.625)/.108, (v-.375)/.108
    radius_i = np.sqrt(ix*ix + iy*iy)
    ring = np.clip((radius_i-.75)/.3, 0, 1)
    iris = rgb("92B950")[None, None, :]*(1-ring[..., None]) + rgb("5F923F")*ring[..., None]
    fiber = 1 + .018*np.sin(np.arctan2(iy, ix)*58 + radius_i*9)
    iris *= fiber[..., None]
    atlas[iris_area] = iris[iris_area]
    atlas[(u >= .75) & (u < .86) & (v >= .25) & (v < .5)] = rgb("101816")
    atlas[(u >= .86) & (v >= .25) & (v < .5)] = rgb("FFFBEF")
    atlas[(u >= .5) & (u < .69) & (v < .25)] = rgb("471B1C")
    atlas[(u >= .69) & (u < .82) & (v < .25)] = rgb("DB777B")
    atlas[(u >= .82) & (v < .25)] = rgb("FFF3DA")
    out = SOURCE / "Textures/Pet01_BaseColor.png"
    write_png(out, np.clip(atlas, 0, 255).astype(np.uint8))
    (TEXTURE_DIR / out.name).write_bytes(out.read_bytes())
    return out


def setup_materials(texture_path):
    image = bpy.data.images.load(str(texture_path), check_existing=True)
    image.name = "Pet01_BaseColor"
    image.colorspace_settings.name = "sRGB"
    for name, roughness in ((BODY, .57), (EYES, .36), (ORAL, .43)):
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        material.diffuse_color = (1, .68, .15, 1) if name == BODY else (.2, .2, .2, 1)
        bsdf = material.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = 0
        bsdf.inputs["Specular IOR Level"].default_value = .25 if name == BODY else .32
        node = material.node_tree.nodes.new("ShaderNodeTexImage")
        node.name = "Pet01 Base Color"
        node.image = image
        node.interpolation = "Linear"
        material.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        MATERIALS[name] = material


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def move_to_collection(obj):
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    COLLECTION.objects.link(obj)


def mesh_object(name, verts, faces, material=BODY, uv_function=None, weight_function=None):
    mesh = bpy.data.meshes.new(name + "_Geometry")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    COLLECTION.objects.link(obj)
    mesh.materials.append(MATERIALS[material])
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    if uv_function:
        layer = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            layer.data[loop.index].uv = uv_function(mesh.vertices[loop.vertex_index].co)
    if weight_function:
        assign_weights(obj, weight_function)
    COMPONENTS.append(obj)
    return obj


def assign_weights(obj, fn):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)
    groups = {}
    for vertex in obj.data.vertices:
        weights = {key: float(value) for key, value in fn(vertex.co).items() if value > 1e-6}
        total = sum(weights.values())
        if not total:
            raise ValueError("Unweighted vertex: " + obj.name)
        for name, value in weights.items():
            if name not in groups:
                groups[name] = obj.vertex_groups.new(name=name)
            groups[name].add([vertex.index], value/total, "REPLACE")


def body_weights(z, theta):
    if z < .075:
        t = smoothstep(.054, .086, z)
        result = {"Pelvis": 1-t, "Spine": t}
    elif z < .132:
        t = smoothstep(.093, .142, z)
        result = {"Spine": 1-t, "Chest": t}
    else:
        t = smoothstep(.145, .173, z)
        result = {"Chest": 1-t, "Head": t}
    angular = max(0, math.cos(min(abs(theta)/MOUTH_SPAN, 1)*math.pi/2)) ** .72
    jaw = None
    if .162 < z <= MOUTH_BOTTOM + 1e-8:
        amount = smoothstep(.162, MOUTH_BOTTOM, z) * angular
        jaw = "MouthLower"
    elif MOUTH_TOP-1e-8 <= z < .194:
        amount = (1-smoothstep(MOUTH_TOP, .194, z)) * angular
        jaw = "MouthUpper"
    if jaw:
        result = {k: w*(1-amount) for k, w in result.items()}
        result[jaw] = amount
    return result


def make_body():
    z_values = list(np.arange(BODY_ROWS[0][0], .199, .0022))
    z_values = [float(z) for z in z_values if abs(z-MOUTH_CENTER) > .0013]
    z_values += [.150, SKIN_JOIN_UPPER, .1980, .1990, .1995, .1998, MOUTH_BOTTOM, MOUTH_TOP, .200]
    z_values = sorted(set(z_values))
    mouth_row = z_values.index(MOUTH_BOTTOM)
    verts, faces, uv_faces, weights = [], [], [], []
    for z in z_values:
        rx, ry, cy = interp_profile(z)
        for j in range(N_BODY):
            theta = (j/N_BODY-.5)*math.tau
            actual_z = z
            if z in (MOUTH_BOTTOM, MOUTH_TOP) and abs(theta) <= MOUTH_SPAN+.01:
                actual_z += .0009*(abs(theta)/MOUTH_SPAN)**1.7
            lip = .0014*math.exp(-((z-MOUTH_CENTER)/.0030)**2)*max(0,math.cos(theta))**8
            verts.append((rx*math.sin(theta), cy+ry*math.cos(theta)+lip, actual_z))
            weights.append(body_weights(z, theta))
    for row in range(len(z_values)-1):
        for j in range(N_BODY):
            if row == mouth_row and N_BODY//2-MOUTH_HALF_COLUMNS <= j < N_BODY//2+MOUTH_HALF_COLUMNS:
                continue
            j2 = (j+1) % N_BODY
            faces.append((row*N_BODY+j, row*N_BODY+j2, (row+1)*N_BODY+j2, (row+1)*N_BODY+j))
            uv_faces.append(((.5*j/N_BODY, z_values[row]/.205), (.5*(j+1)/N_BODY, z_values[row]/.205),
                             (.5*(j+1)/N_BODY, z_values[row+1]/.205), (.5*j/N_BODY, z_values[row+1]/.205)))
    bottom = len(verts)
    verts.append((0, BODY_ROWS[0][3], BODY_ROWS[0][0]))
    weights.append({"Pelvis": 1})
    top = len(verts)
    verts.append((0, BODY_ROWS[-1][3], .200))
    weights.append({"Head": 1})
    for j in range(N_BODY):
        faces.append((bottom, (j+1)%N_BODY, j))
        uv_faces.append(((.01, .24),)*3)
        faces.append((top, (len(z_values)-1)*N_BODY+j, (len(z_values)-1)*N_BODY+(j+1)%N_BODY))
        uv_faces.append(((.01, .975),)*3)
    obj = mesh_object("Body", verts, faces)
    layer = obj.data.uv_layers.new(name="UVMap")
    for polygon, coords in zip(obj.data.polygons, uv_faces):
        for loop_index, uv in zip(polygon.loop_indices, coords):
            layer.data[loop_index].uv = uv
    groups = {}
    for index, weights_i in enumerate(weights):
        for name, value in weights_i.items():
            if value <= 1e-6:
                continue
            if name not in groups:
                groups[name] = obj.vertex_groups.new(name=name)
            groups[name].add([index], value, "REPLACE")
    left, right = N_BODY//2-MOUTH_HALF_COLUMNS, N_BODY//2+MOUTH_HALF_COLUMNS
    rim_indices = [mouth_row*N_BODY+j for j in range(left, right+1)]
    rim_indices += [(mouth_row+1)*N_BODY+j for j in range(right, left-1, -1)]
    make_mouth([Vector(verts[i]) for i in rim_indices], [weights[i] for i in rim_indices])
    return obj


def make_mouth(rim, rim_weights):
    verts, faces, weights = [], [], []
    count = len(rim)
    for depth, factor in ((0, 1), (.004, .90), (.013, .60)):
        for p, w in zip(rim, rim_weights):
            verts.append((p.x*factor, p.y-depth, MOUTH_CENTER+(p.z-MOUTH_CENTER)*factor))
            weights.append(w)
    for r in range(2):
        for j in range(count):
            faces.append((r*count+j, r*count+(j+1)%count, (r+1)*count+(j+1)%count, (r+1)*count+j))
    faces.append(tuple(range(2*count, 3*count)))
    obj = mesh_object("MouthCavity", verts, faces, ORAL, lambda p: (.59, .125))
    groups = {}
    for i, ws in enumerate(weights):
        for name, weight in ws.items():
            if weight > 1e-6:
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)
                groups[name].add([i], weight, "REPLACE")
    for i in range(6):
        x = (i-2.5)*.0024
        ellipsoid("Tooth_%02d" % i, (x, .0237-.00025*abs(i-2.5), MOUTH_TOP+.0012),
                  (.00120, .00100, .0009), ORAL, (.9, .125), {"Head": 1}, segments=12, rings=8)
    ellipsoid("Tongue", (0, .0220, MOUTH_BOTTOM-.0003), (.0045, .0030, .0010), ORAL, (.755, .125),
              {"MouthLower": .65, "Head": .35}, segments=20, rings=10)


def ellipsoid(name, center, scale, material=BODY, uv=(.522, .75), weights=None, segments=24, rings=16):
    verts, faces = [], []
    for r in range(rings+1):
        phi = -.5*math.pi + (r+.00001)/(rings+.00002)*math.pi
        for j in range(segments):
            theta = math.tau*j/segments
            verts.append((center[0]+scale[0]*math.cos(phi)*math.cos(theta),
                          center[1]+scale[1]*math.cos(phi)*math.sin(theta),
                          center[2]+scale[2]*math.sin(phi)))
    for r in range(rings):
        for j in range(segments):
            faces.append((r*segments+j, r*segments+(j+1)%segments, (r+1)*segments+(j+1)%segments, (r+1)*segments+j))
    faces += [tuple(reversed(range(segments))), tuple(range(rings*segments, (rings+1)*segments))]
    return mesh_object(name, verts, faces, material, lambda p: uv,
                       (lambda p: weights) if weights else None)


def make_tube(name, controls, sides=20, steps_per_segment=5):
    points = []
    control = [np.array(p, dtype=float) for p in controls]
    for i in range(len(control)-1):
        p0, p1, p2, p3 = control[max(i-1, 0)], control[i], control[i+1], control[min(i+2, len(control)-1)]
        for k in range(steps_per_segment):
            t = k/steps_per_segment
            points.append(.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))
    points.append(control[-1])
    verts, faces = [], []
    for i, row in enumerate(points):
        tangent = Vector(points[min(i+1, len(points)-1)][:3] - points[max(i-1, 0)][:3]).normalized()
        anchor = Vector((1, 0, 0)) if abs(tangent.x) < .9 else Vector((0, 1, 0))
        side = tangent.cross(anchor).normalized()
        up = tangent.cross(side).normalized()
        for j in range(sides):
            t = math.tau*j/sides
            p = Vector(row[:3]) + (math.cos(t)*side + math.sin(t)*up)*max(float(row[3]), .0002)
            verts.append(tuple(p))
    for i in range(len(points)-1):
        for j in range(sides):
            faces.append((i*sides+j, i*sides+(j+1)%sides, (i+1)*sides+(j+1)%sides, (i+1)*sides+j))
    faces += [tuple(reversed(range(sides))), tuple(range((len(points)-1)*sides, len(points)*sides))]
    return mesh_object(name, verts, faces)


def union_parts(name, parts, voxel=.00072, ratio=.42):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
        if obj in COMPONENTS:
            COMPONENTS.remove(obj)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = parts[0]
    obj.name = name
    obj.data.remesh_voxel_size = voxel
    obj.data.use_remesh_preserve_volume = True
    bpy.ops.object.voxel_remesh()
    mod = obj.modifiers.new("GentleSurfaceRelax", "SMOOTH")
    mod.factor, mod.iterations = .7, 4
    bpy.ops.object.modifier_apply(modifier=mod.name)
    mod = obj.modifiers.new("MobileTopology", "DECIMATE")
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials.clear()
    obj.data.materials.append(MATERIALS[BODY])
    for face in obj.data.polygons:
        face.use_smooth = True
    COMPONENTS.append(obj)
    return obj


def assign_uv(obj, function):
    layer = obj.data.uv_layers.active or obj.data.uv_layers.new(name="UVMap")
    layer.name = "UVMap"
    for loop in obj.data.loops:
        layer.data[loop.index].uv = function(obj.data.vertices[loop.vertex_index].co)


def make_arm(side):
    sign, suffix = side, "L" if side > 0 else "R"
    def xyz(x, y, z):
        return (sign*x, y, z)
    controls = [(*xyz(.010, -.006, .158), .0070),
                (*xyz(.018, -.004, .151), .0105),
                (*xyz(.028, -.002, .141), .0135),
                (*xyz(.039,  .005, .128), .0125),
                (*xyz(.0445, .021, .116), .0110),
                (*xyz(.034, .0385, .108), .0100),
                (*xyz(.022, .0465, .103), .0060),
                (*xyz(.018,  .048, .102), .0040)]
    parts = [make_tube("ArmSurface."+suffix, controls)]
    parts.append(ellipsoid("Palm."+suffix, xyz(.0175, .0475, .102), (.0085, .0042, .0080)))
    for i, z in enumerate((.1040, .0990, .0942)):
        parts.append(ellipsoid("Finger%d.%s" % (i, suffix), xyz(.009+(i==2)*.0015, .0485, z),
                               (.0068, .0031, .0022), segments=20, rings=12))
    parts.append(ellipsoid("Thumb."+suffix, xyz(.0155, .0475, .1110),
                           (.0056, .0033, .0030), segments=20, rings=12))
    obj = union_parts("Arm."+suffix, parts, voxel=.00063, ratio=.44)
    def weights(p):
        hand = (1-smoothstep(.018, .030, abs(p.x)))*(1-smoothstep(.114,.134,p.z))
        upper = smoothstep(.110, .134, p.z)*(1-hand)
        lower = (1-hand)-upper
        chest = .95*smoothstep(.139, .157, p.z)*(1-smoothstep(.018,.037,abs(p.x)))
        return {"Hand."+suffix: hand*(1-chest), "Forearm."+suffix: lower*(1-chest),
                "UpperArm."+suffix: upper*(1-chest), "Chest": chest}
    assign_weights(obj, weights)
    assign_uv(obj, arm_uv)


def arm_uv(p):
    brown=(1-smoothstep(.018,.030,abs(p.x)))*(1-smoothstep(.113,.125,p.z))
    return (.522+.435*brown,.75)


def foot_uv(p):
    return (.522+.435*(1-smoothstep(.009,.021,p.z)),.75)


def make_leg(side):
    suffix = "L" if side > 0 else "R"
    def xyz(x, y, z):
        return (side*x, y, z)
    controls = [(*xyz(.022, -.0080, .088), .0140),
                (*xyz(.026, -.0005, .067), .0165),
                (*xyz(.028, -.0015, .050), .0145),
                (*xyz(.0275,-.0045, .034), .0108),
                (*xyz(.0265,-.0090, .015), .0048),
                (*xyz(.026, -.008, .008), .0045)]
    parts = [make_tube("LegSurface."+suffix, controls)]
    feet = [ellipsoid("Foot."+suffix, (0,.0065,.0050), (.0105,.0185,.0048))]
    for i, offset in enumerate((-.0075, .000, .0075)):
        feet.append(ellipsoid("Toe%d.%s" % (i, suffix), (offset,.027+(0.002 if i==1 else 0),.0036),
                              (.0043,.0085,.0035), segments=16, rings=10))
    angle=math.radians(35)
    for part in feet:
        for v in part.data.vertices:
            x,y,z=v.co
            v.co=(side*(.026+math.cos(angle)*x+math.sin(angle)*y),
                  -.010-math.sin(angle)*x+math.cos(angle)*y,z)
    parts.extend(feet)
    obj = union_parts("Leg."+suffix, parts, voxel=.00060, ratio=.43)
    for v in obj.data.vertices:
        v.co.z = max(.00015, v.co.z)
    def weights(p):
        foot = 1-smoothstep(.012, .022, p.z)
        thigh = smoothstep(.032, .050, p.z)*(1-foot)
        shin = 1-foot-thigh
        pelvis = .80*smoothstep(.058, .074, p.z)
        return {"Foot."+suffix: foot*(1-pelvis), "Shin."+suffix: shin*(1-pelvis),
                "Thigh."+suffix: thigh*(1-pelvis), "Pelvis": pelvis}
    assign_weights(obj, weights)
    assign_uv(obj, foot_uv)


def make_tail():
    obj = make_tube("Tail", [(0,-.030,.087,.0100), (0,-.042,.082,.0070),
                            (0,-.047,.083,.0035), (0,-.050,.087,.0006)], sides=24)
    assign_weights(obj, lambda p: {"Pelvis": 1-smoothstep(-.035,-.050,p.y), "Tail": smoothstep(-.035,-.050,p.y)})
    assign_uv(obj, lambda p: (.522, .75))


def unify_skin():
    """Weld the shoulders/hips; retain the authored face and mouth edge loops.

    Only the lower skin is voxel remeshed. The original surface supplies
    normalized weights through a nearest-triangle barycentric transfer.
    Its part labels select separate UV regions without crossing atlas tiles.
    A stitched collar joins it to the unchanged facial topology.
    """
    body = next(o for o in COMPONENTS if o.name == "Body")
    head = body.copy()
    head.data = body.data.copy()
    head.name = "FacialTopology"
    COLLECTION.objects.link(head)
    bm = bmesh.new()
    bm.from_mesh(head.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < SKIN_JOIN_UPPER-1e-7], context="VERTS")
    bm.to_mesh(head.data)
    bm.free()

    parts = [o for o in COMPONENTS if o.name in ("Body", "Arm.L", "Arm.R", "Leg.L", "Leg.R", "Tail")]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        region = 1 if obj.name.startswith("Arm.") else 2 if obj.name.startswith("Leg.") else 3 if obj.name=="Tail" else 0
        attribute = obj.data.attributes.new(name="pet01_region",type="INT",domain="FACE")
        for item in attribute.data:
            item.value = region
        obj.select_set(True)
        COMPONENTS.remove(obj)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    reference = bpy.context.object
    reference.name = "TemporarySkinTransferSource"
    bm = bmesh.new()
    bm.from_mesh(reference.data)
    # Volume remeshing needs outward, closed component surfaces. The narrow
    # mouth opening only belongs to the retained head, not this volume copy.
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(reference.data)
    bm.free()
    reference.data.calc_loop_triangles()
    source_positions = [v.co.copy() for v in reference.data.vertices]
    source_weights = [{reference.vertex_groups[g.group].name:g.weight for g in v.groups}
                      for v in reference.data.vertices]
    source_triangles = [tuple(t.vertices) for t in reference.data.loop_triangles]
    source_regions = [reference.data.attributes["pet01_region"].data[t.polygon_index].value
                      for t in reference.data.loop_triangles]
    tree = BVHTree.FromPolygons(source_positions, source_triangles, all_triangles=True)

    lower = reference.copy()
    lower.data = reference.data.copy()
    lower.name = "Body"
    COLLECTION.objects.link(lower)
    activate(lower)
    lower.data.remesh_voxel_size = .00068
    lower.data.use_remesh_preserve_volume = True
    bpy.ops.object.voxel_remesh()
    relax = lower.modifiers.new("SmoothShoulderHipJoins", "SMOOTH")
    relax.factor, relax.iterations = .65, 7
    bpy.ops.object.modifier_apply(modifier=relax.name)
    shoulder_mask=lower.vertex_groups.new(name="TemporaryShoulderRelax")
    for vertex in lower.data.vertices:
        p=vertex.co
        weight=smoothstep(.129,.138,p.z)*(1-smoothstep(.155,.165,p.z))*smoothstep(.013,.024,abs(p.x))
        if weight>1e-5:
            shoulder_mask.add([vertex.index],weight,"REPLACE")
    relax=lower.modifiers.new("RoundShoulderTransitions","SMOOTH")
    relax.vertex_group=shoulder_mask.name
    relax.factor,relax.iterations=.70,100
    bpy.ops.object.modifier_apply(modifier=relax.name)
    bm = bmesh.new()
    bm.from_mesh(lower.data)
    # Keep the entire shoulder union below the collar. Cutting through the
    # shoulder caps pulls their boundary onto the neck ellipse and makes notches.
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                          dist=1e-7, plane_co=(0,0,SKIN_JOIN_LOWER), plane_no=(0,0,1), clear_outer=True)
    bm.to_mesh(lower.data)
    bm.free()
    lower.data.calc_loop_triangles()
    decimate = lower.modifiers.new("MobileSkinBudget", "DECIMATE")
    decimate.ratio = min(1, 11800/max(1,len(lower.data.loop_triangles)))
    decimate.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    # Decimation can move open boundary vertices slightly off the cut plane.
    # Recover the complete boundary, then project it onto the design profile.
    bm = bmesh.new()
    bm.from_mesh(lower.data)
    rim = [v for v in bm.verts if v.is_boundary]
    if not rim or any(v.co.z < SKIN_JOIN_LOWER-.008 for v in rim):
        raise RuntimeError("Unexpected lower-skin opening before collar stitching")
    rx, ry, cy = interp_profile(SKIN_JOIN_LOWER)
    for v in rim:
        theta = math.atan2(v.co.x/rx,(v.co.y-cy)/ry)
        v.co = (rx*math.sin(theta),cy+ry*math.cos(theta),SKIN_JOIN_LOWER)
    bm.to_mesh(lower.data)
    bm.free()

    for group in list(lower.vertex_groups):
        lower.vertex_groups.remove(group)
    groups = {}
    for vertex in lower.data.vertices:
        if vertex.co.z < .0007:
            vertex.co.z = 0.0
        point, normal, tri, distance = tree.find_nearest(vertex.co)
        indices = source_triangles[tri]
        a, b, c = (source_positions[i] for i in indices)
        ab, ac, ap = b-a, c-a, point-a
        d00, d01, d11 = ab.dot(ab), ab.dot(ac), ac.dot(ac)
        denominator = d00*d11-d01*d01
        if abs(denominator) < 1e-22:
            factors = (1.0,0.0,0.0)
        else:
            wb = (d11*ap.dot(ab)-d01*ap.dot(ac))/denominator
            wc = (d00*ap.dot(ac)-d01*ap.dot(ab))/denominator
            factors = np.clip([1-wb-wc,wb,wc],0,1)
            factors = factors/sum(factors)
        weights = {}
        for index, factor in zip(indices, factors):
            for name, weight in source_weights[index].items():
                weights[name] = weights.get(name,0.0)+weight*float(factor)
        weights = dict(sorted(((k,v) for k,v in weights.items() if v>1e-5),
                              key=lambda pair:pair[1],reverse=True)[:4])
        total = sum(weights.values())
        if total < 1e-8:
            raise RuntimeError("Missing transferred skin weight")
        for name, weight in weights.items():
            if weight > 1e-5:
                if name not in groups:
                    groups[name] = lower.vertex_groups.new(name=name)
                groups[name].add([vertex.index],weight/total,"REPLACE")
    layer = lower.data.uv_layers.active or lower.data.uv_layers.new(name="UVMap")
    layer.name = "UVMap"
    for polygon in lower.data.polygons:
        polygon.use_smooth = True
        center = polygon.center
        region = source_regions[tree.find_nearest(center)[2]]
        for loop_index in polygon.loop_indices:
            p = lower.data.vertices[lower.data.loops[loop_index].vertex_index].co
            if region==1:
                uv = arm_uv(p)
            elif region==2:
                uv = foot_uv(p)
            elif region==3 or center.y < interp_profile(center.z)[2]:
                uv = (.522,.75)
            else:
                radius = max(interp_profile(p.z)[0],.001)
                theta = math.asin(max(-1,min(1,p.x/radius)))
                uv = (.25+.5*theta/math.tau,p.z/.205)
            layer.data[loop_index].uv = uv
    bpy.data.objects.remove(reference,do_unlink=True)

    activate(lower)
    head.select_set(True)
    bpy.ops.object.join()
    bm = bmesh.new()
    bm.from_mesh(lower.data)
    bottom_center = interp_profile(SKIN_JOIN_LOWER)[2]
    top_center = interp_profile(SKIN_JOIN_UPPER)[2]
    bottom_ring = sorted([v for v in bm.verts if abs(v.co.z-SKIN_JOIN_LOWER)<2e-6 and v.is_boundary],
                         key=lambda v:math.atan2(v.co.x,v.co.y-bottom_center))
    top_ring = sorted([v for v in bm.verts if abs(v.co.z-SKIN_JOIN_UPPER)<1e-7 and v.is_boundary],
                      key=lambda v:math.atan2(v.co.x,v.co.y-top_center))
    if len(bottom_ring)<8 or len(top_ring)!=N_BODY:
        raise RuntimeError("Cannot stitch neck loops: %d, %d" % (len(bottom_ring),len(top_ring)))
    uv_layer = bm.loops.layers.uv.active
    n, m, i, j = len(bottom_ring), len(top_ring), 0, 0
    bottom_angles = [math.atan2(v.co.x,v.co.y-bottom_center) for v in bottom_ring]
    top_angles = [math.atan2(v.co.x,v.co.y-top_center) for v in top_ring]
    while i<n or j<m:
        # Use actual polar angles; uniform index spacing twists an irregular
        # remeshed ring and can fold the collar triangles across one another.
        a, b = bottom_ring[i%n], top_ring[j%m]
        next_bottom = bottom_angles[(i+1)%n] + (math.tau if i+1>=n else 0)
        next_top = top_angles[(j+1)%m] + (math.tau if j+1>=m else 0)
        if j==m or (i<n and next_bottom <= next_top):
            face = bm.faces.new((a,b,bottom_ring[(i+1)%n]))
            i += 1
        else:
            face = bm.faces.new((a,b,top_ring[(j+1)%m]))
            j += 1
        face.smooth = True
        for loop in face.loops:
            loop[uv_layer].uv = (.522,.75)
    collar = [v for v in bm.verts if SKIN_JOIN_LOWER-.003 < v.co.z < SKIN_JOIN_UPPER+.004]
    for _ in range(4):
        bmesh.ops.smooth_vert(bm, verts=collar, factor=.25, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(lower.data)
    bm.free()
    lower.name = "Body"
    COMPONENTS.append(lower)
    bm = bmesh.new()
    bm.from_mesh(lower.data)
    if any(e.is_boundary and SKIN_JOIN_LOWER-.008 < e.verts[0].co.z < SKIN_JOIN_UPPER+.004 for e in bm.edges):
        raise RuntimeError("Collar is not watertight after stitching")
    bm.free()
    log("Unified skin: %d vertices; original facial edge loops retained" % len(lower.data.vertices))


EYE_DATA = {}


def make_eye(side):
    suffix = "L" if side > 0 else "R"
    center = Vector((side*.0131, .0211, .1856))
    normal = Vector((side*.40, .916, .025)).normalized()
    horizontal = Vector((normal.y, -normal.x, 0)).normalized()
    vertical = horizontal.cross(normal).normalized()
    radius = .00550
    EYE_DATA[suffix] = (center, normal, horizontal, vertical)
    verts, faces, uvs = [], [], []
    segments, rings = 32, 12
    for row in range(rings+1):
        angle = .001+(math.pi-.002)*row/rings
        for j in range(segments):
            theta = j/segments*math.tau
            local_x, local_z = math.sin(angle)*math.cos(theta), math.sin(angle)*math.sin(theta)
            p = center + radius*(math.cos(angle)*normal + local_x*horizontal + local_z*vertical)
            verts.append(tuple(p))
            uvs.append((.625+.107*local_x, .375+.107*local_z))
    for row in range(rings):
        for j in range(segments):
            faces.append((row*segments+j,row*segments+(j+1)%segments,(row+1)*segments+(j+1)%segments,(row+1)*segments+j))
    faces += [tuple(reversed(range(segments))),tuple(range(rings*segments,(rings+1)*segments))]
    obj = mesh_object("Eye."+suffix, verts, faces, EYES, weight_function=lambda p: {"Eye."+suffix:1})
    layer = obj.data.uv_layers.new(name="UVMap")
    for loop in obj.data.loops:
        layer.data[loop.index].uv = uvs[loop.vertex_index]
    verts, faces = [], []
    for row in range(7):
        angle = .001+(.69-.001)*row/6
        for j in range(32):
            t = j/32*math.tau
            p = center + (radius+.00009)*(math.cos(angle)*normal+math.sin(angle)*(math.cos(t)*horizontal+math.sin(t)*vertical))
            verts.append(tuple(p))
    for row in range(6):
        for j in range(32):
            faces.append((row*32+j,row*32+(j+1)%32,(row+1)*32+(j+1)%32,(row+1)*32+j))
    faces.append(tuple(reversed(range(32))))
    mesh_object("Pupil."+suffix, verts, faces, EYES, lambda p:(.8,.375),lambda p:{"Eye."+suffix:1})
    for upper in (True, False):
        verts, faces = [], []
        opening = math.radians(78 if upper else -78)
        rotation = Quaternion(horizontal, opening)
        for row in range(9):
            phi = (.0001+(math.pi/2-.0002)*row/8)*(1 if upper else -1)
            for j in range(32):
                theta = j/32*math.tau
                direction = math.cos(phi)*(math.cos(theta)*horizontal+math.sin(theta)*normal)+math.sin(phi)*vertical
                p = center + rotation @ (direction*(radius+.00019))
                verts.append(tuple(p))
        for row in range(8):
            for j in range(32):
                faces.append((row*32+j,row*32+(j+1)%32,(row+1)*32+(j+1)%32,(row+1)*32+j))
        bone = ("LidUpper." if upper else "LidLower.")+suffix
        mesh_object(bone, verts, faces, BODY, lambda p:(.522,.75), lambda p,b=bone:{b:1})


def make_rig():
    armature = bpy.data.armatures.new("Pet01_Skeleton")
    rig = bpy.data.objects.new("Pet01Rig", armature)
    COLLECTION.objects.link(rig)
    activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    def bone(name, head, tail, parent=None):
        edit = armature.edit_bones.new(name)
        edit.head, edit.tail = head, tail
        edit.use_deform = True
        if parent:
            edit.parent = armature.edit_bones[parent]
        return edit
    bone("Root",(0,0,0),(0,0,.024))
    bone("Pelvis",(0,-.008,.056),(0,-.008,.090),"Root")
    bone("Spine",(0,-.008,.090),(0,-.007,.127),"Pelvis")
    bone("Chest",(0,-.007,.127),(0,-.004,.162),"Spine")
    bone("Head",(0,-.004,.162),(0,.006,.195),"Chest")
    bone("Tail",(0,-.035,.084),(0,-.050,.087),"Pelvis")
    bone("MouthLower",(0,.022,MOUTH_BOTTOM),(0,.022,MOUTH_BOTTOM+.009),"Head")
    bone("MouthUpper",(0,.022,MOUTH_TOP),(0,.022,MOUTH_TOP+.009),"Head")
    for sign, suffix in ((1,"L"),(-1,"R")):
        bone("UpperArm."+suffix,(sign*.028,-.002,.143),(sign*.0445,.021,.116),"Chest")
        bone("Forearm."+suffix,(sign*.0445,.021,.116),(sign*.022,.0465,.103),"UpperArm."+suffix)
        bone("Hand."+suffix,(sign*.022,.0465,.103),(sign*.007,.0485,.100),"Forearm."+suffix)
        bone("Thigh."+suffix,(sign*.027,-.0005,.064),(sign*.0275,-.0045,.034),"Pelvis")
        bone("Shin."+suffix,(sign*.0275,-.0045,.034),(sign*.026,-.010,.010),"Thigh."+suffix)
        bone("Foot."+suffix,(sign*.026,-.010,.010),(sign*.043,.015,.004),"Shin."+suffix)
        center, normal, horizontal, vertical = EYE_DATA[suffix]
        bone("Eye."+suffix,center,center+normal*.008,"Head")
        bone("LidUpper."+suffix,center,center+horizontal*.008,"Head")
        bone("LidLower."+suffix,center,center+horizontal*.008,"Head")
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
    rig.show_in_front = True
    for name,prefixes,palette in (
        ("01 Body",("Root","Pelvis","Spine","Chest","Head","Tail"),"THEME04"),
        ("02 Arms",("UpperArm.","Forearm.","Hand."),"THEME03"),
        ("03 Legs",("Thigh.","Shin.","Foot."),"THEME05"),
        ("04 Face",("Eye.","LidUpper.","LidLower.","Mouth"),"THEME02"),
    ):
        group=armature.collections.new(name)
        for b in armature.bones:
            if any(b.name==p or b.name.startswith(p) for p in prefixes):
                group.assign(b)
                b.color.palette=palette
    rig["asset_id"] = "Pet01"
    rig["reference"] = REFERENCE_BOARD + ", user confirmed 2026-09-10"
    rig["shape_revision"] = SHAPE_REVISION
    rig["units"] = "meters; Blender +Y forward, +Z up"
    return rig


def join_and_bind(rig):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in COMPONENTS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next(o for o in COMPONENTS if o.name == "Body")
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = "Pet01_Mesh"
    mesh.data.name = "Pet01_Mesh"
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    # Join the mouth rim to its matching cavity rim (coincident vertices).
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-7)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh.data)
    bm.free()
    mesh.parent = rig
    mod = mesh.modifiers.new("Pet01 Skinning", "ARMATURE")
    mod.object = rig
    mod.use_deform_preserve_volume = False
    mesh["asset_id"] = "Pet01"
    mesh["base_color_atlas"] = "Pet01_BaseColor.png"
    mesh["note"] = "Facial animation uses skeletal eyelids and mouth controls."
    return mesh


def reset_pose(rig):
    for bone in rig.pose.bones:
        bone.location = (0,0,0)
        bone.rotation_euler = (0,0,0)
        bone.scale = (1,1,1)


def blink(t, center, width=.15):
    x = abs(t-center)/width
    return math.cos(x*math.pi/2)**2 if x < 1 else 0


def pose(rig, action_name, t):
    reset_pose(rig)
    bones = rig.pose.bones
    if action_name == "Idle":
        phase = math.tau*t/3
        breath = math.sin(phase)
        bones["Spine"].scale = (1+.007*breath,1+.005*breath,1+.006*breath)
        bones["Chest"].rotation_euler.x = math.radians(.65*breath)
        bones["Head"].rotation_euler.x = math.radians(-.60*breath)
        bones["Head"].rotation_euler.z = math.radians(.65*math.sin(phase))
        bones["Tail"].rotation_euler.z = math.radians(2.0*math.sin(phase))
        closing = blink(t,1.20,.15)
        for sign,suffix in ((1,"L"),(-1,"R")):
            bones["UpperArm."+suffix].rotation_euler.z = sign*math.radians(.65*breath)
    else:
        lean = smoothstep(.18,.82,t)*(1-smoothstep(2.52,3.16,t))
        chew = sum(blink(t, center, .205) for center in (.95,1.45,1.95))
        swallow = blink(t,2.40,.20)
        bones["Spine"].rotation_euler.x = math.radians(-3.0*lean)
        bones["Chest"].rotation_euler.x = math.radians(-5.0*lean)
        bones["Head"].rotation_euler.x = math.radians(-10*lean+2*chew+3*swallow)
        bones["MouthLower"].location.y = -.0063*chew
        bones["MouthUpper"].location.y = .0016*chew
        bones["Tail"].rotation_euler.z = math.radians(2.0*math.sin(t*math.tau/3.2))*lean
        for sign,suffix in ((1,"L"),(-1,"R")):
            bones["UpperArm."+suffix].rotation_euler.x = -.025*lean
            bones["UpperArm."+suffix].rotation_euler.z = sign*.020*lean
            bones["Forearm."+suffix].rotation_euler.x = -.045*lean
            bones["Hand."+suffix].rotation_euler.x = .020*lean
        closing = max(.08*lean,blink(t,.43,.18),blink(t,2.71,.18))
    for suffix in ("L","R"):
        bones["LidUpper."+suffix].rotation_euler.y = -math.radians(78)*closing
        bones["LidLower."+suffix].rotation_euler.y = math.radians(78)*closing


def action_curves(action):
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield from bag.fcurves


def create_actions(rig):
    for name,last in (("Idle",91),("Eat",97)):
        rig.animation_data_create()
        rig.animation_data.action = None
        for frame in range(1,last+1):
            bpy.context.scene.frame_set(frame)
            pose(rig,name,(frame-1)/FPS)
            for bone in rig.pose.bones:
                for channel in ("location","rotation_euler","scale"):
                    bone.keyframe_insert(data_path=channel,frame=frame,group=bone.name)
        action = rig.animation_data.action
        action.name = name
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start,action.frame_end = 1,last
        action.use_cyclic = name == "Idle"
        for curve in action_curves(action):
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
        markers = [(1,"Start"),(last,"Loop" if name=="Idle" else "Complete")]
        if name == "Eat":
            markers += [(29,"Bite01"),(44,"Bite02"),(59,"Bite03"),(73,"Consume")]
        for frame,label in markers:
            marker=action.pose_markers.new(label)
            marker.frame=frame
        log("Created action %s: %d frames at %d fps" % (name,last,FPS))
    set_action(rig,"Idle")


def set_action(rig,name,frame=1):
    rig.animation_data_create()
    rig.animation_data.action=bpy.data.actions[name]
    if rig.animation_data.action.slots:
        rig.animation_data.action_slot=rig.animation_data.action.slots[0]
    bpy.context.scene.frame_start=1
    bpy.context.scene.frame_end=91 if name=="Idle" else 97
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def look_at(obj,target):
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()


def make_studio():
    scene=bpy.context.scene
    studio=bpy.data.collections.new("PREVIEW_STUDIO_NOT_EXPORTED")
    scene.collection.children.link(studio)
    def move(obj):
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        studio.objects.link(obj)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.0004))
    plane=bpy.context.object
    plane.name="PreviewFloor"
    move(plane)
    mat=bpy.data.materials.new("PreviewFloor_Material")
    mat.use_nodes=True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(.74,.79,.78,1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value=.82
    plane.data.materials.append(mat)
    for name,position,power,size,color in (
        ("Key",(-.30,.36,.46),5.0,.28,(1,.97,.92)),
        ("Fill",(.28,.22,.26),2.0,.25,(.87,.93,1)),
        ("Rim",(.06,-.32,.39),6.0,.24,(1,.95,.87)),
    ):
        data=bpy.data.lights.new("Preview_"+name,"AREA")
        data.energy=power
        data.shape="DISK"
        data.size=size
        data.color=color
        obj=bpy.data.objects.new(data.name,data)
        studio.objects.link(obj)
        obj.location=position
        look_at(obj,(0,0,.10))
    camera_data=bpy.data.cameras.new("Pet01_PreviewCamera")
    camera=bpy.data.objects.new("Pet01_PreviewCamera",camera_data)
    studio.objects.link(camera)
    camera_data.type="ORTHO"
    camera_data.ortho_scale=.265
    camera_data.lens=55
    camera_data.clip_start=.001
    camera_data.clip_end=200
    scene.camera=camera
    set_camera("hero")
    scene.world=bpy.data.worlds.new("Pet01_StudioWorld")
    scene.world.use_nodes=True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value=(.82,.86,.84,1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value=.50
    scene.render.engine="CYCLES"
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.cycles.max_bounces=5
    scene.cycles.diffuse_bounces=3
    scene.cycles.glossy_bounces=3
    scene.render.resolution_x=800
    scene.render.resolution_y=900
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.render.image_settings.color_mode="RGBA"
    scene.render.film_transparent=False
    scene.view_settings.view_transform="Khronos PBR Neutral"
    scene.view_settings.look="None"
    scene.view_settings.exposure=-.7
    scene.render.fps=FPS


def set_camera(view):
    camera=bpy.context.scene.camera
    positions={"hero":(-.29,.44,.215),"front":(0,.48,.106),"side":(.48,.005,.106),
               "back":(0,-.48,.106),"face":(.12,.42,.215)}
    camera.location=positions[view]
    target=(0,.005,.106) if view!="face" else (0,.012,.180)
    look_at(camera,target)
    camera.data.ortho_scale=.257 if view!="face" else .087
    # True orthographic elevation views do not need an edge-on studio floor.
    bpy.data.objects["PreviewFloor"].hide_render=view in {"front","side","back"}


def write_manifest(rig,mesh):
    mesh.data.calc_loop_triangles()
    manifest={
        "schema_version":1,"asset_id":"Pet01","created":"2026-09-09","updated":"2026-09-11",
        "character":"Naiwa-style pet rebuilt to the user-selected Meshy candidate proportions",
        "reference":REFERENCE_BOARD,
        "reference_frames":REFERENCE_FRAMES,
        "shape_revision":SHAPE_REVISION,
        "image_reference":IMAGE_REFERENCE_BOARD,
        "reference_sha256":hashlib.sha256((ROOT/REFERENCE_BOARD).read_bytes()).hexdigest(),
        "shape_note":"2026-09-10 to 2026-09-11: narrow torso and shoulders to the Meshy silhouette, bend arms across the belly, taper thighs into inward ankles and outward feet, and raise the mouth and eyes. Authored topology and skeletal facial controls retained.",
        "software":"Blender "+bpy.app.version_string,
        "source_file":str(BLEND.relative_to(ROOT)).replace("\\","/"),
        "geometry":{"vertices":len(mesh.data.vertices),"triangles":len(mesh.data.loop_triangles),
                    "materials":[m.name for m in mesh.data.materials],"bones":len(rig.data.bones),
                    "rest_height_m":float(mesh.dimensions.z),
                    "rest_width_m":float(mesh.dimensions.x),"rest_depth_m":float(mesh.dimensions.y),
                    "source_forward":"+Y","source_up":"+Z",
                    "maximum_skin_influences":max(len(v.groups) for v in mesh.data.vertices)},
        "abdomen_design_profile":{"height_m":.091,"width_m":.0956,"depth_m":.0946,"center_y_m":-.0022,
                                  "note":"Authored cross-section parameters, compared with the height-normalized candidate mesh."},
        "proportion_reference":{"normalized_height_m":.2,"normalized_width_m":.1110748723,
                                "normalized_depth_m":.0992085189,"source_triangles":65546,
                                "has_textures":False,"has_rig":False,"has_animations":False,
                                "blend_collection":"REFERENCE_MESHY_NOT_EXPORTED",
                                "hidden_in_source":True,"included_in_fbx":False},
        "clips":[
            {"name":"Idle","fps":FPS,"first_blender_frame":1,"last_blender_frame":91,
             "duration_seconds":3.0,"loop":True,"root_motion":False},
            {"name":"Eat","fps":FPS,"first_blender_frame":1,"last_blender_frame":97,
             "duration_seconds":3.2,"loop":False,"root_motion":False,
             "suggested_events":[{"name":"Bite01","time_seconds":28/30},
                                 {"name":"Bite02","time_seconds":43/30},
                                 {"name":"Bite03","time_seconds":58/30},
                                 {"name":"Consume","time_seconds":72/30}]}
        ],
        "animation_event_note":"Markers are design metadata; add Unity events only when a receiver exists. Consume is a single gameplay settlement point.",
        "texture":"Textures/Pet01_BaseColor.png",
        "status":{"blend_created":True,"fbx_exported":False,"blender_fbx_roundtrip":False,
                  "unity_import_tested":False,"android_device_tested":False},
        "export_settings":{"forward":"-Z","up":"Y","apply_unit_scale":True,"add_leaf_bones":False,
                           "included_object_types":["ARMATURE","MESH"],"preview_studio_excluded":True}
    }
    (SOURCE/"asset_manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    return manifest


def make_proportion_reference():
    """Keep an optional hidden, height-normalized reference in the edit file."""
    before=set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(ROOT/REFERENCE_BOARD))
    imported=[o for o in bpy.context.scene.objects if o not in before]
    reference=next(o for o in imported if o.type=="MESH")
    activate(reference)
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    raw=np.array([v.co[:] for v in reference.data.vertices])
    lo,hi=raw.min(axis=0),raw.max(axis=0)
    factor=.2/(hi[2]-lo[2])
    for vertex in reference.data.vertices:
        x,y,z=vertex.co
        vertex.co=(-(x-(lo[0]+hi[0])/2)*factor,-(y-(lo[1]+hi[1])/2)*factor,(z-lo[2])*factor)
    reference.name="Meshy_Proportion_Reference"
    reference["source_file"]=REFERENCE_BOARD
    reference["note"]="User-selected proportion reference, height-normalized only. Hidden by default and excluded from FBX."
    collection=bpy.data.collections.new("REFERENCE_MESHY_NOT_EXPORTED")
    bpy.context.scene.collection.children.link(collection)
    collection.hide_render=True
    for obj in imported:
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        collection.objects.link(obj)
        obj.hide_render=True
        obj.hide_set(True)
    log("Stored hidden Meshy proportion reference; original GLB unchanged")


def build():
    global COLLECTION
    for directory in (SOURCE,PREVIEWS,SOURCE/"Textures",MODEL_DIR,TEXTURE_DIR):
        directory.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene
    scene.unit_settings.system="METRIC"
    scene.unit_settings.scale_length=1
    scene.render.fps=FPS
    COLLECTION=bpy.data.collections.new("Pet01_CHARACTER")
    scene.collection.children.link(COLLECTION)
    setup_materials(make_texture())
    make_body()
    log("Body and mouth created")
    make_arm(1)
    make_arm(-1)
    make_leg(1)
    make_leg(-1)
    make_tail()
    unify_skin()
    make_eye(1)
    make_eye(-1)
    rig=make_rig()
    mesh=join_and_bind(rig)
    log("Mesh complete; creating animation")
    create_actions(rig)
    make_studio()
    make_proportion_reference()
    set_action(rig,"Idle",1)
    activate(rig)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=="VIEW_3D":
                area.spaces.active.clip_start=.001
                area.spaces.active.clip_end=100
                area.spaces.active.region_3d.view_distance=.35
                area.spaces.active.region_3d.view_location=(0,0,.1)
                area.spaces.active.shading.type="MATERIAL"
    # Images stay external and editable, using paths relative to the source blend.
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    manifest=write_manifest(rig,mesh)
    log("Saved source: "+str(BLEND))
    log(json.dumps(manifest["geometry"]))


def preview(args):
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    scene=bpy.context.scene
    scene.render.resolution_x=args.size
    scene.render.resolution_y=round(args.size*1.10)
    scene.cycles.samples=args.samples
    rig=bpy.data.objects["Pet01Rig"]
    for view in args.views.split(","):
        set_action(rig,args.action,args.frame)
        set_camera(view)
        scene.render.filepath=str(PREVIEWS/(args.action.lower()+"_%03d_%s.png"%(args.frame,view)))
        bpy.ops.render.render(write_still=True)
        log("Rendered "+scene.render.filepath)
        if view=="front" and args.action=="Idle" and args.frame==1:
            from bpy_extras.object_utils import world_to_camera_view
            def screen(p):
                v=world_to_camera_view(scene,scene.camera,rig.matrix_world @ p)
                return [v.x,1-v.y]
            projection={"width":scene.render.resolution_x,"height":scene.render.resolution_y,
                        "source_sha256":hashlib.sha256(BLEND.read_bytes()).hexdigest(),
                        "bones":[{"name":b.name,"parent":b.parent.name if b.parent else None,
                                  "head":screen(b.head),"tail":screen(b.tail)} for b in rig.pose.bones]}
            (PREVIEWS/"skeleton_projection.json").write_text(json.dumps(projection,indent=2)+"\n",encoding="utf-8")


def sequence(args):
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    scene=bpy.context.scene
    scene.render.resolution_x=args.size
    scene.render.resolution_y=round(args.size*1.10)
    scene.cycles.samples=args.samples
    rig=bpy.data.objects["Pet01Rig"]
    set_action(rig,args.action,1)
    set_camera("hero")
    directory=PREVIEWS/"Frames"/args.action
    directory.mkdir(parents=True,exist_ok=True)
    last=91 if args.action=="Idle" else 97
    frames=list(range(1,last,args.step))
    for index,frame in enumerate(frames):
        scene.frame_set(frame)
        scene.render.filepath=str(directory/(args.action+"_%04d.png"%index))
        bpy.ops.render.render(write_still=True)
        if index%10==0:
            log("Preview %s %d/%d"%(args.action,index+1,len(frames)))
    metadata={"action":args.action,"source_fps":FPS,"preview_fps":FPS/args.step,
              "source_sha256":hashlib.sha256(BLEND.read_bytes()).hexdigest(),
              "source_frames":frames,"duration_seconds":(last-1)/FPS,"loop":args.action=="Idle"}
    (directory/"sequence.json").write_text(json.dumps(metadata,indent=2)+"\n",encoding="utf-8")
    log("Preview sequence complete: "+args.action)


def export():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    rig=bpy.data.objects["Pet01Rig"]
    mesh=bpy.data.objects["Pet01_Mesh"]
    common=dict(use_selection=True,object_types={"ARMATURE","MESH"},add_leaf_bones=False,
                use_armature_deform_only=True,axis_forward="-Z",axis_up="Y",
                apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",
                use_mesh_modifiers=True,mesh_smooth_type="FACE",use_tspace=False,
                path_mode="RELATIVE",embed_textures=False,bake_anim_use_nla_strips=False,
                bake_anim_use_all_actions=False,bake_anim_simplify_factor=0.0,
                bake_anim_step=1.0,armature_nodetype="NULL")
    def select():
        bpy.ops.object.select_all(action="DESELECT")
        rig.select_set(True)
        mesh.select_set(True)
        bpy.context.view_layer.objects.active=rig
    # Point exported image references to the runtime texture copy.
    bpy.data.images["Pet01_BaseColor"].filepath=str(TEXTURE_DIR/"Pet01_BaseColor.png")
    rig.animation_data.action=None
    reset_pose(rig)
    bpy.context.view_layer.update()
    select()
    bpy.ops.export_scene.fbx(filepath=str(MODEL_DIR/"Pet01.fbx"),bake_anim=False,**common)
    for name in ("Idle","Eat"):
        set_action(rig,name,1)
        bpy.context.scene.name=name
        select()
        bpy.ops.export_scene.fbx(filepath=str(MODEL_DIR/("Pet01_"+name+".fbx")),bake_anim=True,**common)
    manifest_path=SOURCE/"asset_manifest.json"
    manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["status"]["fbx_exported"]=True
    manifest["export_files"]=[str(p.relative_to(ROOT)).replace("\\","/") for p in sorted(MODEL_DIR.glob("*.fbx"))]
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    (ART/"asset_manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    log("Exported model, Idle and Eat FBX assets")


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--phase",choices=("build","preview","sequence","export"),default="build")
    parser.add_argument("--views",default="hero")
    parser.add_argument("--action",default="Idle",choices=("Idle","Eat"))
    parser.add_argument("--frame",type=int,default=1)
    parser.add_argument("--size",type=int,default=800)
    parser.add_argument("--samples",type=int,default=24)
    parser.add_argument("--step",type=int,choices=(1,2,3),default=2)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.phase=="build":
        build()
    elif args.phase=="preview":
        preview(args)
    elif args.phase=="sequence":
        sequence(args)
    else:
        export()


if __name__=="__main__":
    main()
