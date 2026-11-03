# 第四轮修图工具：彻底消除轴对齐修图痕迹
# 手段（按任务书要求）：
#   1) cv2.inpaint(Telea) + 掩码内低频平滑 + 匹配微噪声 —— 大范围平滑重建
#   2) Catmull-Rom 样条曲线轮廓重塑（alpha 重描，边缘 1.2px AA），杜绝直边/直角
#   3) ≥20px 高斯羽化混合（核心区全替换，20px 过渡带在掩码内部）
#   4) 光晕径向重建：椭圆拟合 + 干净扇区采样 LUT + 径向渐变重画
# 用法: python retouch2.py [--apply]
import numpy as np
import cv2
from PIL import Image, ImageDraw, ImageFilter
import os, sys

BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work")
AUD = os.path.join(WORK, "audit4")
APPLY = "--apply" in sys.argv

def load(name):
    return Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")

def save(name, im):
    im.save(os.path.join(WORK, f"role_{name}.png"))

# ---------- 几何工具 ----------

def catmull_rom(pts, samples=26, closed=False):
    """Catmull-Rom 样条：返回密集折线（自然端点条件）。"""
    pts = [tuple(map(float, p)) for p in pts]
    if closed:
        pts = pts + [pts[0], pts[1]]
        ext = [pts[0]] + pts + [pts[-1]]
    else:
        ext = [pts[0]] + pts + [pts[-1]]
    out = []
    n = len(ext) - 3
    for i in range(1, n):
        p0, p1, p2, p3 = ext[i-1], ext[i], ext[i+1], ext[i+2]
        for t in np.linspace(0, 1, samples, endpoint=False):
            t2, t3 = t*t, t*t*t
            x = 0.5*((2*p1[0]) + (-p0[0]+p2[0])*t + (2*p0[0]-5*p1[0]+4*p2[0]-p3[0])*t2 + (-p0[0]+3*p1[0]-3*p2[0]+p3[0])*t3)
            y = 0.5*((2*p1[1]) + (-p0[1]+p2[1])*t + (2*p0[1]-5*p1[1]+4*p2[1]-p3[1])*t2 + (-p0[1]+3*p1[1]-3*p2[1]+p3[1])*t3)
            out.append((x, y))
    out.append(pts[-1])
    return out

def poly_mask(shape, polys, offset=(0, 0)):
    """多边形栅格化为 bool 掩码。polys: list of [(x,y),...] 或密集折线。"""
    m = Image.new("L", (shape[1], shape[0]), 0)
    d = ImageDraw.Draw(m)
    ox, oy = offset
    for poly in polys:
        d.polygon([(x-ox, y-oy) for x, y in poly], fill=255)
    return np.asarray(m) > 0

def curve_band_mask(shape, curve, width, offset=(0, 0)):
    """沿曲线的带状掩码（宽 width px，圆形笔刷沿样条扫过）。"""
    m = Image.new("L", (shape[1], shape[0]), 0)
    d = ImageDraw.Draw(m)
    ox, oy = offset
    for x, y in curve:
        d.ellipse([x-ox-width/2, y-oy-width/2, x-ox+width/2, y-oy+width/2], fill=255)
    return np.asarray(m) > 0

# ---------- 填充工具 ----------

def matched_noise(rgb, mask, ring=(18, 46), gain=0.35, seed=7):
    """从掩码外环带估计高频纹理幅度，在掩码内加匹配微噪声，避免死平。"""
    rng = np.random.default_rng(seed)
    h, w, _ = rgb.shape
    m = mask.astype(np.uint8)
    ring_mask = cv2.dilate(m, np.ones((ring[1]*2+1, ring[1]*2+1), np.uint8)) > 0
    inner = cv2.erode(m, np.ones((ring[0]*2+1, ring[0]*2+1), np.uint8)) > 0
    ring_mask = ring_mask & ~inner & ~mask
    if ring_mask.sum() < 50:
        return rgb
    gray = rgb.mean(axis=2)
    hp = gray - cv2.GaussianBlur(gray, (0, 0), 3)
    std = hp[ring_mask].std()
    noise = rng.normal(0, max(std*gain, 0.3), (h, w)).astype(np.float32)
    # 噪声轻微模糊以免过颗粒
    noise = cv2.GaussianBlur(noise, (0, 0), 0.8)[..., None]
    out = rgb.copy()
    out[mask] = np.clip(out[mask] + np.broadcast_to(noise, (h, w, 3))[mask], 0, 255)
    return out

