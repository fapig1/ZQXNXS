"""Paint the two-dimensional face expression atlases for the Pet01 (naiwa) overlay.

The frozen model keeps its geometry, UV and weights; expressions are drawn on two
separate "decal" patches (eyes, mouth) that are added by
`Tools/Blender/build_pet01_face_overlay.py`. This script only writes PNG atlases plus a
manifest with the Unity `_MainTex_ST` rects, so the Unity side can pick a cell by
setting the material tiling/offset.

    python Tools/Art/make_pet01_face_atlas.py

Design space vs. output space: every cell is *drawn* in design pixels (eyes 512x192,
mouth 256x128) and then rasterised at `OUTPUT` resolution (eyes 1024x384 since the
2026-10-27 rework, mouth 256x128) with supersampling, so widening the atlas does not
change any drawn proportion or `_MainTex_ST`. Compositing is done in premultiplied alpha
so no white fringe appears in the transparent pixels; the RGB of fully transparent
pixels is set to the line colour on purpose (bilinear filtering would otherwise blend
towards black or white).

Mouth alignment (2026-10-27 rework): the mouth patch now spans z 0.1655..0.1827 with
x +-0.0172 (same 2:1 cell aspect). The Oral surface sits at v 0.6105..0.8401 of that
cell and the visible lip slit at v 0.7665..0.8087 (z 0.1773..0.1787, x +-10.2mm), so the
open mouths (laugh / roar / o) put their upper edge above the Oral top edge and the line
mouths (smile / serene / flat) are centred on the Oral centre line, v = 0.7252.
"""
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
TEXTURES = ROOT / 'ARPet/Assets/_Project/Art/Characters/Pet01/Textures'
WORK = ROOT / 'ArtSource/Characters/Pet01/FaceOverlay'

MARGIN = 6  # transparent border kept inside every cell, output pixels
LINE = (0x3a, 0x24, 0x18)  # deep brown, shared by eyes and mouth line art
SCLERA = (0xf8, 0xf7, 0xfb)
PUPIL = (0x2c, 0x1e, 0x24)
HIGHLIGHT = (0xff, 0xff, 0xff)
GLOW = (0x9f, 0x76, 0xf2)
CAVITY = (0x5a, 0x1e, 0x1e)  # #5a1e1e
TEETH = (0xf9, 0xf6, 0xef)
TONGUE = (0xe2, 0x82, 0x92)

DESIGN = {'eyes': (512, 192), 'mouth': (256, 128)}
OUTPUT = {'eyes': (1024, 384), 'mouth': (256, 128)}
SS = {'eyes': 3, 'mouth': 4}
WORLD_MM = {'eyes': (48.0, 18.0), 'mouth': (34.4, 17.2)}
ATLAS = {'eyes': (2048, 2048), 'mouth': (512, 512)}
GRID = {'eyes': (2, 5), 'mouth': (2, 4)}

# Eye layout inside a cell, in design pixels (frozen head measurements).
EYE_U = (0.227, 0.773)
EYE_V = 0.394
EYE_R = 59
# Mouth: measured from Pet01_Mesh, re-verified by the Blender script every run.
ORAL_V = (0.6105, 0.8401)  # bottom, top of the Oral material inside the mouth cell
SLIT_V = (0.7665, 0.8087)  # visible lip slit inside the mouth cell
SLIT_U = (0.1995, 0.8005)


def mouth_y(v):
    """Cell v (0 = cell bottom) -> design/output row (0 = cell top)."""
    return DESIGN['mouth'][1] * (1. - v)


ORAL_Y = (mouth_y(ORAL_V[1]), mouth_y(ORAL_V[0]))  # top row, bottom row
ORAL_MID_Y = mouth_y(0.5 * (ORAL_V[0] + ORAL_V[1]))
SLIT_Y = (mouth_y(SLIT_V[1]), mouth_y(SLIT_V[0]))
SLIT_HALF_PX = 0.5 * (SLIT_U[1] - SLIT_U[0]) * DESIGN['mouth'][0]
TOP = ORAL_Y[0]  # 20.5 design px, upper edge of the Oral surface
MID = ORAL_MID_Y  # 35.2 design px


def quad(p0, p1, p2, n=48):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])
            for t in (i / n for i in range(n + 1))]


