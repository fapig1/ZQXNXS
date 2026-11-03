# 灰度自检：对 4 张成品卡(card_*.png 827x1417)计算 Vuforia 关注的灰度指标
# 指标:
#  1. edge_density: FIND_EDGES 均值 (0-255)，整卡
#  2. char_vs_bg: 角色核心区(mask 内)灰度均值 与 同高度背景环带(mask 外、bbox 外扩 30px)均值之差
#  3. title_contrast: 标题区(y 1150-1400)内 FIND_EDGES 均值
#  4. ring_texture: 外围 0-60px 缓冲带灰度标准差（验证"边缘到10mm非纯色"）
#  5. bg_flatness: 最大均匀色块检测——63px 网格块的灰度标准差最小值（应 > 3）
#  6. pairwise: 两两卡面 128px 缩略灰度的平均绝对差（两两区分度）
# 用法: python qc_gray.py   (结果写入 qc_report.txt)
import os
import numpy as np
from PIL import Image, ImageFilter

BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
OUT = os.path.join(BASE, "out")
WORK = os.path.join(BASE, "work")
NAMES = ["calm", "rage", "divine", "classic"]

def gray_mean(a):
    return float(a.mean())

report = []
def P(*a):
    s = " ".join(str(x) for x in a)
    print(s); report.append(s)

grays = {}
for n in NAMES:
    g = np.asarray(Image.open(os.path.join(OUT, f"card_{n}.png")).convert("L"), dtype=np.float64)
    grays[n] = g
    H, W = g.shape
    # 1. 边缘密度
    e = np.asarray(Image.open(os.path.join(OUT, f"card_{n}.png")).convert("L")
                   .filter(ImageFilter.FIND_EDGES), dtype=np.float64)
    edge_all = e.mean()
    # 3. 标题区
    edge_title = e[1150:1400, :].mean()
    # 4. 外围缓冲带纹理
    ring = np.concatenate([g[:60, :].ravel(), g[-60:, :].ravel(),
                           g[:, :60].ravel(), g[:, -60:].ravel()])
    ring_std = ring.std()
    # 5. 均匀块检测
    mask0 = np.asarray(Image.open(os.path.join(WORK, f"card_{n}_mask.png")), dtype=np.float64) > 128
    mins = 1e9
    for by in range(0, H-63, 63):
        for bx in range(0, W-63, 63):
            blk = g[by:by+63, bx:bx+63]
            if mask0[by:by+63, bx:bx+63].mean() > 0.3:
                continue   # 角色内部的平滑肚皮属角色固有属性，不计入背景色块
            mins = min(mins, blk.std())
    # 2. 角色-背景 明度差
    mask = np.asarray(Image.open(os.path.join(WORK, f"card_{n}_mask.png")), dtype=np.float64) > 128
    ys, xs = np.where(mask)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    mimg_d = Image.fromarray((mask*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(25))
    ring_excl = np.asarray(mimg_d) > 128           # 掩码+12px 轮廓环一并从背景带排除
    band = np.zeros_like(mask)
    band[max(0,y0-30):y1+30, max(0,x0-30):min(W,x1+30)] = True
    band &= ~ring_excl
    mimg = Image.fromarray((mask*255).astype(np.uint8)).filter(ImageFilter.MinFilter(13))
    core = np.asarray(mimg) > 128
    char_mean = g[core].mean()
    bg_mean = g[band].mean()
    diff = abs(char_mean - bg_mean)
    # 轮廓边界对比度：边界内外各 3px 的灰度差绝对值的均值（多色调主体下比均值差更能代表识别度）
    m_in  = np.asarray(Image.fromarray((mask*255).astype(np.uint8)).filter(ImageFilter.MinFilter(7)))  > 128
    m_out = np.asarray(Image.fromarray((mask*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7)))  > 128
    b_in  = m_in & mask
    b_out = m_out & ~mask & ~m_in
    bnd = abs(g[b_in].mean() - g[b_out].mean())
    P(f"[{n}] edge_density={edge_all:.1f}  title_edge={edge_title:.1f}  ring_std={ring_std:.1f}  "
      f"min_block_std={mins:.1f}  char_gray={char_mean:.1f}  bg_gray={bg_mean:.1f}  char_bg_diff={diff:.1f}  boundary_contrast={bnd:.1f}")

# 6. 两两区分度
P("")
for i in range(len(NAMES)):
    for j in range(i+1, len(NAMES)):
        a = np.asarray(Image.fromarray(grays[NAMES[i]].astype(np.uint8)).resize((128, 219)), dtype=np.float64)
        b = np.asarray(Image.fromarray(grays[NAMES[j]].astype(np.uint8)).resize((128, 219)), dtype=np.float64)
        mad = np.abs(a-b).mean()
        corr = np.corrcoef(a.ravel(), b.ravel())[0, 1]
        P(f"pair {NAMES[i]}-{NAMES[j]}: mean_abs_diff={mad:.1f}  corr={corr:.3f}")

with open(os.path.join(WORK, "qc_report.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(report) + "\n")
print("saved qc_report.txt")