def inpaint_lowfreq(rgb, mask, alpha=None, sigma_lf=12, edge_band=16, noise=True):
    """Telea inpaint + 掩码内低频平滑。rgb float32 HWC, mask bool。
    alpha: 若给出，alpha<40 不作为有效源（防透明黑拉入）。
    返回填充后的 rgb（仅掩码内被修改）。"""
    h, w, _ = rgb.shape
    work = rgb.copy()
    m8 = (mask.astype(np.uint8))*255
    if alpha is not None:
        invalid = alpha < 40
        # 无效源像素先一并 inpaint，再仅取掩码内结果
        m8[invalid] = 255
    # 多次小半径 Telea 比一次大半径更少拖尾
    inp = cv2.inpaint(work.astype(np.uint8), m8, 5, cv2.INPAINT_TELEA)
    inp = inp.astype(np.float32)
    # 掩码内低频平滑：masked-normalized Gaussian
    mf = cv2.GaussianBlur((m8 > 0).astype(np.float32), (0, 0), sigma_lf)
    mf = np.maximum(mf, 1e-4)[..., None]
    lf = cv2.GaussianBlur(inp, (0, 0), sigma_lf) / mf
    # 低频场与 inpaint 的过渡带（近边界保留 inpaint 的边界连续色，核心用低频）
    core = cv2.erode(m8, np.ones((edge_band*2+1, edge_band*2+1), np.uint8)) > 0
    dist = cv2.distanceTransform((m8 > 0).astype(np.uint8), cv2.DIST_L2, 3)
    w_lf = np.clip(dist / float(edge_band), 0, 1)[..., None]  # 边界0 → 核心1
    out = inp*(1-w_lf) + lf*w_lf
    out[~mask] = rgb[~mask]
    if noise:
        out = matched_noise(out, mask)
    return out

def feather_apply(arr, new_rgb, mask, erode=10, sigma=8, garbage_guard=True):
    """把 new_rgb 按 ≥20px 羽化混合进 arr（HWC uint8，含 alpha 通道）。
    核心（内缩 erode）全替换，过渡带在掩码内部。
    garbage_guard: 掩码内原透明像素(alpha<40)的 RGB 先以填充值打底，防止羽化混入透明黑。"""
    m8 = mask.astype(np.uint8)*255
    core = cv2.erode(m8, np.ones((erode*2+1, erode*2+1), np.uint8)) > 0
    soft = cv2.GaussianBlur(m8, (0, 0), sigma).astype(np.float32)/255.0
    bm = np.where(core, 1.0, soft)[..., None]
    if garbage_guard:
        bad = mask & (arr[..., 3] < 40)
        if bad.any():
            arr = arr.copy()
            arr[..., :3][bad] = np.clip(new_rgb[bad], 0, 255).astype(np.uint8)
    arr[..., :3] = np.clip(arr[..., :3].astype(np.float32)*(1-bm) + new_rgb*bm, 0, 255).astype(np.uint8)
    return arr

# ---------- 高层操作 ----------