def cubic(p0, p1, p2, p3, n=64):
    out = []
    for i in range(n + 1):
        t = i / n
        s, tt = 1 - t, t
        out.append((s ** 3 * p0[0] + 3 * s * s * tt * p1[0] + 3 * s * tt * tt * p2[0] + tt ** 3 * p3[0],
                    s ** 3 * p0[1] + 3 * s * s * tt * p1[1] + 3 * s * tt * tt * p2[1] + tt ** 3 * p3[1]))
    return out


class Cell:
    """One atlas cell: drawn in design pixels, rasterised at OUTPUT resolution."""

    def __init__(self, kind):
        self.kind = kind
        self.dw, self.dh = DESIGN[kind]
        self.ow, self.oh = OUTPUT[kind]
        self.kx = self.ow / self.dw
        self.ky = self.oh / self.dh
        self.ss = SS[kind]
        self.W = int(round(self.ow * self.ss))
        self.H = int(round(self.oh * self.ss))
        self.rgb = np.zeros((self.H, self.W, 3), np.float32)
        self.alpha = np.zeros((self.H, self.W), np.float32)

    def px(self, x, y):
        return (x * self.kx * self.ss, y * self.ky * self.ss)

    def w(self, width):
        return max(1, int(round(width * self.kx * self.ss)))

    def mask(self):
        return Image.new('L', (self.W, self.H), 0)

    def paint(self, mask, color):
        a = np.asarray(mask, np.float32) / 255.
        c = np.array(color, np.float32) / 255.
        self.rgb = self.rgb * (1. - a)[..., None] + c * a[..., None]
        self.alpha = self.alpha * (1. - a) + a

    def stroke(self, pts, width, color=LINE):
        m = self.mask()
        d = ImageDraw.Draw(m)
        p = [self.px(x, y) for x, y in pts]
        wpx = self.w(width)
        if len(p) > 1:
            d.line(p, fill=255, width=wpx, joint='curve')
        r = wpx / 2.
        for x, y in (p[0], p[-1]):  # round line caps
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
        self.paint(m, color)

    def blob(self, mask, color, blur=0., clip=None):
        if blur:
            mask = mask.filter(ImageFilter.GaussianBlur(blur * self.kx * self.ss))
        if clip is not None:
            mask = ImageChops.multiply(mask, clip)
        self.paint(mask, color)

    def ellipse(self, cx, cy, rx, ry, color, blur=0., clip=None):
        m = self.mask()
        ImageDraw.Draw(m).ellipse([self.px(cx - rx, cy - ry), self.px(cx + rx, cy + ry)], fill=255)
        self.blob(m, color, blur, clip)
        return m

    def ring(self, cx, cy, rx, ry, width, color=LINE):
        m = self.mask()
        ImageDraw.Draw(m).ellipse([self.px(cx - rx, cy - ry), self.px(cx + rx, cy + ry)],
                                  outline=255, width=self.w(width))
        self.paint(m, color)
        return m

    def polygon(self, pts, color, clip=None):
        m = self.mask()
        ImageDraw.Draw(m).polygon([self.px(x, y) for x, y in pts], fill=255)
        self.blob(m, color, 0., clip)
        return m

    def finish(self):
        def shrink(a):
            im = Image.fromarray(np.ascontiguousarray(a, np.float32), mode='F')
            return np.asarray(im.resize((self.ow, self.oh), Image.LANCZOS), np.float32)

        rgb = np.stack([shrink(self.rgb[..., i]) for i in range(3)], axis=-1)
        alpha = shrink(self.alpha)
        rgb = np.clip(rgb, 0., 1.)
        alpha = np.clip(alpha, 0., 1.)
        straight = np.clip(rgb / np.maximum(alpha, 1e-6)[..., None], 0., 1.)
        base = np.array(LINE, np.float32) / 255.
        straight = np.where((alpha < 1. / 510.)[..., None], base, straight)
        edge = np.zeros((self.oh, self.ow), bool)
        edge[:MARGIN, :] = edge[-MARGIN:, :] = True
        edge[:, :MARGIN] = edge[:, -MARGIN:] = True
        straight[edge] = base
        alpha[edge] = 0.
        return Image.fromarray((np.concatenate([straight, alpha[..., None]], axis=-1) * 255. + .5).astype(np.uint8),
                               mode='RGBA')


# ------------------------------------------------------------------ eye expressions

def eye_centres():
    cy = DESIGN['eyes'][1] * (1. - EYE_V)
    return [DESIGN['eyes'][0] * u for u in EYE_U], cy


