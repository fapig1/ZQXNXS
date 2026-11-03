# 按 must_fix 坐标裁剪当前成品卡 4~6x 放大自查
import os
from PIL import Image
BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
OUT = os.path.join(BASE, "out")
W4 = os.path.join(BASE, "work", "selfcheck4")
REGS = [
 ("calm",   (290,320,375,445), "cur_calm_shoulderL"),
 ("rage",   (220,380,440,580), "cur_rage_armpit"),
 ("rage",   (530,370,620,440), "cur_rage_shoulderR"),
 ("divine", (260,360,400,470), "cur_div_shoulderL"),
 ("divine", (510,360,640,470), "cur_div_shoulderR"),
 ("divine", (220,500,310,640), "cur_div_bodyL"),
 ("divine", (560,560,620,630), "cur_div_bodyR"),
 ("divine", (230,470,620,660), "cur_div_belly_wide"),
 ("classic",(495,310,575,505), "cur_class_shoulderR"),
 ("classic",(280,380,340,490), "cur_class_shoulderL"),
]
for name,(x0,y0,x1,y1),tag in REGS:
    im = Image.open(os.path.join(OUT, f"card_{name}.png"))
    c = im.crop((x0,y0,x1,y1))
    s = 5
    c = c.resize((c.width*s, c.height*s), Image.NEAREST)
    c.save(os.path.join(W4, tag+".png"))
    print(tag, c.size)