def fill_zones(im, zones, sigma_lf=12, erode=10, sigma=8, use_alpha_guard=True):
    """zones: list of (rect, polys) 多边形填充（颜色重建，alpha 不动）"""
    a = np.array(im)
    for rect, polys in zones:
        x0, y0, x1, y1 = rect
        sub = a[y0:y1, x0:x1]
        mask = poly_mask(sub.shape[:2], polys, offset=(x0, y0))
        # 只填充原本不透明或紧邻不透明的区域（防越界涂到透明背景）
        guard = sub[..., 3] > 40
        guard = cv2.dilate(guard.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
        mask = mask & guard if use_alpha_guard else mask
        if mask.sum() < 8:
            continue
        rgb = sub[..., :3].astype(np.float32)
        new = inpaint_lowfreq(rgb, mask, alpha=sub[..., 3] if use_alpha_guard else None, sigma_lf=sigma_lf)
        a[y0:y1, x0:x1] = feather_apply(sub, new, mask, erode=erode, sigma=sigma)
    return Image.fromarray(a)

def force_alpha_fill(im, rect, polys, sigma_lf=12, erode=8, sigma=6):
    """闭合 alpha 洞：polys 内 alpha=255（边缘 2px 渐变），仅对原透明像素做颜色重建。
    用平滑多边形（样条密集化）避免直边。"""
    a = np.array(im)
    x0, y0, x1, y1 = rect
    sub = a[y0:y1, x0:x1]
    mask = poly_mask(sub.shape[:2], polys, offset=(x0, y0))
    # 仅对原本透明/半透明的像素重建颜色（不重涂已不透明内容，如手部）
    fill_m = mask & (sub[..., 3] < 200)
    if fill_m.sum() < 8:
        return im
    # 边缘 2px 软过渡
    m8 = mask.astype(np.uint8)*255
    soft = cv2.GaussianBlur(m8, (0, 0), 1.2).astype(np.float32)/255.0
    new_a = np.maximum(sub[..., 3].astype(np.float32), soft*255)
    rgb = sub[..., :3].astype(np.float32)
    new = inpaint_lowfreq(rgb, fill_m, alpha=sub[..., 3], sigma_lf=sigma_lf)
    # 颜色羽化
    sub2 = sub.copy()
    sub2[..., 3] = new_a.astype(np.uint8)
    a[y0:y1, x0:x1] = feather_apply(sub2, new, fill_m, erode=erode, sigma=sigma)
    return Image.fromarray(a)

def reshape_edge(im, rect, curve, outside_poly, fill_band=10, sigma_lf=10):
    """沿样条曲线重塑轮廓：
    curve: 密集折线（role 坐标，新轮廓线）。
    outside_poly: role 坐标多边形（曲线外侧+收口）→ 内部 alpha=0。
    新轮廓处 alpha 以 1.2px AA 过渡；newly-opaque 像素颜色 inpaint。"""
    a = np.array(im)
    x0, y0, x1, y1 = rect
    sub = a[y0:y1, x0:x1]
    h, w = sub.shape[:2]
    curve_l = [(x-x0, y-y0) for x, y in curve]
    poly_l = [(px-x0, py-y0) for px, py in outside_poly]
    outside = poly_mask((h, w), [poly_l])
    # 新 alpha：inside→255, outside→0
    old_a = sub[..., 3].astype(np.float32)
    new_a = np.where(outside, 0.0, 255.0)
    # AA：沿边界 1.2px
    edge8 = np.zeros((h, w), np.uint8)
    d = ImageDraw.Draw(Image.fromarray(edge8))
    d.line(curve_l, fill=255, width=2)
    edge8 = np.asarray(edge8)
    aa = cv2.GaussianBlur(edge8, (0, 0), 1.2).astype(np.float32)/255.0
    new_a_f = new_a*(1-aa) + old_a*aa
    # 颜色：newly-opaque（原透明、新不透明）需填色；newly-transparent 直接丢色
    newly_op = (~outside) & (sub[..., 3] < 40)
    # 带状颜色重建：沿曲线两侧 fill_band 像素
    band = curve_band_mask((h, w), curve_l, fill_band*2)
    band = band & (new_a_f > 128) & ~outside
    rgb = sub[..., :3].astype(np.float32)
    cmask = band | newly_op
    if cmask.sum() > 8:
        new_rgb = inpaint_lowfreq(rgb, cmask, alpha=sub[..., 3], sigma_lf=sigma_lf, noise=False)
        # 羽化混合（核心全替换，过渡带在掩码内），避免掩码边界感
        rgb = feather_apply(sub, new_rgb, cmask, erode=5, sigma=6)[..., :3].astype(np.float32)
    sub[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    sub[..., 3] = np.clip(new_a_f, 0, 255).astype(np.uint8)
    a[y0:y1, x0:x1] = sub
    return Image.fromarray(a)

def radial_glow_rebuild(im, rect, zones, center, axes, ang_deg=0.0, r0=0.98, r1=1.5, sector=(35, 145)):
    """光晕径向重建：以 center/axes(半轴)/ang_deg 的拟合椭圆归一化半径 rr；
    rr∈[r0,r1] 的颜色 LUT 从干净扇区（向下为正，默认下方 35°~145°）采样并平滑。
    zones: [(zrect, polys)] —— polys 为 role 坐标，在多边形内按 LUT 重画颜色（alpha 不动）。
    rect: 参与拟合/采样的 role 坐标窗口。"""
    a = np.array(im)
    ix0, iy0, ix1, iy1 = rect
    cx, cy = center[0]-ix0, center[1]-iy0
    ax, ay = axes
    th = np.deg2rad(ang_deg)
    yy, xx = np.mgrid[0:iy1-iy0, 0:ix1-ix0]   # 局部坐标
    dx = xx - cx; dy = yy - cy
    u = dx*np.cos(th) + dy*np.sin(th)
    v = -dx*np.sin(th) + dy*np.cos(th)
    rr = np.sqrt((u/ax)**2 + (v/ay)**2)
    ang = np.degrees(np.arctan2(dy, dx))
    sub = a[iy0:iy1, ix0:ix1].astype(np.float32)
    # LUT 采样：干净扇区
    sec = (ang >= sector[0]) & (ang <= sector[1]) & (rr >= r0*0.92) & (rr <= r1*1.05)
    nb = 90
    edges = np.linspace(r0*0.92, r1*1.05, nb+1)
    lut = np.zeros((nb, 3), np.float32)
    for i in range(nb):
        sel = sec & (rr >= edges[i]) & (rr < edges[i+1])
        if sel.sum() > 30:
            lut[i] = sub[..., :3][sel].mean(axis=0)
        else:
            lut[i] = lut[i-1] if i > 0 else 0
    k = 5
    ker = np.ones(k)/k
    for c in range(3):
        lut[:, c] = np.convolve(np.pad(lut[:, c], (k//2, k//2), mode="edge"), ker, mode="valid")
    idx = np.clip(((rr - edges[0]) / (edges[1]-edges[0])).astype(int), 0, nb-1)
    lut_f = lut[idx]
    for zrect, polys in zones:
        zx0, zy0, zx1, zy1 = zrect
        zx0, zy0 = max(zx0, ix0), max(zy0, iy0)
        zx1, zy1 = min(zx1, ix1), min(zy1, iy1)
        zsub = a[zy0:zy1, zx0:zx1]
        zmask = poly_mask(zsub.shape[:2], [polys], offset=(zx0, zy0))
        rsub = rr[zy0-iy0:zy1-iy0, zx0-ix0:zx1-ix0]
        lsub = lut_f[zy0-iy0:zy1-iy0, zx0-ix0:zx1-ix0]
        sel = zmask & (rsub >= r0*0.95) & (rsub <= r1*1.02) & (zsub[..., 3] > 40)
        if sel.sum() < 8:
            print("  glow zone skipped (few px)", zrect)
            continue
        new_rgb = np.where(sel[..., None], lsub, zsub[..., :3].astype(np.float32))
        a[zy0:zy1, zx0:zx1] = feather_apply(zsub, new_rgb, sel, erode=8, sigma=7)
    return Image.fromarray(a)
