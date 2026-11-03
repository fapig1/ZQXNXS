# 生成四款肤色贴图变体（只改颜色，不改网格/骨骼/UV）
# 分类依据：body h∈[0.05,0.17]&s>0.25&v>0.45；brown h∈[0.05,0.15]&s>0.25&v<=0.45；
# belly s<0.3&v>0.75；绿眼环 h∈[0.18,0.35] 与红口腔 h>=0.9 一律不动；暗部 v<0.2 不动
import numpy as np
from PIL import Image, ImageFilter
import os

SRC = r"D:/ZengQiangXianShi/ArtSource/Characters/Pet01/Textures/Pet01_BaseColor.png"
OUT = r"D:/ZengQiangXianShi/Docs/AssetReview/20260919_卡面基准形象/work/tex_variant"
os.makedirs(OUT, exist_ok=True)

im = Image.open(SRC).convert("RGB")
a = np.asarray(im).astype(np.float32) / 255.0
mx = a.max(2); mn = a.min(2); d = np.maximum(mx - mn, 1e-6)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
v = mx
s = (mx - mn) / np.maximum(mx, 1e-6)

def smooth(m):
    """bool mask -> float 0..1, 3px 高斯软化防硬边"""
    mi = Image.fromarray((m * 255).astype(np.uint8))
    mi = mi.filter(ImageFilter.GaussianBlur(3))
    return np.asarray(mi).astype(np.float32) / 255.0

m_body = smooth((hue >= 0.05) & (hue <= 0.17) & (s > 0.25) & (v > 0.45))
m_belly = smooth((s < 0.3) & (v > 0.75))

def hsv_to_rgb(h, s_, v_):
    i = np.floor(h * 6.0); f = h * 6.0 - i
    p = v_ * (1 - s_); q = v_ * (1 - f * s_); t = v_ * (1 - (1 - f) * s_)
    i = i.astype(int) % 6
    r_ = np.choose(i, [v_, q, p, p, t, v_])
    g_ = np.choose(i, [t, v_, v_, q, p, p])
    b_ = np.choose(i, [p, p, t, v_, v_, q])
    return np.stack([r_, g_, b_], axis=-1)

def rgb_to_hsv(arr):
    mx_ = arr.max(2); mn_ = arr.min(2); d_ = np.maximum(mx_ - mn_, 1e-6)
    r_, g_, b_ = arr[..., 0], arr[..., 1], arr[..., 2]
    h_ = np.where(mx_ == r_, ((g_ - b_) / d_) % 6, np.where(mx_ == g_, (b_ - r_) / d_ + 2, (r_ - g_) / d_ + 4)) / 6.0
    s_ = (mx_ - mn_) / np.maximum(mx_, 1e-6)
    return h_, s_, mx_

VARIANTS = {
    # body: (hue, sat, val)，belly: (hue, sat)（belly v 保持原高明度）
    "calm":   dict(body=(0.585, 0.64, 0.40), belly=(0.580, 0.30)),
    "rage":   dict(body=(0.030, 0.72, 0.68), belly=None),          # 肚皮保持奶白
    "divine": dict(body=(0.740, 0.48, 0.55), belly=(0.750, 0.20)),
    "classic": dict(body=(0.115, 0.62, 0.70), belly=None),
}

for name, cfg in VARIANTS.items():
    out = a.copy()
    h2, s2, v2 = rgb_to_hsv(out)
    bh, bs, bv = cfg["body"]
    # 身体：换色相/饱和/明度（保持一点原有明暗起伏：v 按比例缩放）
    body_rgb = hsv_to_rgb(np.full_like(h2, bh), np.full_like(h2, bs),
                          np.clip(v2 / max(np.median(v2[m_body > 0.5]), 1e-6) * bv, 0.05, 1.0))
    w = m_body[..., None]
    out = out * (1 - w) + body_rgb * w
    if cfg["belly"] is not None:
        h3, s3, v3 = rgb_to_hsv(out)
        bh2, bs2 = cfg["belly"]
        belly_rgb = hsv_to_rgb(np.full_like(h3, bh2), np.full_like(h3, bs2), v3)
        w2 = (m_belly * (1 - m_body))[..., None]   # 肚皮权重扣除身体重叠
        out = out * (1 - w2) + belly_rgb * w2
    res = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))
    res = res.filter(ImageFilter.MedianFilter(3))   # 去 Meshy 贴图颗粒噪点
    p = os.path.join(OUT, f"Pet01_BaseColor_{name}.png")
    res.save(p)
    print("saved", p)

print("done")
