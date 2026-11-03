"""Build the two face decal patches for Pet01 (P5-A) without touching the frozen source.

The frozen model (`ArtSource/Characters/Pet01/pet01.blend`) is opened read-only and is
never saved. Two flat grids are generated in memory in world space, projected onto the
rest-pose face along -Y, pushed out along the *hit triangle's surface normal* (eyes
1.2mm, mouth 1.0mm) and exported as `Pet01_FaceOverlay.fbx`. Each patch carries UV 0..1
over exactly one atlas cell, so Unity selects an expression by setting `_MainTex_ST` from
`FaceOverlay/face_atlas_manifest.json`.

    D:/blender.exe -b ArtSource/Characters/Pet01/pet01.blend --python-exit-code 1 \
        --python Tools/Blender/build_pet01_face_overlay.py -- [--no-preview]

2026-10-27 rework (see FaceOverlay/README.md):
  * offsets follow the surface normal instead of a fixed -Y shift, so steep cheek
    triangles no longer cut through the skin;
  * triangles that touch a vertex where the -Y ray missed are deleted instead of being
    interpolated (they were the cause of the purple tail and the floating disc edges);
  * every patch triangle is verified (gap along its own normal, occlusion from five
    camera directions) and the atlas ink that lost coverage is counted;
  * previews cover the old eyes/mouth with a *copy of the skin material* pinned to the
    sampled skin texel (`_MainTex_ST = (0,0,u,v)`), not with a flat colour;
  * the mouth cells are re-aligned to the measured Oral surface and lip slit.

Phases: it always builds, verifies, samples cover colours, exports the FBX and writes the
report. Preview renders go to `ArtSource/Characters/Pet01/Logs/FaceOverlay_20261027/`.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Characters/Pet01/pet01.blend'
WORK = ROOT / 'ArtSource/Characters/Pet01/FaceOverlay'
LOGS = ROOT / 'ArtSource/Characters/Pet01/Logs/FaceOverlay_20261027'
TEMP = Path('D:/Cache/Temp/opencode')
MODELS = ROOT / 'ARPet/Assets/_Project/Art/Characters/Pet01/Models'
TEXTURES = ROOT / 'ARPet/Assets/_Project/Art/Characters/Pet01/Textures'
REPORT = WORK / 'face_overlay_report.json'
MANIFEST = WORK / 'face_atlas_manifest.json'
EXPORT = MODELS / 'Pet01_FaceOverlay.fbx'
UNITY_LAYOUT = MODELS.parent / 'face_overlay_layout.json'

SOURCE_SHA = 'a289a8db83d91452284537fb55afbee4a2a6fb42c0c51a6027b564511a5c3c9b'
RIG, MESH = 'Pet01Rig', 'Pet01_Mesh'
MATERIALS = {'Pet01_Skin': 'skin', 'Pet01_Oral': 'oral', 'Pet01_Eyes': 'eyes'}
TIMES = 'classic calm rage divine'.split()
PEAK_ALPHA = 0.25  # "visible ink" threshold for the coverage audit
POKE_THROUGH_M = 0.005  # an occluder this close to a patch triangle is skin cutting through it
# The Unity decal shader (ZQXNXS/FaceDecal) moves every patch vertex this far toward the
# camera in view space. Skin closer than this no longer hides the decal; hands and arms
# further in front still do. The previews emulate it by translating the patches along the
# (orthographic) view direction.
# Round 6: the offset is scaled by how frontal the view is, max(0, dot(face forward, view)),
# so 35 degrees still gets ~8.2mm (the sweep shows 8mm clears front / +-35 degrees) while a
# 90 degree profile gets none and the decal no longer sticks out of the silhouette.
VIEW_OFFSET_M = 0.010


def facing(view_dir):
    """Offset scale used by ZQXNXS/FaceDecal: cosine between the face forward (+Y) and the view."""
    v = np.asarray(view_dir, dtype=np.float64)
    return max(0., float(v[1] / np.linalg.norm(v)))
OFFSET_SWEEP_MM = (2, 4, 6, 8, 10, 12, 15, 20)  # report: triangles still hidden per offset
# Round 3: quads whose surface faces away from the front more than this (normal.y below
# the value) sit on the silhouette; seen from 35 degrees their lift shows as slabs past
# the head outline, so they are dropped like missed quads.
MIN_FACING_Y = 0.45
SILHOUETTE_STEPS = 6  # 3mm on the eyes grid
BRIDGE_CAP_M = 0.002  # groove filling never lifts a vertex by more than this
# Same export call as the Pet01.fbx export in Tools/Blender/build_pet01_arm_separation.py
# (line ~618): identical axis conversion, unit scale and smoothing, so both files can be
# dropped into the same Unity import settings.
FBX_OPTIONS = dict(add_leaf_bones=False, use_armature_deform_only=True, axis_forward='-Z', axis_up='Y',
                   apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
                   mesh_smooth_type='FACE', use_tspace=False, path_mode='RELATIVE', embed_textures=False,
                   bake_anim=False, armature_nodetype='NULL')
PATCHES = {
    # Round 3: grid doubled (0.5mm quads) so the dropped-quad boundary no longer staircases.
    'Pet01_FaceEyes': dict(x=(-0.024, 0.024), z=(0.1785, 0.1965), seg=(96, 36), grip='eyes', lift=0.0012,
                           bridge=2, relax=3, drop_steep=True),
    # Rework: the mouth rect grew upward (same 2:1 aspect) so the whole lip slit and the
    # Oral surface fit inside the cell with room above them.
    'Pet01_FaceMouth': dict(x=(-0.0172, 0.0172), z=(0.1655, 0.1827), seg=(28, 14), grip='mouth', lift=0.0010,
                            bridge=2, drop_steep=False),  # steep quads there are the lip groove
}
EYE_CENTRES = ((0.0131, 0.1856), (-0.0131, 0.1856))  # Eye.L / Eye.R head bones
EYE_RADIUS = 0.00553125  # 59 design px on a cell that spans 0.048 x 0.018m
EYE_RING = (0.0015, 0.0025)  # 1-3mm outside the eye ellipse
MOUTH_RECT = PATCHES['Pet01_FaceMouth']
CAMERA_TARGET = (0., .02, .181)
CAMERAS = {
    'front': (0., .40, .183),
    'right35': (.40 * np.sin(np.radians(35)), .40 * np.cos(np.radians(35)), .196),
    'left35': (-.40 * np.sin(np.radians(35)), .40 * np.cos(np.radians(35)), .196),
    'right60': (.40 * np.sin(np.radians(60)), .40 * np.cos(np.radians(60)), .200),
    'left60': (-.40 * np.sin(np.radians(60)), .40 * np.cos(np.radians(60)), .200),
    'side90': (.40, .02, .181),
}
PATCH_DATA = {}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def log(*args, **kwargs):
    kwargs.pop('flush', None)
    print(*args, flush=True, **kwargs)


# --------------------------------------------------------------------- evaluated mesh

class Surface:
    """World-space rest-pose triangles of Pet01_Mesh plus per-loop UVs and materials."""

    def __init__(self, obj):
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = obj.evaluated_get(depsgraph)
        me = evaluated.to_mesh()
        try:
            mw = obj.matrix_world.copy()
            self.verts = np.array([tuple(mw @ v.co) for v in me.vertices], dtype=np.float64)
            me.calc_loop_triangles()
            uv_layer = me.uv_layers['UVMap'].data
            self.tris = np.array([t.vertices[:] for t in me.loop_triangles], dtype=np.int32)
            self.tri_uvs = np.array([[[uv_layer[l].uv[0], uv_layer[l].uv[1]] for l in t.loops]
                                     for t in me.loop_triangles], dtype=np.float64)
            self.tri_mat = np.array([me.polygons[t.polygon_index].material_index
                                     for t in me.loop_triangles], dtype=np.int32)
            slot_names = [m.name if m else '' for m in obj.data.materials]
            groups = {g.index: g.name for g in obj.vertex_groups}
            lid = {i for i, v in enumerate(obj.data.vertices)
                   if any(groups[g.group] in ('LidUpper.L', 'LidUpper.R', 'LidLower.L', 'LidLower.R')
                          and g.weight > 0.05 for g in v.groups)}
            self.lid_tris = np.array([i for i, t in enumerate(me.loop_triangles)
                                      if any(int(v) in lid for v in t.vertices)], dtype=np.int32)
            self.slot_names = slot_names
            self.vnormals = np.array([tuple(v.normal) for v in me.vertices], dtype=np.float64)
            self.slot_role = np.array([MATERIALS.get(slot_names[m], 'other') if m < len(slot_names) else 'other'
                                       for m in self.tri_mat])
            log('SURFACE verts', len(self.verts), 'tris', len(self.tris),
                'slots', slot_names, 'lid_tris', len(self.lid_tris),
                'roles', {r: int((self.slot_role == r).sum()) for r in set(self.slot_role.tolist())})
        finally:
            evaluated.to_mesh_clear()
        coord, index = self.verts, self.tris.tolist()
        self.bvh_all = BVHTree.FromPolygons(coord, index, all_triangles=True, epsilon=0.0)
        eyes = np.nonzero(self.slot_role == 'eyes')[0]
        oral = np.nonzero(self.slot_role == 'oral')[0]
        lids = self.lid_tris
        self.bvh_eyes = (BVHTree.FromPolygons(coord, self.tris[eyes].tolist(), all_triangles=True)
                         if len(eyes) else None), eyes
        self.bvh_oral = (BVHTree.FromPolygons(coord, self.tris[oral].tolist(), all_triangles=True)
                         if len(oral) else None), oral
        self.bvh_lid = (BVHTree.FromPolygons(coord, self.tris[lids].tolist(), all_triangles=True)
                        if len(lids) else None), lids
        self.bvh_skin = None
        self.skin_tris = np.nonzero(self.slot_role == 'skin')[0]
        if len(self.skin_tris):
            self.bvh_skin = (BVHTree.FromPolygons(coord, self.tris[self.skin_tris].tolist(), all_triangles=True),
                             self.skin_tris)

    def cast(self, x, z, bvh=None):
        """First hit from +Y above the face; returns (y, face) or (None, None)."""
        bvh = bvh or self.bvh_all
        location, _normal, index, _dist = bvh.ray_cast(Vector((x, 0.2, z)), Vector((0., -1., 0.)), 2.0)
        return (float(location.y), int(index)) if location is not None else (None, None)

    def normal(self, tri):
        a, b, c = (self.verts[self.tris[tri][i]] for i in range(3))
        n = np.cross(b - a, c - a)
        length = np.linalg.norm(n)
        if length < 1e-12:
            return np.array([0., 1., 0.])
        n = n / length
        return n if n[1] >= 0 else -n

    def smooth_normal(self, tri, location):
        """Area-weighted vertex normals interpolated at the hit point (no per-face creases)."""
        idx = self.tris[tri]
        a, b, c = (self.verts[idx[i]] for i in range(3))
        p = np.array(location, dtype=np.float64)
        v0, v1, v2 = b - a, c - a, p - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        denominator = d00 * d11 - d01 * d01
        if abs(denominator) < 1e-18:
            n = self.vnormals[idx].mean(axis=0)
        else:
            v = (d11 * d20 - d01 * d21) / denominator
            w = (d00 * d21 - d01 * d20) / denominator
            u = 1. - v - w
            n = u * self.vnormals[idx[0]] + v * self.vnormals[idx[1]] + w * self.vnormals[idx[2]]
        length = np.linalg.norm(n)
        n = n / length if length > 1e-12 else np.array([0., 1., 0.])
        return n if n[1] >= 0 else -n

    def uv(self, tri, location):
        """Barycentric UV of a world point on triangle `tri` (index into self.tris)."""
        a, b, c = self.verts[self.tris[tri][0]], self.verts[self.tris[tri][1]], self.verts[self.tris[tri][2]]
        p = np.array(location, dtype=np.float64)
        v0, v1, v2 = b - a, c - a, p - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        denominator = d00 * d11 - d01 * d01
        if abs(denominator) < 1e-18:
            return self.tri_uvs[tri][0]
        v = (d11 * d20 - d01 * d21) / denominator
        w = (d00 * d21 - d01 * d20) / denominator
        u = 1. - v - w
        return u * self.tri_uvs[tri][0] + v * self.tri_uvs[tri][1] + w * self.tri_uvs[tri][2]


# --------------------------------------------------------------------- patch building

def build_patch(name, spec, surface, stats):
    """Project a grid along -Y, push out along the surface normal, drop outside triangles."""
    (x0, x1), (z0, z1) = spec['x'], spec['z']
    nx, nz = spec['seg']
    lift = spec['lift']
    rows, cols = nz + 1, nx + 1
    xs = np.linspace(x0, x1, cols)
    zs = np.linspace(z0, z1, rows)
    hit_y = np.full((rows, cols), np.nan)
    pos = np.full((rows, cols, 3), np.nan)
    normal = np.full((rows, cols, 3), np.nan)
    miss = np.zeros((rows, cols), dtype=bool)
    on_skin = np.zeros((rows, cols), dtype=bool)
    eye_front = np.full((rows, cols), np.nan)
    lid_front = np.full((rows, cols), np.nan)
    for i, z in enumerate(zs):
        for j, x in enumerate(xs):
            y, tri = surface.cast(x, z)
            if y is None:
                miss[i, j] = True
            else:
                on_skin[i, j] = surface.slot_role[tri] == 'skin'
                n = surface.smooth_normal(tri, (x, y, z))
                hit_y[i, j] = y
                normal[i, j] = n
                pos[i, j] = np.array([x, y, z]) + n * lift
            for key, bvh in (('eye', surface.bvh_eyes[0]), ('lid', surface.bvh_lid[0])):
                if bvh is None:
                    continue
                yy, _ = surface.cast(x, z, bvh)
                if yy is not None:
                    (eye_front if key == 'eye' else lid_front)[i, j] = yy
    # Keep the eyes patch above the front-most eyeball / eyelid surface as well.
    lifted = np.zeros((rows, cols), dtype=bool)
    for i in range(rows):
        for j in range(cols):
            if miss[i, j]:
                continue
            floor_y = pos[i, j][1]
            for extra in (eye_front[i, j], lid_front[i, j]):
                if not np.isnan(extra) and extra + lift > floor_y:
                    floor_y = extra + lift
                    lifted[i, j] = True
            pos[i, j][1] = floor_y
    # Round 3: bridge narrow grooves (lip slit, eyeball crease). Each vertex takes the
    # front-most y of its neighbours within `bridge` grid steps, so the patch spans the
    # groove instead of dipping into it where the groove walls cut through from 35 degrees.
    # Round 4: a morphological closing (max filter, then min filter) fills only grooves
    # narrower than the kernel, such as the lip slit where the -Y ray dives deep into the
    # Oral cavity. Convex or sloped skin is left where it is, so the patch no longer
    # lifts off the cheek in profile (the round-3 dilation did).
    radius = spec.get('bridge', 0)
    if radius:
        def window(values, fill, reduce):
            out = values.copy()
            for di in range(-radius, radius + 1):
                for dj in range(-radius, radius + 1):
                    shifted = np.full_like(values, fill)
                    si = slice(max(di, 0), rows + min(di, 0))
                    ti = slice(max(-di, 0), rows + min(-di, 0))
                    sj = slice(max(dj, 0), cols + min(dj, 0))
                    tj = slice(max(-dj, 0), cols + min(-dj, 0))
                    shifted[ti, tj] = values[si, sj]
                    out = reduce(out, shifted)
            return out

        ys = np.where(miss, -np.inf, pos[..., 1])
        closed = window(window(ys, -np.inf, np.maximum), np.inf, np.minimum)
        closed = np.where(np.isfinite(closed), closed, ys)
        # Then relax: average with the 4 neighbours but never sink below the closed
        # surface, which rounds the steps where the eyeball rim meets the crease.
        relaxed = closed.copy()
        for _ in range(spec.get('relax', 0)):
            pad = np.pad(relaxed, 1, mode='edge')
            avg = (pad[:-2, 1:-1] + pad[2:, 1:-1] + pad[1:-1, :-2] + pad[1:-1, 2:] + relaxed) / 5.
            relaxed = np.where(np.isfinite(avg), np.maximum(avg, closed), closed)
        bridge_lift = np.where(miss, 0., np.minimum(np.maximum(relaxed - ys, 0.), BRIDGE_CAP_M))
        pos[..., 1] = np.where(miss, pos[..., 1], ys + bridge_lift)
    else:
        bridge_lift = np.zeros((rows, cols))
    # A quad survives only if all four corners had a real -Y hit: no interpolation at all.
    keep = np.zeros((nz, nx), dtype=bool)
    steep = 0
    # Round 4: steep quads are dropped only near the head silhouette (within
    # SILHOUETTE_STEPS grid steps of a missed ray). The crease around the eyeball is also
    # steep, but it lies inside the face and dropping it bit holes into the discs.
    near_edge = miss.copy()
    for _ in range(SILHOUETTE_STEPS):
        grown = near_edge.copy()
        grown[1:, :] |= near_edge[:-1, :]
        grown[:-1, :] |= near_edge[1:, :]
        grown[:, 1:] |= near_edge[:, :-1]
        grown[:, :-1] |= near_edge[:, 1:]
        near_edge = grown
    for i in range(nz):
        for j in range(nx):
            corners = ((i, j), (i, j + 1), (i + 1, j), (i + 1, j + 1))
            keep[i, j] = not any(miss[c] for c in corners)
            # Round 5: only skin quads are dropped; the eyeball's own steep sides must stay
            # covered, otherwise the cover-coloured original shows as a bite in the disc.
            if (keep[i, j] and spec.get('drop_steep') and any(near_edge[c] for c in corners)
                    and all(on_skin[c] for c in corners)
                    and min(normal[c][1] for c in corners) < MIN_FACING_Y):
                keep[i, j] = False
                steep += 1
    # Clearance repair: where the triangulated surface still pokes through a patch triangle
    # (the lip groove, the brow ridge), lift the involved vertices along +Y in small steps
    # until the front / +-35 degree cameras are clear. Reported, never silent.
    repair_cams = [np.array(CAMERAS[c]) for c in ('front', 'left35', 'right35')]
    repaired, extra, iterations = 0, 0., 0
    offending = set()
    # At most 4 x 0.25mm extra: higher lifts make the decal visibly float. What remains is
    # left to the Unity overlay shader's depth offset and is reported.
    for iterations in range(1, 5):
        offending = set()
        for i, j in np.argwhere(keep):
            a = pos[i, j]
            b, c, d = pos[i + 1, j + 1], pos[i, j + 1], pos[i + 1, j]
            for tri in ((a, b, c), (a, d, b)):
                centre = (tri[0] + tri[1] + tri[2]) / 3.
                for cam in repair_cams:
                    direction = cam - centre
                    dist = float(np.linalg.norm(direction))
                    direction = direction / dist
                    origin = Vector((centre + direction * 1e-5).tolist())
                    hit, _n, _i, hit_dist = surface.bvh_all.ray_cast(origin, Vector(direction.tolist()), dist)
                    # Only a skin surface right on top of the patch is a poke-through; the far
                    # cheek hiding the patch from a side camera is ordinary occlusion.
                    if hit is not None and hit_dist < POKE_THROUGH_M:
                        offending.update(((i, j), (i, j + 1), (i + 1, j), (i + 1, j + 1)))
                        break
        if not offending:
            break
        for i, j in offending:
            pos[i, j][1] += 0.00025
            repaired += 1
            extra += 0.00025
    repair = {'repair_iterations': iterations,
              'repair_vertex_lifts': repaired,
              'repair_step_m': 0.00025,
              'repair_converged': not offending,
              'poke_through_threshold_m': POKE_THROUGH_M,
              'quads_dropped_steep': steep, 'min_facing_normal_y': MIN_FACING_Y,
              'bridge_grid_steps': radius,
              'bridge_lift_max_m': round(float(bridge_lift.max()), 5),
              'bridge_lift_mean_m': round(float(bridge_lift[~miss].mean()), 6)}
    faces, users = [], set()
    for i, j in np.argwhere(keep):
        a = i * cols + j
        b, c, d = a + 1, a + cols + 1, a + cols
        # Winding chosen so the face normal is +Y (visible from the front with culling).
        faces.append((a, c, b))
        faces.append((a, d, c))
        users.update((a, b, c, d))
    order = sorted(users)
    remap = {old: new for new, old in enumerate(order)}
    verts, uvs = [], []
    for old in order:
        i, j = divmod(old, cols)
        verts.append(tuple(float(v) for v in pos[i, j]))
        uvs.append(((xs[j] - x0) / (x1 - x0), (zs[i] - z0) / (z1 - z0)))
    faces = [(remap[a], remap[b], remap[c]) for a, b, c in faces]
    total_triangles = 2 * nx * nz

    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uv_layer = me.uv_layers.new(name='UVMap')
    for loop in me.loops:
        uv_layer.data[loop.index].uv = uvs[loop.vertex_index]
    me.update()
    obj = bpy.data.objects.new(name, me)
    obj.matrix_world = Matrix.Identity(4)
    obj.parent = None
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()

    PATCH_DATA[name] = {'uv': np.array(uvs, dtype=np.float64), 'keep': keep, 'miss': miss,
                        'hit_y': hit_y, 'pos': pos, 'seg': (nx, nz)}
    offsets = np.array([pos[i, j][1] - hit_y[i, j] for i in range(rows) for j in range(cols)
                        if not miss[i, j]])
    offsets3d = np.array([np.linalg.norm(pos[i, j] - np.array([xs[j], hit_y[i, j], zs[i]]))
                          for i in range(rows) for j in range(cols) if not miss[i, j]])
    lateral = np.array([np.hypot(pos[i, j][0] - xs[j], pos[i, j][2] - zs[i])
                        for i in range(rows) for j in range(cols) if not miss[i, j]])
    nrm = np.array([tuple(p.normal) for p in me.polygons])
    co = np.array([[v.co[0], v.co[1], v.co[2]] for v in me.vertices])
    stats[name] = {
        'grid_segments': [nx, nz],
        'projection': 'ray -Y from y=+0.2, then displaced along the hit triangle normal',
        'normal_lift_m': lift,
        'vertices': len(me.vertices), 'triangles': len(me.polygons),
        'triangles_dropped_no_hit': total_triangles - len(me.polygons),
        'dropped_fraction': round((total_triangles - len(me.polygons)) / total_triangles, 4),
        'ray_misses_vertices': int(miss.sum()),
        'uv_layer': uv_layer.name,
        'bounds_world_m': {'x': [float(co[:, 0].min()), float(co[:, 0].max())],
                           'y': [float(co[:, 1].min()), float(co[:, 1].max())],
                           'z': [float(co[:, 2].min()), float(co[:, 2].max())]},
        'surface_gap_m': {'vertical_min': float(offsets.min()), 'vertical_max': float(offsets.max()),
                          'vertical_mean': float(offsets.mean()),
                          'euclidean_min': float(offsets3d.min()), 'euclidean_max': float(offsets3d.max())},
        'lateral_drift_m': {'max': float(lateral.max()), 'mean': float(lateral.mean())},
        'vertices_lifted_by_eye_or_lid': int(lifted.sum()),
        'normals_all_positive_y': bool(np.all(nrm[:, 1] > 0.)),
        'min_face_normal_y': round(float(nrm[:, 1].min()), 4),
        'object_matrix_world_identity': all(abs(obj.matrix_world[r][c] - (1. if r == c else 0.)) < 1e-9
                                            for r in range(4) for c in range(4)),
        'parent': None,
        **repair,
    }
    eye_delta = eye_front - hit_y
    lid_delta = lid_front - hit_y
    for label, delta in (('eye', eye_delta), ('lid', lid_delta)):
        if np.isfinite(delta).any():
            stats[name][f'{label}_front_vs_skin_m'] = {
                'vertices_with_hit': int(np.isfinite(delta).sum()),
                'max_m': float(np.nanmax(delta)), 'min_m': float(np.nanmin(delta))}
    log('PATCH', name, 'verts', len(me.vertices), 'tris', len(me.polygons),
        'dropped', total_triangles - len(me.polygons), 'steep_quads', steep, 'lift_mm', round(lift * 1000, 2),
        'gap_mm[min,max]', round(float(offsets3d.min()) * 1000, 3), round(float(offsets3d.max()) * 1000, 3),
        'drift_mm', round(float(lateral.max()) * 1000, 3), flush=True)
    return obj


def verify_patch(name, obj, surface, stats):
    """Per triangle: gap along its own normal, and occlusion from six camera directions."""
    me = obj.data
    verts = np.array([[v.co[0], v.co[1], v.co[2]] for v in me.vertices])
    tris = np.array([tuple(p.vertices) for p in me.polygons], dtype=np.int32)
    normals = np.array([tuple(p.normal) for p in me.polygons])
    gaps, no_hit = [], 0
    for k, (a, b, c) in enumerate(tris):
        centre = Vector(((verts[a] + verts[b] + verts[c]) / 3.).tolist())
        n = Vector(normals[k].tolist())
        if n.y < 0:
            n = -n
        location, _normal, _index, distance = surface.bvh_all.ray_cast(centre, -n, 0.05)
        if location is None:
            no_hit += 1
        else:
            gaps.append(float(distance))
    gaps = np.array(gaps) if gaps else np.array([np.nan])
    centres = verts[tris].mean(axis=1)
    cam_report = {}
    for cam_name, cam_pos in CAMERAS.items():
        cam = np.array(cam_pos)
        effective_offset = VIEW_OFFSET_M * facing(cam - np.array(CAMERA_TARGET))
        penetrated, occluded, blocked = 0, 0, 0
        hit_dists = []
        worst = None
        for centre in centres:
            direction = cam - centre
            dist = float(np.linalg.norm(direction))
            direction = direction / dist
            hit, _normal, _index, hit_dist = surface.bvh_all.ray_cast(
                Vector((centre + direction * 1e-5).tolist()), Vector(direction.tolist()), dist)
            if hit is not None and hit_dist < dist:
                hit_dists.append(float(hit_dist))
                if hit_dist < POKE_THROUGH_M:
                    penetrated += 1
                else:
                    occluded += 1
                if hit_dist > effective_offset:
                    blocked += 1
                if worst is None or hit_dist < worst:
                    worst = float(hit_dist)
        sweep = {}
        for offset_mm in OFFSET_SWEEP_MM:
            sweep[str(offset_mm)] = int(sum(1 for d in hit_dists if d > offset_mm / 1000.))
        cam_report[cam_name] = {'hidden_by_offset_mm': sweep,
                                'effective_view_offset_m': round(effective_offset, 5),
                                'penetrated_triangles': penetrated,
                                'penetrated_fraction': round(penetrated / max(len(tris), 1), 4),
                                'occluded_by_far_geometry_triangles': occluded,
                                'still_hidden_after_view_offset_triangles': blocked,
                                'nearest_occluder_m': worst}
    stats[name]['triangle_gap_m'] = {
        'min': float(np.nanmin(gaps)), 'max': float(np.nanmax(gaps)),
        'mean': float(np.nanmean(gaps)), 'no_hit_triangles': no_hit}
    stats[name]['visibility'] = cam_report
    log('VERIFY', name, 'gap_mm[min,max,mean]', round(float(np.nanmin(gaps)) * 1000, 3),
        round(float(np.nanmax(gaps)) * 1000, 3), round(float(np.nanmean(gaps)) * 1000, 3),
        'no_hit', no_hit,
        'penetrated', {k: v['penetrated_triangles'] for k, v in cam_report.items()},
        'occluded', {k: v['occluded_by_far_geometry_triangles'] for k, v in cam_report.items()},
        'hidden_after_offset', {k: v['still_hidden_after_view_offset_triangles'] for k, v in cam_report.items()},
        flush=True)
    return cam_report


# ------------------------------------------------------------------------- textures

def load_texture(path):
    img = bpy.data.images.load(str(path), check_existing=False)
    img.colorspace_settings.name = 'Non-Color'  # raw sRGB byte values, no linearisation
    width, height = img.size
    buf = np.empty(width * height * img.channels, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(height, width, img.channels)


def sample(texture, uv):
    height, width = texture.shape[:2]
    x = (uv[0] % 1.) * width - .5
    y = (uv[1] % 1.) * height - .5  # Blender image rows start at the bottom
    x0, y0 = int(np.floor(x)), int(np.floor(y))
    fx, fy = x - x0, y - y0
    x0c, x1c = np.clip([x0, x0 + 1], 0, width - 1)
    y0c, y1c = np.clip([y0, y0 + 1], 0, height - 1)
    p00, p10 = texture[y0c, x0c], texture[y0c, x1c]
    p01, p11 = texture[y1c, x0c], texture[y1c, x1c]
    top = p00 * (1 - fx) + p10 * fx
    bottom = p01 * (1 - fx) + p11 * fx
    return top * (1 - fy) + bottom * fy


def atlas_alpha(png):
    img = bpy.data.images.load(str(TEXTURES / png), check_existing=False)
    width, height = img.size
    buf = np.empty(width * height * img.channels, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(height, width, img.channels)[..., 3]  # row 0 = bottom


def cover_colours(surface, textures):
    """Median skin UV next to the eyes / mouth plus the colour found at that UV."""
    eye_samples, mouth_samples = [], []
    skin_bvh = surface.bvh_skin[0] if surface.bvh_skin else None

    def skin_uv(x, z):
        y, tri = surface.cast(x, z, skin_bvh) if skin_bvh else (None, None)
        if y is None:
            return None
        return surface.uv(int(surface.skin_tris[tri]), (x, y, z))

    for cx, cz in EYE_CENTRES:
        for out in EYE_RING:
            for k in range(24):
                angle = 2 * np.pi * k / 24
                uv = skin_uv(cx + (EYE_RADIUS + out) * np.cos(angle), cz + (EYE_RADIUS + out) * np.sin(angle))
                if uv is not None:
                    eye_samples.append(uv)
    for x in np.arange(MOUTH_RECT['x'][0] + 0.0015, MOUTH_RECT['x'][1] - 0.0014, 0.002):
        for z in np.arange(MOUTH_RECT['z'][0] + 0.0006, MOUTH_RECT['z'][1] - 0.0005, 0.001):
            uv = skin_uv(x, z)
            if uv is not None:
                mouth_samples.append(uv)
    eye_uv = np.median(np.array(eye_samples), axis=0)
    mouth_uv = np.median(np.array(mouth_samples), axis=0)
    result, evidence = {}, {
        'eye_ring_samples': len(eye_samples), 'mouth_area_samples': len(mouth_samples),
        'eye_cover_uv_median': [round(float(c), 6) for c in eye_uv],
        'mouth_cover_uv_median': [round(float(c), 6) for c in mouth_uv],
        'note': ('UV is geometry-derived, so all four variants share the same pinned texel; the colour '
                 'differs because each variant has its own base-colour texture.'),
    }
    for name in TIMES:
        texture = textures[name]
        entry = {}
        for key, uv, samples in (('eyes', eye_uv, eye_samples), ('mouth', mouth_uv, mouth_samples)):
            values = np.array([sample(texture, s)[:3] for s in samples])
            median = np.median(values, axis=0)
            at_uv = sample(texture, uv)[:3]
            entry[f'{key}_cover_srgb'] = [round(float(c), 5) for c in median]
            entry[f'{key}_uv'] = [round(float(c), 6) for c in uv]
            entry[f'{key}_colour_at_uv_srgb'] = [round(float(c), 5) for c in at_uv]
            entry[f'{key}_median_vs_texel_max_abs_diff'] = round(float(np.abs(median - at_uv).max()), 5)
            entry[f'{key}_spread_srgb'] = [round(float(c), 5) for c in (values.max(0) - values.min(0))]
        result[name] = entry
    log('COVER', json.dumps(result, ensure_ascii=False), flush=True)
    return result, evidence


# -------------------------------------------------------------------------- preview

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def look_at(camera, target):
    direction = Vector(target) - camera.location
    camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()


def overlay_material(atlas, st, name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    coords = nodes.new('ShaderNodeTexCoord')
    mapping = nodes.new('ShaderNodeMapping')
    mapping.inputs['Location'].default_value = (st[2], st[3], 0.)
    mapping.inputs['Scale'].default_value = (st[0], st[1], 1.)
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = atlas
    tex.extension = 'CLIP'
    tex.interpolation = 'Linear'  # rework: never Closest, hard edges would staircase
    links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    links.new(tex.outputs['Alpha'], shader.inputs['Alpha'])
    shader.inputs['Roughness'].default_value = .85
    if 'Metallic' in shader.inputs:
        shader.inputs['Metallic'].default_value = 0.
    links.new(mapping.outputs['Vector'], tex.inputs['Vector'])
    links.new(coords.outputs['UV'], mapping.inputs['Vector'])
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    if hasattr(mat, 'surface_render_method'):
        mat.surface_render_method = 'BLENDED'
    mat.use_backface_culling = False
    return mat


def set_uv_offset(material, st):
    for node in material.node_tree.nodes:
        if node.bl_idname == 'ShaderNodeMapping':
            node.inputs['Location'].default_value = (st[2], st[3], 0.)
            node.inputs['Scale'].default_value = (st[0], st[1], 1.)


def skin_cover_material(texture_name, uv, name):
    """Copy of the skin material pinned to one texel: the Unity `_MainTex_ST=(0,0,u,v)` way."""
    mat = bpy.data.materials['Pet01_Skin'].copy()
    mat.name = name
    tree = mat.node_tree
    tex = next(n for n in tree.nodes if n.type == 'TEX_IMAGE')
    tex.image = bpy.data.images.load(str(TEXTURES / f'Pet01_BaseColor_{texture_name}.png'), check_existing=False)
    tex.interpolation = 'Linear'
    tex.extension = 'REPEAT'
    for link in list(tex.inputs['Vector'].links):
        tree.links.remove(link)
    coords = tree.nodes.new('ShaderNodeTexCoord')
    mapping = tree.nodes.new('ShaderNodeMapping')
    mapping.inputs['Scale'].default_value = (0., 0., 1.)  # collapse the UV to a single texel
    mapping.inputs['Location'].default_value = (float(uv[0]), float(uv[1]), 0.)
    tree.links.new(coords.outputs['UV'], mapping.inputs['Vector'])
    tree.links.new(mapping.outputs['Vector'], tex.inputs['Vector'])
    if hasattr(mat, 'surface_render_method'):
        mat.surface_render_method = 'DITHERED'
    return mat


def flat_cover_material(colour, name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    shader = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if shader is None:
        shader = nodes.new('ShaderNodeBsdfPrincipled')
        output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
        mat.node_tree.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    shader.inputs['Base Color'].default_value = tuple(srgb_to_linear(c) for c in colour) + (1.,)
    shader.inputs['Roughness'].default_value = .85
    if 'Metallic' in shader.inputs:
        shader.inputs['Metallic'].default_value = 0.
    if hasattr(mat, 'surface_render_method'):
        mat.surface_render_method = 'DITHERED'
    return mat


GROUPS = (  # name, eyes cell, mouth cell, cover eyes, cover mouth
    ('neutral', None, None, False, False),
    ('squint_laugh', 'squint', 'laugh', True, True),
    ('closed_serene', 'closed', 'serene', True, True),
    ('divine_serene', 'divine', 'serene', True, True),
    ('rage_roar', 'rage', 'roar', True, True),
    ('nocover_o', None, 'o', False, True),  # rework: the mouth is covered as well now
)
VIEWS = {'neutral': ('front', 'right35', 'side90'),
         'squint_laugh': ('front', 'right35', 'left35'),
         'closed_serene': ('front', 'right35'),
         'divine_serene': ('front', 'right35', 'left35'),
         'rage_roar': ('front', 'right35'),
         'nocover_o': ('front', 'right35')}


def setup_scene(scene):
    render = scene.render
    render.resolution_x, render.resolution_y, render.resolution_percentage = 480, 528, 100
    render.image_settings.file_format = 'PNG'
    render.film_transparent = False
    for name in ('Meshy_Proportion_Reference', 'PreviewFloor'):
        if name in bpy.data.objects:
            bpy.data.objects[name].hide_render = True
    scene.render.engine = 'BLENDER_EEVEE'
    try:
        scene.eevee.taa_render_samples = 64
    except AttributeError:
        pass
    camera = scene.camera
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = .062
    return camera


def preview(cells, covers, scene, rig, mesh, stats, temp_render, surface):
    LOGS.mkdir(parents=True, exist_ok=True)
    eye_atlas = bpy.data.images.load(str(TEXTURES / 'Pet01_FaceEyes.png'), check_existing=False)
    mouth_atlas = bpy.data.images.load(str(TEXTURES / 'Pet01_FaceMouth.png'), check_existing=False)
    eye_mat = overlay_material(eye_atlas, cells['eyes']['squint'], 'Pet01_FaceEyes_Preview')
    mouth_mat = overlay_material(mouth_atlas, cells['mouth']['smile'], 'Pet01_FaceMouth_Preview')
    interpolation = {}
    for mat in (eye_mat, mouth_mat):
        for node in mat.node_tree.nodes:
            if node.type == 'TEX_IMAGE':
                interpolation[f'{mat.name}:{node.name}'] = node.interpolation
    patches = {'eyes': bpy.data.objects['Pet01_FaceEyes'], 'mouth': bpy.data.objects['Pet01_FaceMouth']}
    patches['eyes'].data.materials.clear()
    patches['eyes'].data.materials.append(eye_mat)
    patches['mouth'].data.materials.clear()
    patches['mouth'].data.materials.append(mouth_mat)
    # Round 5: decals cast no shadow (the Unity decal shader has no ShadowCaster pass);
    # low-resolution shadow maps of the thin patch were a suspect for the stepped disc rims.
    for obj in patches.values():
        obj.visible_shadow = False

    slots = {role: i for i, m in enumerate(mesh.data.materials) for role in [MATERIALS.get(m.name, 'other')]}
    original = {i: s.material for i, s in enumerate(mesh.material_slots)}
    classic = 'classic'
    eye_cover = skin_cover_material(classic, covers[classic]['eyes_uv'], 'Pet01_PreviewEyeCover')
    mouth_cover = skin_cover_material(classic, covers[classic]['mouth_uv'], 'Pet01_PreviewMouthCover')

    camera = setup_scene(scene)
    render = scene.render
    shots = []
    for group, eye_cell, mouth_cell, cover_eye, cover_mouth in GROUPS:
        for i, slot in enumerate(mesh.material_slots):
            slot.material = original[i]
        mesh.material_slots[slots['eyes']].material = eye_cover if cover_eye else original[slots['eyes']]
        mesh.material_slots[slots['oral']].material = mouth_cover if cover_mouth else original[slots['oral']]
        patches['eyes'].hide_render = eye_cell is None
        patches['mouth'].hide_render = mouth_cell is None
        if eye_cell:
            set_uv_offset(eye_mat, cells['eyes'][eye_cell])
        if mouth_cell:
            set_uv_offset(mouth_mat, cells['mouth'][mouth_cell])
        for view in VIEWS[group]:
            camera.location = CAMERAS[view]
            look_at(camera, CAMERA_TARGET)
            emulate_view_offset(patches, view)
            out = LOGS / f'{group}_{view}.png'
            render.filepath = str(out)
            bpy.ops.render.render(write_still=True)
            size = out.stat().st_size
            shots.append({'file': str(out.relative_to(ROOT)).replace('\\', '/'), 'group': group,
                          'view': view, 'eyes_cell': eye_cell, 'mouth_cell': mouth_cell,
                          'eye_cover': cover_eye, 'mouth_cover': cover_mouth,
                          'engine': scene.render.engine, 'samples': 64, 'bytes': size})
            log('RENDER', out.name, size, flush=True)

    # Extra profile shot: does the patch float off the cheek at 90 degrees?
    patches['eyes'].hide_render = False
    patches['mouth'].hide_render = False
    set_uv_offset(eye_mat, cells['eyes']['squint'])
    set_uv_offset(mouth_mat, cells['mouth']['laugh'])
    mesh.material_slots[slots['eyes']].material = eye_cover
    mesh.material_slots[slots['oral']].material = mouth_cover
    camera.location = CAMERAS['side90']
    look_at(camera, CAMERA_TARGET)
    emulate_view_offset(patches, 'side90')
    out = LOGS / 'squint_laugh_side90.png'
    render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    shots.append({'file': str(out.relative_to(ROOT)).replace('\\', '/'), 'group': 'squint_laugh',
                  'view': 'side90', 'eyes_cell': 'squint', 'mouth_cell': 'laugh',
                  'eye_cover': True, 'mouth_cover': True, 'engine': scene.render.engine,
                  'samples': 64, 'bytes': out.stat().st_size})
    log('RENDER', out.name, out.stat().st_size, flush=True)

    # Cover comparison: the same shot with the old flat-colour cover, rendered outside the
    # delivery folder and only used for the numeric comparison in the report.
    comparison = {}
    if temp_render:
        TEMP.mkdir(parents=True, exist_ok=True)
        flat_eye = flat_cover_material(covers[classic]['eyes_cover_srgb'], 'Pet01_FlatEyeCover')
        flat_mouth = flat_cover_material(covers[classic]['mouth_cover_srgb'], 'Pet01_FlatMouthCover')
        for i, slot in enumerate(mesh.material_slots):
            slot.material = original[i]
        mesh.material_slots[slots['eyes']].material = flat_eye
        mesh.material_slots[slots['oral']].material = flat_mouth
        patches['eyes'].hide_render = False
        patches['mouth'].hide_render = False
        set_uv_offset(eye_mat, cells['eyes']['squint'])
        set_uv_offset(mouth_mat, cells['mouth']['laugh'])
        camera.location = CAMERAS['front']
        look_at(camera, CAMERA_TARGET)
        emulate_view_offset(patches, 'front')
        temp = TEMP / 'cover_flat_front.png'
        render.filepath = str(temp)
        bpy.ops.render.render(write_still=True)
        comparison = compare_cover(scene, camera, [LOGS / 'squint_laugh_front.png', temp], surface)
        log('COVER_COMPARE', json.dumps(comparison, ensure_ascii=False), flush=True)
        for i, slot in enumerate(mesh.material_slots):
            slot.material = original[i]
    for obj in patches.values():
        obj.location = Vector((0., 0., 0.))
    return shots, interpolation, comparison


def emission_material(colour, name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    emit = nodes.new('ShaderNodeEmission')
    emit.inputs['Color'].default_value = tuple(colour) + (1.,)
    links.new(emit.outputs['Emission'], out.inputs['Surface'])
    return mat


DIAGNOSE = (  # file, eyes cell, mouth cell, view, target, ortho scale
    ('divine_front', 'divine', None, 'front', (0., .022, .186), .030),
    ('divine_right35', 'divine', None, 'right35', (0., .022, .186), .030),
    ('divine_left35', 'divine', None, 'left35', (0., .022, .186), .030),
    ('rage_right35', 'rage', None, 'right35', (0., .022, .186), .030),
    ('laugh_front', None, 'laugh', 'front', (0., .024, .1765), .022),
    ('laugh_right35', None, 'laugh', 'right35', (0., .024, .1765), .022),
    ('roar_front', None, 'roar', 'front', (0., .024, .1765), .022),
)


def diagnose(cells, scene, mesh):
    """False colours: skin green, eye cover blue, oral cover magenta; decals keep their atlas."""
    out_dir = LOGS / 'diagnose'
    out_dir.mkdir(parents=True, exist_ok=True)
    patches = {'eyes': bpy.data.objects['Pet01_FaceEyes'], 'mouth': bpy.data.objects['Pet01_FaceMouth']}
    eye_mat, mouth_mat = patches['eyes'].data.materials[0], patches['mouth'].data.materials[0]
    false = {'skin': emission_material((0., .8, 0.), 'Diag_Skin'),
             'eyes': emission_material((0., .2, 1.), 'Diag_EyeCover'),
             'oral': emission_material((1., 0., 1.), 'Diag_OralCover')}
    original = [s.material for s in mesh.material_slots]
    for slot in mesh.material_slots:
        role = MATERIALS.get(slot.material.name if slot.material else '', 'other')
        if role in false:
            slot.material = false[role]
    camera = scene.camera
    ortho = camera.data.ortho_scale
    for name, eye_cell, mouth_cell, view, target, scale in DIAGNOSE:
        patches['eyes'].hide_render = eye_cell is None
        patches['mouth'].hide_render = mouth_cell is None
        if eye_cell:
            set_uv_offset(eye_mat, cells['eyes'][eye_cell])
        if mouth_cell:
            set_uv_offset(mouth_mat, cells['mouth'][mouth_cell])
        direction = np.array(CAMERAS[view]) - np.array(CAMERA_TARGET)
        direction = direction / np.linalg.norm(direction)
        camera.location = Vector((np.array(target) + direction * .40).tolist())
        look_at(camera, target)
        camera.data.ortho_scale = scale
        emulate_view_offset(patches, view)
        path = out_dir / f'{name}.png'
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        log('DIAG', path.name, path.stat().st_size, flush=True)
    camera.data.ortho_scale = ortho
    for slot, mat in zip(mesh.material_slots, original):
        slot.material = mat
    for obj in patches.values():
        obj.location = Vector((0., 0., 0.))
        obj.hide_render = False


def emulate_view_offset(patches, view):
    """Shift both patches toward the orthographic camera, as the Unity decal shader does."""
    direction = np.array(CAMERAS[view]) - np.array(CAMERA_TARGET)
    direction = direction / np.linalg.norm(direction)
    for obj in patches.values():
        obj.location = Vector((direction * VIEW_OFFSET_M * facing(direction)).tolist())
    bpy.context.view_layer.update()


def screen_point(scene, camera, world):
    co = world_to_camera_view(scene, camera, Vector(world))
    return int(round(co.x * scene.render.resolution_x)), int(round((1. - co.y) * scene.render.resolution_y))


def pixel(img, px, py, radius=2):
    width, height = img.size
    buf = np.empty(width * height * img.channels, dtype=np.float32)
    img.pixels.foreach_get(buf)
    arr = buf.reshape(height, width, img.channels)
    patch = arr[max(py - radius, 0):py + radius + 1, max(px - radius, 0):px + radius + 1, :3]
    return patch.reshape(-1, 3).mean(axis=0)


def compare_cover(scene, camera, paths, surface):
    """Sample the covered regions and a skin reference in two renders (skin copy vs flat)."""
    probes = {}
    for label, (x, z) in (('eye_cover', (0.0155, 0.1856)), ('mouth_cover', (0.0, 0.1780)),
                          ('skin_cheek', (0.0145, 0.1720)), ('skin_forehead', (0.0, 0.1945))):
        y, _tri = surface.cast(x, z)
        probes[label] = (x, y, z)
    images = [bpy.data.images.load(str(p), check_existing=False) for p in paths]
    result = {'probe_points_m': {k: [round(c, 5) for c in v] for k, v in probes.items()}, 'renders': {}}
    for path, img in zip(paths, images):
        values = {}
        for label, world in probes.items():
            px, py = screen_point(scene, camera, world)
            values[label] = [round(float(c), 4) for c in pixel(img, px, py)]
        result['renders'][Path(path).name] = values
    for key in ('eye_cover', 'mouth_cover'):
        deltas = {}
        for name, values in result['renders'].items():
            ref = values['skin_forehead']
            deltas[name] = [round(v - r, 4) for v, r in zip(values[key], ref)]
            deltas[name] = [round(c, 4) for c in deltas[name]]
        result[f'{key}_vs_skin_forehead'] = deltas
    return result


# ----------------------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument('--no-preview', action='store_true')
    parser.add_argument('--no-compare', action='store_true')
    parser.add_argument('--diagnose', action='store_true',
                        help='also render zoomed false-colour shots into Logs/.../diagnose/')
    args = parser.parse_args(argv)

    before = sha(SOURCE)
    log('SOURCE_SHA_BEFORE', before)
    if before != SOURCE_SHA:
        log('FATAL frozen source SHA mismatch: expected', SOURCE_SHA)
        sys.exit(2)
    if bpy.data.filepath and Path(bpy.data.filepath).resolve() != SOURCE.resolve():
        log('FATAL opened file is not the frozen source:', bpy.data.filepath)
        sys.exit(2)

    rig, mesh = bpy.data.objects[RIG], bpy.data.objects[MESH]
    identity = Matrix.Identity(4)
    matrix_check = {}
    for obj in (rig, mesh):
        deviation = max(abs(obj.matrix_world[r][c] - identity[r][c]) for r in range(4) for c in range(4))
        matrix_check[obj.name] = {'matrix_world_identity': deviation < 1e-9, 'max_deviation': deviation,
                                  'matrix_world': [round(v, 6) for row in obj.matrix_world for v in row]}
        log('MATRIX', obj.name, 'identity', deviation < 1e-9, 'deviation', deviation)

    rig.data.pose_position = 'REST'
    if rig.animation_data:
        rig.animation_data.action = None
    scene = bpy.context.scene
    scene.frame_set(1)
    bpy.context.view_layer.update()
    surface = Surface(mesh)

    stats = {}
    for name, spec in PATCHES.items():
        build_patch(name, spec, surface, stats)
    for name in PATCHES:
        verify_patch(name, bpy.data.objects[name], surface, stats)

    textures = {name: load_texture(TEXTURES / f'Pet01_BaseColor_{name}.png') for name in TIMES}
    covers, cover_evidence = cover_colours(surface, textures)

    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    cells = {'eyes': {c['name']: c['main_texture_st'] for c in manifest['atlases']['eyes']['cells']},
             'mouth': {c['name']: c['main_texture_st'] for c in manifest['atlases']['mouth']['cells']}}
    geom = {'eyes': (manifest['atlases']['eyes']['cell_px'], manifest['atlases']['eyes']['size_px']),
            'mouth': (manifest['atlases']['mouth']['cell_px'], manifest['atlases']['mouth']['size_px'])}
    for grip, sts in cells.items():
        cell, size = geom[grip]
        for name, st in sts.items():
            expected = [cell[0] / size[0], cell[1] / size[1]]
            if abs(st[0] - expected[0]) > 1e-6 or abs(st[1] - expected[1]) > 1e-6:
                raise RuntimeError(f'Atlas manifest cell {grip}/{name} does not match its patch aspect')

    # Oral / lip-slit range measured from the mesh, in the mouth patch UV space.
    (mx0, mx1), (mz0, mz1) = MOUTH_RECT['x'], MOUTH_RECT['z']
    oral = surface.tris[surface.slot_role == 'oral']
    oral_pts = surface.verts[np.unique(oral)]
    oral_v = [(float(oral_pts[:, 2].min()) - mz0) / (mz1 - mz0), (float(oral_pts[:, 2].max()) - mz0) / (mz1 - mz0)]
    oral_u = [(float(oral_pts[:, 0].min()) - mx0) / (mx1 - mx0), (float(oral_pts[:, 0].max()) - mx0) / (mx1 - mx0)]
    slit_rows = []
    for z in np.arange(0.1755, 0.1810, 0.0002):
        xs = [x for x in np.arange(-0.013, 0.0131, 0.0002)
              if (lambda h: h[0] is not None and surface.slot_role[h[1]] == 'oral')(surface.cast(x, z))]
        if xs:
            slit_rows.append((z, min(xs), max(xs)))
    slit_z = [min(r[0] for r in slit_rows), max(r[0] for r in slit_rows)]
    slit_x = [min(r[1] for r in slit_rows), max(r[2] for r in slit_rows)]
    oral_range = {
        'oral_v_range': [round(v, 4) for v in oral_v],
        'oral_v_centre': round(0.5 * (oral_v[0] + oral_v[1]), 4),
        'oral_u_range': [round(u, 4) for u in oral_u],
        'lip_slit_z_m': [round(float(z), 5) for z in slit_z],
        'lip_slit_v_range': [round((z - mz0) / (mz1 - mz0), 4) for z in slit_z],
        'lip_slit_x_m': [round(float(x), 5) for x in slit_x],
        'lip_slit_u_range': [round((x - mx0) / (mx1 - mx0), 4) for x in slit_x],
        'cells_z_m': [round(mz0, 5), round(mz1, 5)], 'cells_x_m': [round(mx0, 5), round(mx1, 5)],
    }
    expected = manifest['atlases']['mouth'].get('oral_v_range_expected')
    if expected and max(abs(expected[0] - oral_v[0]), abs(expected[1] - oral_v[1])) > 5e-3:
        raise RuntimeError(f'Atlas mouth design assumed Oral v {expected}, mesh says {oral_v}')
    manifest['atlases']['mouth']['oral_range_measured'] = oral_range
    manifest['atlases']['mouth']['oral_range_verified'] = True
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    log('ORAL_RANGE', json.dumps(oral_range, ensure_ascii=False), flush=True)

    # Atlas ink audit: what is painted on each patch vertex, and did the dropped triangles
    # cut any visible ink away?
    for name, spec in PATCHES.items():
        grip = spec['grip']
        png = 'Pet01_FaceEyes.png' if grip == 'eyes' else 'Pet01_FaceMouth.png'
        cell_px, _size = geom[grip]
        alpha_bottom_up = atlas_alpha(png)
        audit, cut = ink_audit(cells, alpha_bottom_up, name, grip, cell_px)
        stats[name]['atlas_ink'] = audit
        stats[name]['ink_cut_pixels_alpha_gt_0_25'] = cut
        stats[name]['head_extent_in_cell_uv'] = head_extent(surface, spec)
        if grip == 'eyes':
            fp = footprint_audit(surface, cells['eyes'], alpha_bottom_up, name, 'eyes', ('divine', 'rage'))
        else:
            fp = footprint_audit(surface, cells['mouth'], alpha_bottom_up, name, 'oral', ('laugh', 'roar', 'o'))
        stats[name]['original_footprint_cover'] = fp
        log('FOOTPRINT', name, json.dumps(fp), flush=True)
        log('INK', name, 'dropped_tris', stats[name]['triangles_dropped_no_hit'],
            'ink_cut_px', cut, {k: v['painted_vertices'] for k, v in audit.items()}, flush=True)

    bpy.ops.object.select_all(action='DESELECT')
    exported = []
    for name in PATCHES:
        obj = bpy.data.objects[name]
        obj.select_set(True)
        exported.append(name)
        bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(EXPORT), use_selection=True, object_types={'MESH'}, **FBX_OPTIONS)
    log('EXPORT', EXPORT, sha(EXPORT), flush=True)

    shots, interpolation, comparison = ([], {}, {}) if args.no_preview else preview(
        cells, covers, scene, rig, mesh, stats, not args.no_compare, surface)
    if args.diagnose:
        diagnose(cells, scene, mesh)

    after = sha(SOURCE)
    log('SOURCE_SHA_AFTER', after)
    report = {
        'schema_version': 2, 'asset_id': 'Pet01', 'slice': 'P5-A', 'date': '2026-10-27',
        'revision': '2026-10-27 rework after visual review (see Logs/FaceOverlay_20261027/visual_review.md)',
        'generator': 'Tools/Blender/build_pet01_face_overlay.py',
        'source': {'blend': str(SOURCE.relative_to(ROOT)).replace('\\', '/'),
                   'sha256_expected': SOURCE_SHA, 'sha256_before': before, 'sha256_after': after,
                   'unchanged': before == after == SOURCE_SHA, 'saved': False,
                   'note': 'Opened read-only; no save_mainfile / save_as_mainfile call exists in the script.'},
        'blender': bpy.app.version_string,
        'scene': {'rig': RIG, 'mesh': MESH, 'pose_position': 'REST', 'frame': 1,
                  'evaluated_mesh_triangles': int(len(surface.tris)),
                  'material_slots': surface.slot_names, 'matrix_check': matrix_check},
        'projection': {'direction': '-Y from y=+0.2, then displaced along the hit triangle normal',
                       'surface_lift_m': {k: v['lift'] for k, v in PATCHES.items()},
                       'note': ('No interpolation: a quad is dropped when any of its corners missed '
                                'the face, so no vertex floats outside the silhouette. UV 0..1 spans '
                                'one atlas cell (see face_atlas_manifest.json).')},
        'patches': stats,
        'mouth_alignment': oral_range,
        'atlas': {'manifest': 'ArtSource/Characters/Pet01/FaceOverlay/face_atlas_manifest.json',
                  'cells': cells, 'cell_px': {k: v[0] for k, v in geom.items()},
                  'size_px': {k: v[1] for k, v in geom.items()}},
        'cover_colors_srgb': covers, 'cover_sampling': cover_evidence,
        'cover_methods': {
            'delivered_preview': ('copy of the variant skin material, Image Texture UV collapsed by a '
                                  'Mapping node (scale 0, location = median skin UV), so colour and '
                                  'shading match the skin exactly'),
            'previous_flat': ('Principled base colour = sRGB->linear of the sampled median colour; the '
                              'colour matched on paper but read as an olive patch because the flat '
                              'surface lost the skin texture detail and highlights'),
            'unity_recommendation': ('swap the Eyes/Oral material slots for a copy of that variant\'s Skin '
                                     'material and set _MainTex_ST = (0, 0, u, v) with (u, v) from '
                                     'cover_colors_srgb.<variant>.{eyes_uv,mouth_uv}'),
            'measured_comparison': comparison,
        },
        'export': {'fbx': str(EXPORT.relative_to(ROOT)).replace('\\', '/'), 'sha256': sha(EXPORT),
                   'selected_objects': exported, 'object_types': ['MESH'], 'materials': 'none',
                   'fbx_options': FBX_OPTIONS},
        'previews': shots,
        'preview_render': {'engine': shots[0]['engine'] if shots else None,
                           'resolution': [480, 528], 'samples': 64,
                           'camera': 'Pet01_PreviewCamera, orthographic',
                           'ortho_scale_m': 0.062, 'camera_distance_m': 0.40,
                           'image_interpolation': 'Linear', 'alpha_output': 'BLENDED',
                           'views': {'front': 'camera on +Y', 'right35': '35deg to +X',
                                     'left35': '35deg to -X', 'side90': 'profile, shows the float distance'},
                           'output_dir': 'ArtSource/Characters/Pet01/Logs/FaceOverlay_20261027/'},
        'known_gaps': ['Unity wiring (materials, _MainTex_ST switching, Head bone parenting) is P5-B.',
                       'No mouth/eye geometry is animated; expressions are flat decals in front of the face.',
                       'The eyes rectangle (x +-0.024, z 0.1785..0.1965) is wider than the head in its top '
                       'corners; those quads are now dropped, not interpolated, so the patch boundary '
                       'follows the silhouette. See patches.*.atlas_ink / head_extent_in_cell_uv.',
                       'At 60 degrees the far side of the patch can still be occluded by the cheek; '
                       'those counts are reported only.',],
    }
    WORK.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    write_unity_layout(cells, covers)
    log('REPORT', REPORT)
    log('DONE')
    if after != before:
        log('FATAL source changed during the run')
        sys.exit(3)


def write_unity_layout(cells, covers):
    """Compact, JsonUtility-friendly copy of what the Unity setup needs (P5-B)."""
    layout = {
        'generator': 'Tools/Blender/build_pet01_face_overlay.py',
        'viewOffsetMeters': VIEW_OFFSET_M,
        'eyesPatch': 'Pet01_FaceEyes', 'mouthPatch': 'Pet01_FaceMouth', 'headBone': 'Head',
        'eyeCells': [{'name': n, 'st': [float(v) for v in st]} for n, st in cells['eyes'].items()],
        'mouthCells': [{'name': n, 'st': [float(v) for v in st]} for n, st in cells['mouth'].items()],
        'covers': [{'variant': name,
                    'eyesUv': [float(v) for v in covers[name]['eyes_uv']],
                    'mouthUv': [float(v) for v in covers[name]['mouth_uv']]} for name in TIMES],
    }
    UNITY_LAYOUT.write_text(json.dumps(layout, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    log('UNITY_LAYOUT', UNITY_LAYOUT, flush=True)


def coverage_mask(keep, seg, cell_px):
    """UV region still covered after dropping quads, in cell pixels (row 0 = top)."""
    nx, nz = seg
    cw, ch = cell_px
    mask = np.zeros((ch, cw), dtype=bool)
    for i, j in np.argwhere(keep):
        x0, x1 = int(round(j / nx * cw)), int(round((j + 1) / nx * cw))
        y0, y1 = int(round((1. - (i + 1) / nz) * ch)), int(round((1. - i / nz) * ch))
        mask[y0:y1, x0:x1] = True
    return mask


def ink_audit(cells, alpha_bottom_up, patch_name, grip, cell_px):
    """Painted patch vertices per cell and visible ink lost by dropping quads."""
    data = PATCH_DATA[patch_name]
    uv = data['uv']
    keep = data['keep']
    nx, nz = data['seg']
    mask = coverage_mask(keep, (nx, nz), cell_px)
    cw, ch = cell_px
    audit, cut = {}, 0
    for name, st in cells[grip].items():
        u = uv[:, 0] * st[0] + st[2]
        v = uv[:, 1] * st[1] + st[3]
        aw, ah = alpha_bottom_up.shape[1], alpha_bottom_up.shape[0]
        px = np.clip((u * aw).astype(int), 0, aw - 1)
        py = np.clip((v * ah).astype(int), 0, ah - 1)
        a = alpha_bottom_up[py, px]
        audit[name] = {'painted_vertices': int((a > 8. / 255.).sum()),
                       'strong_vertices': int((a > PEAK_ALPHA).sum()),
                       'max_alpha': round(float(a.max()), 4)}
        # visible-ink pixels inside this cell that no surviving quad covers
        x0 = int(round(st[2] * aw))
        y0 = int(round((1. - st[3] - st[1]) * ah))
        cell_alpha = alpha_bottom_up[y0:y0 + ch, x0:x0 + cw][::-1]  # top-down
        if cell_alpha.shape == mask.shape:
            cut += int(((cell_alpha > PEAK_ALPHA) & ~mask).sum())
    return audit, cut


def footprint_audit(surface, cells, alpha_bottom_up, patch_name, role, cell_names, step=0.0002):
    """How much of the original eye / mouth footprint (seen along -Y) each cell really hides.

    A footprint sample counts as covered when a kept quad sits over it and the cell's alpha
    there is above 0.5. Uncovered samples are where the cover-coloured original shows.
    """
    spec = PATCHES[patch_name]
    data = PATCH_DATA[patch_name]
    keep, (nx, nz) = data['keep'], data['seg']
    (x0, x1), (z0, z1) = spec['x'], spec['z']
    aw, ah = alpha_bottom_up.shape[1], alpha_bottom_up.shape[0]
    samples, no_quad = [], 0
    for z in np.arange(z0, z1, step):
        for x in np.arange(x0, x1, step):
            y, tri = surface.cast(x, z)
            if y is None or surface.slot_role[tri] != role:
                continue
            u, v = (x - x0) / (x1 - x0), (z - z0) / (z1 - z0)
            j, i = min(int(u * nx), nx - 1), min(int(v * nz), nz - 1)
            if not keep[i, j]:
                no_quad += 1
            samples.append((u, v, keep[i, j], x, z))
    result = {'footprint_samples': len(samples), 'no_quad': no_quad, 'cells': {}}
    for name in cell_names:
        st = cells[name]
        missing = []
        for u, v, kept, x, z in samples:
            px = min(int((u * st[0] + st[2]) * aw), aw - 1)
            py = min(int((v * st[1] + st[3]) * ah), ah - 1)
            if not kept or alpha_bottom_up[py, px] < .5:
                missing.append((u, v))
        entry = {'uncovered': len(missing),
                 'uncovered_fraction': round(len(missing) / max(len(samples), 1), 4)}
        if missing:
            m = np.array(missing)
            entry['uncovered_u_range'] = [round(float(m[:, 0].min()), 3), round(float(m[:, 0].max()), 3)]
            entry['uncovered_v_range'] = [round(float(m[:, 1].min()), 3), round(float(m[:, 1].max()), 3)]
        result['cells'][name] = entry
    return result


def head_extent(surface, spec, samples=11):
    """Head silhouette as u/v of the patch, i.e. where a -Y ray still finds the face."""
    (x0, x1), (z0, z1) = spec['x'], spec['z']
    rows = []
    for v in np.linspace(0., 1., samples):
        z = z0 + v * (z1 - z0)
        hits = [x for x in np.arange(x0, x1 + 1e-9, .0004) if surface.cast(x, z)[0] is not None]
        rows.append({'v': round(float(v), 4), 'z_m': round(float(z), 5),
                     'u': [round(float((min(hits) - x0) / (x1 - x0)), 4),
                           round(float((max(hits) - x0) / (x1 - x0)), 4)] if hits else None})
    return rows


if __name__ == '__main__':
    main()