def cell_squint():
    """`^ ^` happy squint: two thick upward arches."""
    c = Cell('eyes')
    cx, cy = eye_centres()
    for x in cx:
        c.stroke(quad((x - 55, cy + 20), (x, cy - 46), (x + 55, cy + 20)), 11)
    return c.finish()


def cell_closed():
    """Calm closed eyes: thin downward arcs plus two short lashes each."""
    c = Cell('eyes')
    cx, cy = eye_centres()
    for x in cx:
        c.stroke(quad((x - 46, cy - 8), (x, cy + 30), (x + 46, cy - 8)), 8)
        c.stroke([(x - 46, cy - 6), (x - 59, cy + 7)], 5)
        c.stroke([(x + 46, cy - 6), (x + 59, cy + 7)], 5)
    return c.finish()


def cell_divine():
    """Bright pupil-less eye: soft violet halo under a white oval."""
    c = Cell('eyes')
    cx, cy = eye_centres()
    for x in cx:
        # Round 3: the halo stays inside the eyeball rim (EYE_R), the steep crease just
        # outside it is where the round-2 discs were bitten.
        # Round 6: wider blur, same radius, so the halo fades instead of ending in a rim
        # that breaks into steps at grazing angles.
        # Round 7: the white disc covers the whole eyeball like rage (footprint audit), the
        # halo is a soft rim just around it and the white edge itself is blurred, so neither
        # the hard top edge nor a long halo tail is left to comb out at 35 degrees.
        c.ellipse(x, cy, EYE_R + 5, EYE_R + 2, GLOW, blur=5.)
        # Round 8: a wider blur so the top rim (11-1 o'clock) fades like the sides.
        c.ellipse(x, cy, EYE_R + 3, EYE_R, SCLERA, blur=4.5)
        c.ellipse(x - 14, cy - 16, 16, 12, HIGHLIGHT, blur=4.)
    return c.finish()


def cell_rage():
    """Angry glare: white sclera, large dark pupil, highlight, heavy slanting brows."""
    c = Cell('eyes')
    cx, cy = eye_centres()
    for i, x in enumerate(cx):
        inward = 1 if i == 0 else -1  # towards the face centre in cell space
        # Round 5: must cover the whole original eyeball (about 60 x 57 design px, see
        # face_overlay_report.json original_footprint_cover), or its cover colour shows.
        # Round 8: widened symmetrically; the round-7 outward shift left the inner-corner side
        # of the far disc at 35 degrees short (visual_review_round7.md).
        c.ellipse(x, cy, EYE_R + 8, EYE_R + 1, SCLERA, blur=2.4)
        c.ellipse(x, cy + 2, 30, 31, PUPIL)
        c.ellipse(x - 11 * inward - 6, cy - 8, 8.5, 8.5, HIGHLIGHT)
        c.ellipse(x + 12 * inward, cy + 16, 5., 5., HIGHLIGHT)
        # Brow: inner end low, outer end high, thick and slightly curved. It has to stay
        # inside the head silhouette, which narrows quickly above z=0.191m, so the outer
        # tip is pulled in to u~0.17 (see face_overlay_report.json for the ink audit).
        c.stroke(quad((x + 36 * inward, cy - 32), (x + 2 * inward, cy - 50),
                      (x - 28 * inward, cy - 64)), 14)
    return c.finish()


EYE_CELLS = [('squint', cell_squint), ('closed', cell_closed), ('divine', cell_divine), ('rage', cell_rage)]


# ---------------------------------------------------------------- mouth expressions
# All coordinates are mouth-cell design pixels (256x128 over x +-17.2mm,
# z 0.1655..0.1827m). ORAL_Y / MID come from the measured Oral range.

def cell_smile():
    c = Cell('mouth')
    cx = DESIGN['mouth'][0] / 2.
    # corners and control chosen so the arc's midpoint lands exactly on MID (Oral centre).
    c.stroke(quad((cx - 47, MID - 8), (cx, MID + 8), (cx + 47, MID - 8)), 7)
    return c.finish()


