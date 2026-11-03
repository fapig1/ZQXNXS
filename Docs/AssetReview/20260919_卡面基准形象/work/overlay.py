import numpy as np, os
from PIL import Image, ImageDraw
import retouch, masks
info = __import__("json").load(open("audit/role_info.json"))
os.makedirs("audit", exist_ok=True)
for name, rect, mp, tp, tag in masks.REGS:
    im = retouch.load(name)
    ov = im.copy()
    dr = ImageDraw.Draw(ov, "RGBA")
    for poly in mp:
        dr.polygon(poly, fill=(255,0,0,110), outline=(255,0,0,255))
    for poly in tp:
        dr.polygon(poly, fill=(0,255,0,110), outline=(0,255,0,255))
    bg = Image.new("RGB", im.size, (240,240,240)); bg.paste(ov,(0,0),ov)
    c = bg.crop(rect); w,h=c.size
    z = min(6, max(3, int(900/max(w,h))))
    c.resize((w*z,h*z), Image.NEAREST).save(f"audit/ovl_{tag}.png")
    print("overlay", tag, z)
