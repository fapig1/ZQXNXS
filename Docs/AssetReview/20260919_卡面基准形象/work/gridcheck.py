import numpy as np, os, json
from PIL import Image, ImageDraw
BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work")
REGS = [
 ("calm",   (380,420,500,590), "calm_Lsh", 4),
 ("rage",   (730,450,835,600), "rage_Rsh", 4),
 ("rage",   (285,550,425,650), "rage_Larmpit", 5),
 ("divine", (300,430,400,625), "div_Lsh", 4),
 ("divine", (695,475,785,610), "div_Rsh", 5),
 ("divine", (275,675,375,835), "div_Lbelly", 5),
 ("divine", (705,725,795,855), "div_Rwedge", 5),
 ("classic",(675,435,795,645), "cls_Rsh", 4),
 ("classic",(390,490,450,630), "cls_Lsh", 6),
]
for name, box, tag, z in REGS:
    im = Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")
    bg = Image.new("RGB", im.size, (200,200,200)); bg.paste(im,(0,0),im)
    c = bg.crop(box).resize(((box[2]-box[0])*z,(box[3]-box[1])*z), Image.LANCZOS)
    d = ImageDraw.Draw(c)
    for gx in range(box[0]//10*10+10, box[2], 10):
        X = (gx-box[0])*z
        col = (255,0,0) if gx%50==0 else (0,160,0)
        d.line([(X,0),(X,c.height)], fill=col, width=1)
        if gx%50==0: d.text((X+2,2), str(gx), fill=(255,0,0))
    for gy in range(box[1]//10*10+10, box[3], 10):
        Y = (gy-box[1])*z
        col = (255,0,0) if gy%50==0 else (0,160,0)
        d.line([(0,Y),(c.width,Y)], fill=col, width=1)
        if gy%50==0: d.text((2,Y+2), str(gy), fill=(255,0,0))
    c.save(f"audit/grid_{tag}.png")
    print("grid", tag)
