# 四张识别卡合成：883×1472 出血版画布，中心 827×1417 为成品区
# 背景(纯色+低对比散布纹样+四角装饰+细边框) -> 角色暗色描边+贴角色 -> 底部大字标题
# 同时输出成品裁切版与灰度自检版、角色掩码(供 QC 用)
# 用法: python comp_cards.py
import os, sys, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work")
OUT = os.path.join(BASE, "out")
os.makedirs(OUT, exist_ok=True)

BW, BH = 883, 1472          # 出血画布
CW, CH = 827, 1417          # 成品卡
OX, OY = 28, 27             # 成品区在出血画布中的偏移 (883-827=56, 1472-1417=55)

def font_path():
    for p in (r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc",
              r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(p):
            return p
    raise SystemExit("no CJK font found")

FONT = font_path()

def lum(rgb):
    r, g, b = rgb
    return 0.2126*r + 0.7152*g + 0.0722*b

def scale_char(role_img, target_h, max_w):
    a = np.array(role_img)[:, :, 3]
    ys, xs = np.where(a > 16)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    crop = role_img.crop((x0, y0, x1+1, y1+1))
    s = target_h / crop.height
    if crop.width * s > max_w:
        s = max_w / crop.width
    return crop.resize((max(1,int(crop.width*s)), max(1,int(crop.height*s))), Image.LANCZOS)

def outline_from_alpha(role, radius, color):
    a = role.split()[3]
    dil = a.filter(ImageFilter.MaxFilter(radius*2+1))
    mask = dil.point(lambda v: 255 if v > 60 else 0)
    solid = Image.new("RGBA", role.size, color + (255,))
    solid.putalpha(mask)
    return solid

# 纹样绘制：低对比散布 + 高对比四角装饰
def draw_motif(dr, kind, cx, cy, s, rot, col):
    if kind == "wave":      # 三段圆弧（云/浪）
        for i, dx in enumerate((-0.5, 0.0, 0.5)):
            r = s*(0.55 if i == 1 else 0.4)
            bbox = [cx+dx*s-r, cy-r, cx+dx*s+r, cy+r]
            dr.arc(bbox, start=200, end=340, fill=col, width=max(2,int(s*0.14)))
    elif kind == "spark":   # 闪电三角
        pts = [(cx, cy-s), (cx+0.45*s, cy+0.2*s), (cx-0.1*s, cy+0.15*s), (cx+0.25*s, cy+s)]
        dr.line(pts, fill=col, width=max(3,int(s*0.22)))
    elif kind == "star":    # 四角星
        pts = []
        for k in range(8):
            r = s if k % 2 == 0 else s*0.38
            a_ = rot + k*math.pi/4
            pts.append((cx+r*math.sin(a_), cy-r*math.cos(a_)))
        dr.polygon(pts, fill=col)
    elif kind == "dotleaf": # 圆点+短线叶
        dr.ellipse([cx-s*0.3, cy-s*0.3, cx+s*0.3, cy+s*0.3], outline=col, width=max(2,int(s*0.16)))
        dr.line([cx+s*0.5, cy+s*0.5, cx+s*1.1, cy-s*0.2], fill=col, width=max(2,int(s*0.14)))

def corner_ornament(dr, kind, x, y, s, col, quad):
    """quad: 0=TL,1=TR,2=BL,3=BR —— 四角折线+中心纹样"""
    sx = 1 if quad in (1, 3) else -1
    sy = 1 if quad in (2, 3) else -1
    dr.line([x+sx*s*1.6, y, x, y, x, y+sy*s*1.6], fill=col, width=7)
    dr.line([x+sx*s*1.1, y+sy*s*0.5, x+sx*s*0.5, y+sy*s*1.1], fill=col, width=4)
    draw_motif(dr, kind, x+sx*s*0.85, y+sy*s*0.85, s*0.55, 0.0, col)

CARDS = {
    "calm": dict(
        title="平静模式", sub="CALM MODE", motif="wave",
        bg=(227, 221, 205), accent=(44, 74, 120), title_col=(30, 52, 92),
        outline=(26, 42, 66), char_h=0.68),
    "rage": dict(
        title="愤怒模式", sub="RAGE MODE", motif="spark",
        bg=(150, 142, 130), accent=(96, 32, 24), title_col=(84, 26, 20),
        outline=(58, 16, 12), char_h=0.66),
    "divine": dict(
        title="神格模式", sub="DIVINE MODE", motif="star",
        bg=(38, 25, 50), accent=(158, 128, 188), title_col=(206, 190, 226),
        outline=(18, 10, 24), char_h=0.68),
    "classic": dict(
        title="经典模式", sub="CLASSIC MODE", motif="dotleaf",
        bg=(104, 94, 64), accent=(222, 196, 120), title_col=(232, 214, 158),
        outline=(30, 26, 12), char_h=0.68,
        title_ty=1140, sub_dy=178, line_dy=24),   # 修复副标题与"经典模式"底笔画叠压
}

ONLY = sys.argv[1] if len(sys.argv) > 1 else None
for name, cfg in CARDS.items():
    if ONLY and name != ONLY:
        continue
    rng = random.Random(hash(name) & 0xffff)
    card = Image.new("RGB", (BW, BH), cfg["bg"])
    dr = ImageDraw.Draw(card, "RGBA")

    # --- 低对比抖动网格纹样（50px 单元格各放一枚，保证任意 63px 块非纯色；
    #     位置/大小/角度随机化，无重复观感）---
    cell = 50
    for gy in range(cell//2, BH, cell):
        for gx in range(cell//2, BW, cell):
            cx = gx + rng.uniform(-16, 16); cy = gy + rng.uniform(-16, 16)
            s = rng.uniform(14, 32); rot = rng.uniform(0, math.pi)
            alpha = rng.randint(42, 64)
            draw_motif(dr, cfg["motif"], cx, cy, s, rot, cfg["accent"] + (alpha,))

    # --- 低频柔和斑驳层（消灭大面积均匀色块，对比极低）---
    blot = Image.new("L", (BW, BH), 0)
    db = ImageDraw.Draw(blot)
    for i in range(34):
        cx = rng.uniform(0, BW); cy = rng.uniform(0, BH)
        r = rng.uniform(60, 150)
        v = rng.randint(14, 30)
        db.ellipse([cx-r, cy-r*0.7, cx+r, cy+r*0.7], fill=v)
    blot = blot.filter(ImageFilter.GaussianBlur(60))
    bl = np.asarray(blot, dtype=np.float64) / 255.0
    arr = np.asarray(card, dtype=np.float64)
    tone = np.array(cfg["bg"], dtype=np.float64)
    # 向背景色方向偏移 8% 强度
    arr = arr * (1 - 0.08 * bl[..., None]) + tone * (0.08 * bl[..., None])
    card = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    dr = ImageDraw.Draw(card, "RGBA")

    # --- 轻微暗角（整卡 6% 以内，避免均匀大色块）---
    vign = Image.new("L", (BW, BH), 0)
    dv = ImageDraw.Draw(vign)
    dv.ellipse([-BW*0.25, -BH*0.18, BW*1.25, BH*1.18], fill=255)
    vign = vign.filter(ImageFilter.GaussianBlur(120))
    dark = Image.new("RGB", (BW, BH), tuple(int(c*0.82) for c in cfg["bg"]))
    card = Image.composite(card, dark, vign)
    dr = ImageDraw.Draw(card, "RGBA")

    # --- 细边框（成品区内缩 ~2mm/3.5mm）---
    fx0, fy0, fx1, fy1 = OX+22, OY+22, OX+CW-22, OY+CH-22
    dr.rectangle([fx0, fy0, fx1, fy1], outline=cfg["accent"] + (210,), width=6)
    dr.rectangle([fx0+14, fy0+14, fx1-14, fy1-14], outline=cfg["accent"] + (120,), width=2)

    # --- 四角装饰（位于特征排除缓冲带内，兼作边缘纹理）---
    s = 34
    for quad, (x, y) in enumerate([(fx0+26, fy0+26), (fx1-26, fy0+26),
                                   (fx0+26, fy1-26), (fx1-26, fy1-26)]):
        corner_ornament(dr, cfg["motif"], x, y, s, cfg["accent"] + (200,), quad)

    # --- 顶部小标 ---
    f_top = ImageFont.truetype(FONT, 30)
    t = "桌上有龙 · 形象卡"
    tw = dr.textlength(t, font=f_top)
    dr.text(((BW-tw)/2, fy0+40), t, font=f_top, fill=cfg["accent"] + (200,))

    # --- 角色：暗色描边 + 贴图 ---
    role = Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")
    target_h = cfg["char_h"] * CH
    max_w = CW - 150
    ch = scale_char(role, target_h, max_w)
    base_y = OY + 1106                      # 脚底基线
    cx = OX + CW // 2
    px = cx - ch.width // 2
    py = base_y - ch.height
    ol = outline_from_alpha(ch, 7, cfg["outline"])
    # 接触阴影（很淡）
    sh = Image.new("RGBA", (BW, BH), (0, 0, 0, 0))
    ds = ImageDraw.Draw(sh)
    ds.ellipse([cx-ch.width*0.42, base_y-16, cx+ch.width*0.42, base_y+16], fill=(0, 0, 0, 60))
    sh = sh.filter(ImageFilter.GaussianBlur(10))
    card.paste(sh, (0, 0), sh)
    card.paste(ol, (px-7, py-7), ol)
    card.paste(ch, (px, py), ch)

    # --- 底部标题 ---
    f_title = ImageFont.truetype(FONT, 148)
    tw = dr.textlength(cfg["title"], font=f_title)
    ty = OY + cfg.get("title_ty", 1152)
    # 标题微描边（增加边缘锐度）
    dr.text(((BW-tw)/2 - 3, ty - 3), cfg["title"], font=f_title, fill=cfg["outline"] + (160,))
    dr.text(((BW-tw)/2, ty), cfg["title"], font=f_title, fill=cfg["title_col"])
    f_sub = ImageFont.truetype(FONT, 38)
    sw = dr.textlength(cfg["sub"], font=f_sub)
    sy = ty + cfg.get("sub_dy", 150)
    dr.text(((BW-sw)/2, sy), cfg["sub"], font=f_sub, fill=cfg["accent"] + (220,))
    ly = sy + cfg.get("line_dy", 26)
    dr.line([cx-sw/2-110, ly, cx-sw/2-30, ly], fill=cfg["accent"] + (200,), width=3)
    dr.line([cx+sw/2+30, ly, cx+sw/2+110, ly], fill=cfg["accent"] + (200,), width=3)
    dr.polygon([(cx, ly-8), (cx+8, ly), (cx, ly+8), (cx-8, ly)], fill=cfg["accent"] + (220,))

    # --- 输出 ---
    bleed = card
    content = bleed.crop((OX, OY, OX+CW, OY+CH))
    bleed.save(os.path.join(OUT, f"card_{name}_bleed.png"))
    content.save(os.path.join(OUT, f"card_{name}.png"))
    gray = content.convert("L")
    gray.save(os.path.join(OUT, f"card_{name}_gray.png"))
    # 角色掩码（供灰度 QC 计算 主角-背景 明度差）
    m = Image.new("L", (BW, BH), 0)
    ma = ch.split()[3].point(lambda v: 255 if v > 128 else 0)
    m.paste(ma, (px, py))
    m.crop((OX, OY, OX+CW, OY+CH)).save(os.path.join(WORK, f"card_{name}_mask.png"))
    # 角色透明底备用图
    role.save(os.path.join(OUT, f"role_{name}.png"))
    print(f"card_{name}: done (char {ch.width}x{ch.height} at {px},{py})")

print("all cards done")
