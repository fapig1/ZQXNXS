# 2D 修饰：对 role 渲染的手术缝瑕疵做扩散修复/轮廓修剪（卡面原画正当工序）
# 用法: python retouch.py [--apply]
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import os, sys

BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work")
APPLY = "--apply" in sys.argv

def pull_fill(rgb, mask, maxd=260, alpha=None):
    """四方向最近有效像素拉取 + 距离平方权插值，保留两侧渐变。rgb float, mask bool。
    alpha: 若给出，alpha<128 的像素不作为有效源（防止透明黑被拉入）。"""
    h, w, _ = rgb.shape
    if alpha is not None:
        mask = mask | (alpha < 128)
    INF = 10**9
    dist = np.full((h, w), INF, dtype=np.float64)
    colr = np.zeros_like(rgb)
    # 四个方向扫描
    acc_d = np.full((h, w), INF); acc_c = np.zeros_like(rgb)
    # 左→右
    for r in range(h):
        last = -1; lc = None
        row_m = mask[r]; row_v = rgb[r]
        for c in range(w):
            if not row_m[c]: last = c; lc = row_v[c]
            elif last >= 0: acc_d[r,c] = c-last; acc_c[r,c] = lc
    # 右→左
    d2 = np.full((h, w), INF); c2 = np.zeros_like(rgb)
    for r in range(h):
        last = -1; lc = None
        row_m = mask[r]; row_v = rgb[r]
        for c in range(w-1, -1, -1):
            if not row_m[c]: last = c; lc = row_v[c]
            elif last >= 0: d2[r,c] = last-c; c2[r,c] = lc
    upd = d2 < acc_d; acc_d[upd] = d2[upd]; acc_c[upd] = c2[upd]
    # 上→下
    d3 = np.full((h, w), INF); c3 = np.zeros_like(rgb)
    for c in range(w):
        last = -1; lc = None
        col_m = mask[:,c]; col_v = rgb[:,c]
        for r in range(h):
            if not col_m[r]: last = r; lc = col_v[r]
            elif last >= 0: d3[r,c] = r-last; c3[r,c] = lc
    upd = d3 < acc_d; acc_d[upd] = d3[upd]; acc_c[upd] = c3[upd]
    # 下→上
    d4 = np.full((h, w), INF); c4 = np.zeros_like(rgb)
    for c in range(w):
        last = -1; lc = None
        col_m = mask[:,c]; col_v = rgb[:,c]
        for r in range(h-1, -1, -1):
            if not col_m[r]: last = r; lc = col_v[r]
            elif last >= 0: d4[r,c] = last-r; c4[r,c] = lc
    upd = d4 < acc_d; acc_d[upd] = d4[upd]; acc_c[upd] = c4[upd]
    # 组合：四方向 w=1/d^2
    out = rgb.copy()
    ys, xs = np.where(mask)
    for y, x in zip(ys, xs):
        num = np.zeros(3); den = 0.0
        for d, cc in ((acc_d[y,x], acc_c[y,x]), (d2[y,x], c2[y,x]), (d3[y,x], c3[y,x]), (d4[y,x], c4[y,x])):
            if d < maxd:
                wgt = 1.0/(d*d+1.0); num += wgt*cc; den += wgt
        if den > 0: out[y,x] = num/den
    return out

def smooth_mask_edge(mask, rad=2):
    from scipy import ndimage  # may not exist; fallback
    return mask

def load(name):
    return Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")

def save(name, im):
    im.save(os.path.join(WORK, f"role_{name}.png"))

