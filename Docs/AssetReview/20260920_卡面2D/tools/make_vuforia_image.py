# -*- coding: utf-8 -*-
"""把定稿卡面图转成 Vuforia Target Manager 可上传的版本（单图 < 2 MB）。

用途：四张塔罗奶蛙卡的定稿 PNG 往往 3 MB 上下（满幅细纹导致 PNG 压缩率低），
超过 Vuforia Target Manager 的单图 2 MB 上限。本脚本居中裁到卡片标准比例
（780:1110，即 66×94 mm @300 DPI），转 JPEG 并逐级降质直到 < 2 MB。

用法：
    python make_vuforia_image.py <输入图> [-o 输出.jpg] [--max-mb 2.0]

示例：
    python make_vuforia_image.py ../picked/classic.png
    python make_vuforia_image.py ../picked/rage.png -o ../picked/rage_vuforia.jpg

注意：Vuforia 上传的是"印刷卡正面整幅"，因此只做居中裁切 + 缩放，不加字、不改内容；
打印母版仍用原始 PNG（本脚本不覆盖输入文件）。
"""

from __future__ import annotations

import argparse
import os
import sys

from PIL import Image

OUT_W, OUT_H = 780, 1110  # 66 x 94 mm @300 DPI
DEFAULT_MAX_MB = 2.0
QUALITY_LADDER = (92, 88, 85, 80, 75, 70, 65, 60)


def center_crop_to_ratio(im: Image.Image, ratio: float) -> Image.Image:
    """把图像居中裁到目标宽高比（ratio = 宽/高），不拉伸。"""
    w, h = im.size
    cur = w / h
    if abs(cur - ratio) < 1e-6:
        return im
    if cur > ratio:  # 太宽，裁左右
        nw = int(round(h * ratio))
        x0 = (w - nw) // 2
        return im.crop((x0, 0, x0 + nw, h))
    nh = int(round(w / ratio))  # 太高，裁上下
    y0 = (h - nh) // 2
    return im.crop((0, y0, w, y0 + nh))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("-o", "--out", default=None)
    ap.add_argument("--max-mb", type=float, default=DEFAULT_MAX_MB)
    args = ap.parse_args()

    src = os.path.abspath(args.src)
    if not os.path.isfile(src):
        print("找不到输入文件:", src)
        return 1

    im = Image.open(src)
    if im.mode != "RGB":
        im = im.convert("RGB")

    ratio = OUT_W / OUT_H
    cropped = center_crop_to_ratio(im, ratio)
    resized = cropped.resize((OUT_W, OUT_H), Image.LANCZOS)

    out = args.out or (os.path.splitext(src)[0] + "_vuforia.jpg")
    out = os.path.abspath(out)
    limit = int(args.max_mb * 1024 * 1024)

    chosen_q = None
    for q in QUALITY_LADDER:
        resized.save(out, "JPEG", quality=q, optimize=True, subsampling=0)
        size = os.path.getsize(out)
        if size <= limit:
            chosen_q = q
            break

    if chosen_q is None:
        print("降到 q%d 仍超过 %.1f MB（%d B），请检查输入或调高 --max-mb",
              QUALITY_LADDER[-1], args.max_mb, os.path.getsize(out))
        return 2

    print("OK")
    print("  输入: %s (%d x %d, %.2f MB)"
          % (src, im.size[0], im.size[1], os.path.getsize(src) / 1048576.0))
    print("  输出: %s (%d x %d, %.3f MB, JPEG q%d)"
          % (out, OUT_W, OUT_H, os.path.getsize(out) / 1048576.0, chosen_q))
    print("  对应印刷尺寸: 66 x 94 mm @300 DPI；Vuforia ImageTarget 宽高设 0.066 x 0.094 m")
    return 0


if __name__ == "__main__":
    sys.exit(main())
