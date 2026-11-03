import os
from PIL import Image
BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work"); W4 = os.path.join(WORK, "selfcheck4")
REGS = [
 ("calm",  (384,435,483,586), "pre_calm_shL"),
 ("rage",  (288,480,582,747), "pre_rage_armpit"),
 ("rage",  (713,466,830,555), "pre_rage_shR"),
 ("divine",(334,466,435,634), "pre_div_shL"),
 ("divine",(711,466,811,634), "pre_div_shR"),
 ("divine",(293,655,421,863), "pre_div_bodyL"),
 ("divine",(738,735,838,850), "pre_div_bodyR"),
 ("divine",(280,601,852,890), "pre_div_belly"),
 ("classic",(650,403,779,690), "pre_class_shR"),
 ("classic",(356,499,458,669), "pre_class_shL"),
]
for name,(x0,y0,x1,y1),tag in REGS:
    im = Image.open(os.path.join(WORK, f"role_{name}_pre2d.png"))
    # 棋盘格底衬显示透明区
    c = im.crop((x0,y0,x1,y1))
    bg = Image.new("RGBA", c.size, (255,0,255,255))
    bg.paste(c, (0,0), c)
    s = max(2, min(5, 900//max(c.width,1)))
    bg = bg.resize((c.width*s, c.height*s), Image.NEAREST).convert("RGB")
    bg.save(os.path.join(W4, tag+".png"))
    print(tag, "scale", s)