def apply_region(name, rect, mask_polys, trim_polys, iters=1200, feather=1.5, alpha_rule="close"):
    im = load(name)
    a = np.array(im)
    x0,y0,x1,y1 = rect
    sub = a[y0:y1, x0:x1].astype(np.float64)
    h,w = sub.shape[:2]
    mask = np.zeros((h,w), bool)
    dr = ImageDraw.Draw(Image.new("L",(w,h)))
    tmp = Image.new("L",(w,h)); dr = ImageDraw.Draw(tmp)
    for poly in mask_polys:
        dr.polygon([(px-x0,py-y0) for px,py in poly], fill=255)
    mask = np.array(tmp) > 0
    if trim_polys:
        tmp2 = Image.new("L",(w,h)); dr2 = ImageDraw.Draw(tmp2)
        for poly in trim_polys:
            dr2.polygon([(px-x0,py-y0) for px,py in poly], fill=255)
        trim = np.array(tmp2) > 0
    else:
        trim = np.zeros((h,w), bool)
    # RGB 扩散修复（trim 区域也一起修，反正会透明）
    rgb = sub[:,:,:3].copy()
    fill_mask = mask & ~trim
    filled = pull_fill(rgb, fill_mask, alpha=sub[:,:,3])
    # 羽化：掩码边缘 2px 与原图混合，消除多边形硬边
    from PIL import ImageFilter
    mimg = Image.fromarray((fill_mask*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.6))
    bm = np.asarray(mimg).astype(np.float64)[..., None]/255.0
    bm = np.maximum(bm, fill_mask[...,None].astype(np.float64))
    # 掩码内预乘高斯模糊：消除拉取的扫描线带纹与内部接缝
    ma = sub[:,:,3].astype(np.float64)/255.0
    ma = np.maximum(ma, fill_mask.astype(np.float64))
    pmg = Image.fromarray((np.clip(filled,0,255) * ma[...,None]).astype(np.uint8))
    aag = Image.fromarray((ma*255).astype(np.uint8))
    pmg = pmg.filter(ImageFilter.GaussianBlur(2.5)); aag = aag.filter(ImageFilter.GaussianBlur(2.5))
    pmg = np.asarray(pmg).astype(np.float64); aag = np.asarray(aag).astype(np.float64)/255.0
    smooth = np.clip(pmg / np.maximum(aag, 1e-3)[...,None], 0, 255)
    mb = Image.fromarray((fill_mask*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(2))
    mbm = np.asarray(mb).astype(np.float64)[...,None]/255.0
    filled = filled*(1-mbm) + smooth*mbm
    sub[:,:,:3] = filled*bm + sub[:,:,:3]*(1-bm)
    # alpha 语义：close=掩码内不透明区+6px 邻域闭合（防轮廓鼓包）；force255=全部不透明；
    # keep=仅填色不动 alpha；trim 区一律透明
    op = a[y0:y1, x0:x1, 3] > 0
    if alpha_rule == "force255":
        sub[:,:,3][fill_mask] = 255
    elif alpha_rule == "close":
        grow = op.copy()
        for _ in range(6):
            g = grow.copy()
            g[1:,:] |= grow[:-1,:]; g[:-1,:] |= grow[1:,:]
            g[:,1:] |= grow[:,:-1]; g[:,:-1] |= grow[:,1:]
            grow = g
        sub[:,:,3][fill_mask & grow] = 255
    sub[:,:,3][trim] = 0
    # trim 边缘 1px 羽化
    timg = Image.fromarray(((~trim)*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
    ta = np.asarray(timg).astype(np.float64)
    sub[:,:,3] = np.where(trim | (bm[...,0]>0.02), sub[:,:,3], np.minimum(sub[:,:,3], ta))
    # trim: alpha=0（带 1px 羽化）
    if trim.any():
        al = sub[:,:,3].copy()
        al[trim] = 0
        sub[:,:,3] = al
    a[y0:y1, x0:x1] = np.clip(sub,0,255).astype(np.uint8)
    out = Image.fromarray(a)
    if APPLY: save(name, out)
    return out

def smooth_rim_edge(name, box, thresh=128, win=29, band=7, max_dev=14, color_blur=3.0):
    """box 内角色的下缘（自上而下首个 不透明->透明 转变）锯齿平滑：
    alpha 边缘重建为滑动平均曲线，边缘带颜色模糊保留自然暗影。"""
    im = load(name)
    a = np.array(im)
    x0,y0,x1,y1 = box
    sub = a[y0:y1, x0:x1].astype(np.float64)
    h,w = sub.shape[:2]
    al = sub[:,:,3]
    edge = np.full(w, np.nan)
    for c in range(w):
        col = al[:,c]
        for r in range(h-1):
            if col[r] >= thresh and col[r+1] < thresh:
                edge[c] = r; break
    valid = ~np.isnan(edge)
    if valid.sum() < 10:
        print("  rim: too few valid columns"); return
    k = win//2
    sm = edge.copy()
    for c in range(w):
        if not valid[c]: continue
        lo, hi = max(0,c-k), min(w,c+k+1)
        seg = edge[lo:hi][valid[lo:hi]]
        m = seg.mean()
        lo_v = np.percentile(seg, 15); hi_v = np.percentile(seg, 85)
        sm[c] = min(max(m, lo_v-max_dev), hi_v+max_dev)
    sm = np.where(valid, sm, np.nan)
    # NaN 列用邻近有效列填充
    idx = np.arange(w)
    sm = np.interp(idx, idx[valid], sm[valid])
    # 重建 alpha 与颜色
    blur_img = Image.fromarray(sub.astype(np.uint8)).filter(ImageFilter.GaussianBlur(color_blur))
    blur = np.asarray(blur_img).astype(np.float64)
    for c in range(w):
        e_old = int(round(edge[c])) if valid[c] else int(round(sm[c]))
        e_new = int(round(sm[c]))
        hi = max(e_old, e_new)
        # alpha 重建
        col_a = np.full(h, al[:,c].mean())
        col_a[:e_new] = 255
        col_a[e_new:] = 0
        sub[:,c,3] = col_a
        # 颜色：边缘带用模糊后的颜色（去锯齿），其余保持
        b0, b1 = max(0, e_new-band), min(h, e_new+3)
        sub[b0:b1, c, :3] = blur[b0:b1, c, :3]
    a[y0:y1, x0:x1] = np.clip(sub,0,255).astype(np.uint8)
    out = Image.fromarray(a)
    if APPLY: save(name, out)
    return out

def smooth_bottom_edge(name, box, window=25, band=6):
    """把 box 内角色下缘（自上而下首个 不透明->透明 转变）锯齿边平滑：
    削尖刺、填凹口，颜色从边缘上方延续。"""
    im = load(name)
    a = np.array(im)
    x0,y0,x1,y1 = box
    sub = a[y0:y1, x0:x1]
    h,w = sub.shape[:2]
    al = sub[:,:,3]
    edge = np.full(w, -1, dtype=int)
    for c in range(w):
        col = al[:,c] > 128
        for r in range(h-1):
            if col[r] and not col[r+1]:
                edge[c] = r; break
        if edge[c] == -1 and col[0]: edge[c] = h-1
    valid = edge >= 0
    # 滑动平均平滑
    es = edge.astype(np.float64).copy()
    es[~valid] = np.nan
    k = window//2
    sm = np.copy(es)
    for c in range(w):
        seg = es[max(0,c-k):c+k+1]
        sm[c] = np.nanmean(seg)
    sm = np.where(valid, sm, edge)
    for c in range(w):
        if not valid[c]: continue
        oe, se = int(round(edge[c])), int(round(sm[c]))
        if oe > se:      # 向下的尖刺 → 修剪
            sub[se:oe+1, c, 3] = 0
        elif oe < se:    # 向上的凹口 → 延伸并补色
            sub[oe:se+1, c, :3] = sub[max(0,oe-3), c, :3]
            sub[oe:se+1, c, 3] = 255
    # 边缘带轻微模糊去锯齿残留
    from PIL import ImageFilter
    band_img = Image.fromarray(sub)
    blurred = band_img.filter(ImageFilter.GaussianBlur(1.2))
    m = Image.new("L",(w,h),0); dm = ImageDraw.Draw(m)
    for c in range(w):
        if valid[c]:
            se = int(round(sm[c]))
            dm.line([(c, max(0,se-band)),(c, min(h-1,se+band//2))], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(1.5))
    sub[:,:,:3] = np.array(blurred)[:,:,:3]* (np.array(m)[...,None]/255.0) + sub[:,:,:3]*(1-np.array(m)[...,None]/255.0)
    a[y0:y1, x0:x1] = sub
    out = Image.fromarray(a)
    if APPLY: save(name, out)
    return out

REGS = [
 # 1. calm 左肩
 ("calm", (385,425,500,580),
  [ [(404,436),(470,432),(478,470),(480,512),(492,508),(492,526),(470,524),(462,566),(436,568),(410,505)] ], []),
 # 2. rage 右肩
 ("rage", (725,455,845,655),
  [ [(744,472),(824,478),(822,504),(748,502)],
    [(734,494),(754,496),(768,560),(782,610),(788,644),(772,648),(760,600),(748,550),(734,508)] ],
  [ [(744,470),(838,470),(838,486),(800,481),(764,474),(744,494)] ]),
 # 4. divine 左肩
 ("divine", (300,430,410,625),
  [ [(316,452),(376,452),(392,492),(402,516),(384,556),(366,608),(346,614),(330,548),(312,488)] ],
  [ [(312,438),(372,436),(372,458),(312,460)] ]),
 # 5. divine 右肩
 ("divine", (688,465,800,615),
  [ [(695,478),(738,470),(748,490),(792,498),(788,560),(758,600),(735,606),(700,560)] ], []),
 # 6. divine 左体侧扇
 ("divine", (270,640,368,838),
  [ [(283,655),(345,648),(352,700),(358,760),(348,822),(300,828),(278,760)] ], []),
 # 7. divine 右楔形
 ("divine", (778,722,845,820),
  [ [(788,736),(818,732),(822,772),(812,806),(790,808),(784,766)] ], []),
 # 8. classic 右肩
 ("classic", (672,428,782,655),
  [ [(686,436),(768,434),(772,492),(700,496)],
    [(678,498),(702,498),(714,570),(708,644),(692,648),(684,562)] ],
  [ [(694,436),(770,434),(772,448),(696,450)] ]),
 # 9. classic 左肩
 ("classic", (374,488,444,634),
  [ [(382,496),(434,496),(434,622),(382,626)] ], []),
]

if APPLY:
    import masks
    for name, rect, mp, tp, tag, rule in masks.REGS:
        if name == "rage" and tag == "rage_Larmpit":
            continue  # 腋下走 smooth_rim_edge
        apply_region(name, rect, mp, tp, iters=3000, alpha_rule=rule)
        print("inpainted", tag, name, rect, rule)
    smooth_rim_edge("rage", (283,548,428,648), win=29, band=7)
    print("rage larmpit rim smoothed")
    print("all retouch applied")
else:
    print("dry-run: pass --apply")