def cell_laugh():
    """Open laugh: wide cavity, upper edge above the lip slit, teeth row, pink tongue."""
    c = Cell('mouth')
    cx = DESIGN['mouth'][0] / 2.
    rx = 82.
    top, deep = 11., 104.
    outline = (cubic((cx - rx, top), (cx - 0.45 * rx, top + 14), (cx - 0.12 * rx, top + 16), (cx, top + 16))
               + cubic((cx, top + 16), (cx + 0.45 * rx, top + 14), (cx + 0.62 * rx, top + 12), (cx + rx, top))
               + cubic((cx + rx, top), (cx + 0.78 * rx, deep - 14), (cx + 0.42 * rx, deep), (cx, deep))
               + cubic((cx, deep), (cx - 0.42 * rx, deep), (cx - 0.78 * rx, deep - 14), (cx - rx, top)))
    cavity = c.polygon(outline, CAVITY)
    # Upper teeth: an opaque band that also swallows the original lip slit.
    teeth = c.mask()
    ImageDraw.Draw(teeth).rectangle([c.px(cx - rx + 3, 24), c.px(cx + rx - 3, 42)], fill=255)
    teeth = ImageChops.multiply(teeth, cavity)
    gaps = c.mask()
    gd = ImageDraw.Draw(gaps)
    for dx in (-0.36, 0., 0.36):
        gd.rectangle([c.px(cx + dx * rx - 1.5, 22), c.px(cx + dx * rx + 1.5, 44)], fill=255)
    c.blob(ImageChops.subtract(teeth, gaps), TEETH)
    c.ellipse(cx, deep - 18, 40, 16, TONGUE, clip=cavity)
    return c.finish()


def cell_roar():
    """Roar: wide open, corners level-to-dropped, sharp corners, small upper teeth."""
    c = Cell('mouth')
    cx = DESIGN['mouth'][0] / 2.
    rx = 84.
    # Round 6: upper shoulders raised 3-4px so the upper lip line stays inside the cavity.
    outline = [(cx - rx, 26), (cx - 0.55 * rx, 14), (cx, 12), (cx + 0.55 * rx, 14), (cx + rx, 26),
               (cx + 0.60 * rx, 78), (cx, 100), (cx - 0.60 * rx, 78)]
    cavity = c.polygon(outline, CAVITY)
    teeth = c.mask()
    td = ImageDraw.Draw(teeth)
    for dx in (-0.70, -0.35, 0., 0.35, 0.70):
        x = cx + dx * rx
        td.polygon([c.px(x - 8, 20), c.px(x + 8, 20), c.px(x, 36)], fill=255)
    c.blob(ImageChops.multiply(teeth, cavity), TEETH)
    return c.finish()


def cell_o():
    """Surprised `o`: filled cavity oval wide enough to swallow the whole lip slit."""
    c = Cell('mouth')
    cx = DESIGN['mouth'][0] / 2.
    c.ellipse(cx, MID, SLIT_HALF_PX + 4, 21., CAVITY)
    c.ring(cx, MID, SLIT_HALF_PX + 4, 21., 6)
    return c.finish()


def cell_serene():
    """Serene: thinner and shorter than the smile, still centred on the slit."""
    c = Cell('mouth')
    cx = DESIGN['mouth'][0] / 2.
    c.stroke(quad((cx - 34, MID - 6), (cx, MID + 6), (cx + 34, MID - 6)), 5)
    return c.finish()


def cell_flat():
    cx = DESIGN['mouth'][0] / 2.
    c = Cell('mouth')
    c.stroke([(cx - 58, MID + 1), (cx - 22, MID - 2), (cx + 10, MID + 2), (cx + 58, MID - 1)], 7)
    return c.finish()


MOUTH_CELLS = [('smile', cell_smile), ('laugh', cell_laugh), ('roar', cell_roar),
               ('o', cell_o), ('serene', cell_serene), ('flat', cell_flat)]


# ------------------------------------------------------------------------- assembly

def st(rect, atlas_size):
    """Unity `_MainTex_ST` = [scaleX, scaleY, offsetX, offsetY]; UV origin bottom-left."""
    x, y, w, h = rect
    aw, ah = atlas_size
    return [round(w / aw, 6), round(h / ah, 6), round(x / aw, 6), round((ah - y - h) / ah, 6)]


