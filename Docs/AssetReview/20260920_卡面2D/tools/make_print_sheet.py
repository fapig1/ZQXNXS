# -*- coding: utf-8 -*-
"""把四张定稿卡面排到一张 A4 上，供铜版纸打印。

输出：
  print/打印版_A4_四卡_300DPI.pdf   —— 交打印店用（300 DPI 位图 PDF）
  print/打印版_A4_四卡_300DPI.png   —— 同内容 PNG 备份/自查

规格：
  - A4 竖版 210 x 297 mm，300 DPI（2480 x 3508 px），白底
  - 每卡 66 x 94 mm（780 x 1110 px @300 DPI），由 picked/ 母版居中裁比例后缩放，
    与 make_vuforia_image.py 同一裁切口径（Vuforia 上传图与打印图内容一致）
  - 2 x 2 排布，卡间距 15 mm，整组在页面上居中
  - 每卡四角画裁切线（短线，不接触裁切边）；卡间空白处放小字号卡名（会被裁掉，
    不影响卡面零文字约束）；页脚一行印制参数提示
  - 不做出血（成品按裁切线裁到 66 x 94 mm，卡内关键细节本就避开 8% 边带）

用法：
    python make_print_sheet.py            # 使用默认 picked/ 母版
    python make_print_sheet.py --no-pdf   # 只出 PNG
"""

from __future__ import annotations

import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 20260920_卡面2D/
PICKED = os.path.join(BASE, "picked")
OUT_DIR = os.path.join(BASE, "print")

DPI = 300
MM = DPI / 25.4  # 1 mm 对应像素数
PAGE_W, PAGE_H = 2480, 3508  # A4 @300DPI

CARD_W, CARD_H = 780, 1110   # 66 x 94 mm @300 DPI
GAP = round(15 * MM)         # 卡间距 15 mm

# 版面顺序（左上、右上、左下、右下）
CARDS = [
    ("classic", "classic 暖黄 经典"),
    ("calm",    "calm 湖蓝 平静"),
    ("rage",    "rage 朱红 愤怒"),
    ("divine",  "divine 紫 神格"),
]

MARK_OFF = round(1.5 * MM)   # 裁切线与裁切边的空隙
MARK_LEN = round(4 * MM)     # 裁切线长度


def center_crop_to_ratio(im: Image.Image, ratio: float) -> Image.Image:
    w, h = im.size
    cur = w / h
    if abs(cur - ratio) < 1e-6:
        return im
    if cur > ratio:
        nw = int(round(h * ratio))
        x0 = (w - nw) // 2
        return im.crop((x0, 0, x0 + nw, h))
    nh = int(round(w / ratio))
    y0 = (h - nh) // 2
    return im.crop((0, y0, w, y0 + nh))


def load_card(name: str) -> Image.Image:
    src = os.path.join(PICKED, name + ".png")
    im = Image.open(src)
    if im.mode != "RGB":
        im = im.convert("RGB")
    cropped = center_crop_to_ratio(im, CARD_W / CARD_H)
    return cropped.resize((CARD_W, CARD_H), Image.LANCZOS)


def load_font(size_px: int):
    for candidate in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(candidate, size_px)
        except OSError:
            continue
    return ImageFont.load_default()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-pdf", action="store_true")
    args = ap.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    sheet = Image.new("RGB", (PAGE_W, PAGE_H), "white")
    draw = ImageDraw.Draw(sheet)

    group_w = CARD_W * 2 + GAP
    group_h = CARD_H * 2 + GAP
    origin_x = (PAGE_W - group_w) // 2
    origin_y = (PAGE_H - group_h) // 2

    positions = [
        (origin_x, origin_y),
        (origin_x + CARD_W + GAP, origin_y),
        (origin_x, origin_y + CARD_H + GAP),
        (origin_x + CARD_W + GAP, origin_y + CARD_H + GAP),
    ]

    label_font = load_font(round(3.2 * MM))
    for (name, label), (x0, y0) in zip(CARDS, positions):
        x1, y1 = x0 + CARD_W, y0 + CARD_H
        sheet.paste(load_card(name), (x0, y0))

        # 裁切线：四角各两条短线，与卡边留 MARK_OFF 空隙，不接触裁切边
        black = (0, 0, 0)
        for yy in (y0, y1):
            draw.line([(x0 - MARK_OFF - MARK_LEN, yy), (x0 - MARK_OFF, yy)], fill=black, width=2)
            draw.line([(x1 + MARK_OFF, yy), (x1 + MARK_OFF + MARK_LEN, yy)], fill=black, width=2)
        for xx in (x0, x1):
            draw.line([(xx, y0 - MARK_OFF - MARK_LEN), (xx, y0 - MARK_OFF)], fill=black, width=2)
            draw.line([(xx, y1 + MARK_OFF), (xx, y1 + MARK_OFF + MARK_LEN)], fill=black, width=2)

        # 卡名标签：放在卡片上方空隙（裁掉区域），灰色小字
        draw.text((x0, y0 - round(6.5 * MM)), label, fill=(120, 120, 120), font=label_font)

    # 页脚印制参数（页边距内，远离所有卡）
    note = "每张成品 66 x 94 mm @300 DPI · 哑光铜版(哑粉纸) · 不覆膜不烫金 · 沿裁切线裁切 · 打印母版见 picked/*.png"
    note_font = load_font(round(3.0 * MM))
    tw = draw.textlength(note, font=note_font)
    draw.text(((PAGE_W - tw) / 2, PAGE_H - round(8 * MM)), note, fill=(120, 120, 120), font=note_font)

    png_out = os.path.join(OUT_DIR, "打印版_A4_四卡_300DPI.png")
    sheet.save(png_out, "PNG", dpi=(DPI, DPI))
    print("OK  PNG :", png_out, "(%d x %d, %.2f MB)" % (PAGE_W, PAGE_H, os.path.getsize(png_out) / 1048576))

    if not args.no_pdf:
        pdf_out = os.path.join(OUT_DIR, "打印版_A4_四卡_300DPI.pdf")
        sheet.save(pdf_out, "PDF", resolution=DPI)
        print("OK  PDF :", pdf_out, "(%.2f MB)" % (os.path.getsize(pdf_out) / 1048576))
    return 0


if __name__ == "__main__":
    sys.exit(main())