def build(kind, name, cells, note):
    aw, ah = ATLAS[kind]
    cw, ch = OUTPUT[kind]
    grid = GRID[kind]
    atlas = Image.new('RGBA', (aw, ah), LINE + (0,))
    records = []
    for index, (label, painter) in enumerate(cells):
        col, row = index % grid[0], index // grid[0]
        rect = [col * cw, row * ch, cw, ch]
        sheet = painter()
        atlas.paste(sheet, (rect[0], rect[1]))
        records.append({'index': index, 'name': label, 'atlas': kind,
                        'png': str((TEXTURES / name).relative_to(ROOT)).replace('\\', '/'),
                        'rect_px': rect, 'main_texture_st': st(rect, (aw, ah))})
        alpha = np.asarray(sheet)[..., 3]
        ys, xs = np.nonzero(alpha > 8)
        bbox = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())] if len(xs) else None
        luma = np.nonzero(alpha.max(axis=1) > 8)[0]
        print(f'CELL {name} {index:02d} {label:<7} rect={rect} ink_bbox={bbox}'
              f' ink_rows={[int(luma.min()), int(luma.max())] if len(luma) else None}', flush=True)
    path = TEXTURES / name
    atlas.save(path)
    return path, records, [aw, ah]


def main():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    eyes_path, eyes_cells, eyes_size = build('eyes', 'Pet01_FaceEyes.png', EYE_CELLS, 'eye expressions')
    mouth_path, mouth_cells, mouth_size = build('mouth', 'Pet01_FaceMouth.png', MOUTH_CELLS, 'mouth expressions')
    manifest = {
        'schema_version': 2,
        'asset_id': 'Pet01',
        'date': '2026-10-27',
        'revision': '2026-10-27 rework: eyes atlas 2048 (cell 1024x384), mouth cell re-aligned to the '
                    'measured Oral surface and lip slit',
        'generator': 'Tools/Art/make_pet01_face_atlas.py',
        'note': ('Two decal patches (Pet01_FaceEyes / Pet01_FaceMouth) sample exactly one cell each; '
                 'a patch UV is 0..1 so Unity picks a cell with _MainTex_ST = [scaleX, scaleY, '
                 'offsetX, offsetY]. Cells are laid out left to right, top to bottom; UV v grows '
                 'upwards, so offsetY = (atlasHeight - y - cellHeight) / atlasHeight.'),
        'line_color_hex': '#%02x%02x%02x' % LINE,
        'line_color_rgb255': list(LINE),
        'transparent_pixel_rgb': 'line colour (avoids white fringes under bilinear filtering)',
        'cell_margin_px': MARGIN,
        'supersampling': SS,
        'atlases': {
            'eyes': {
                'png': str(eyes_path.relative_to(ROOT)).replace('\\', '/'),
                'size_px': eyes_size, 'cell_px': list(OUTPUT['eyes']), 'design_px': list(DESIGN['eyes']),
                'grid': list(GRID['eyes']), 'world_mm_per_cell': list(WORLD_MM['eyes']),
                'patch_uv_aspect': round(OUTPUT['eyes'][0] / OUTPUT['eyes'][1], 4),
                'cell_centres_uv': {'u': list(EYE_U), 'v': EYE_V, 'radius_design_px': EYE_R,
                                    'radius_output_px': round(EYE_R * OUTPUT['eyes'][0] / DESIGN['eyes'][0], 2)},
                'cells': eyes_cells,
            },
            'mouth': {
                'png': str(mouth_path.relative_to(ROOT)).replace('\\', '/'),
                'size_px': mouth_size, 'cell_px': list(OUTPUT['mouth']), 'design_px': list(DESIGN['mouth']),
                'grid': list(GRID['mouth']), 'world_mm_per_cell': list(WORLD_MM['mouth']),
                'patch_uv_aspect': round(OUTPUT['mouth'][0] / OUTPUT['mouth'][1], 4),
                'oral_v_range_expected': [ORAL_V[0], ORAL_V[1]],
                'oral_v_centre_expected': round(0.5 * (ORAL_V[0] + ORAL_V[1]), 4),
                'lip_slit_uv_expected': {'v': list(SLIT_V), 'u': list(SLIT_U)},
                'measured_from': 'Pet01_Mesh (material Pet01_Oral faces + frontmost-hit scan), '
                                 're-verified by Tools/Blender/build_pet01_face_overlay.py',
                'cells': mouth_cells,
            },
        },
    }
    out = WORK / 'face_atlas_manifest.json'
    out.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('ATLAS', eyes_path.name, eyes_size, 'cells', len(eyes_cells), 'cell', OUTPUT['eyes'], flush=True)
    print('ATLAS', mouth_path.name, mouth_size, 'cells', len(mouth_cells), 'cell', OUTPUT['mouth'], flush=True)
    print('MANIFEST', out, flush=True)


if __name__ == '__main__':
    main()
