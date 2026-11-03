# 计算 role(1100x1650) -> 成品卡(827x1417) 映射，并输出 must_fix 区域在 role 坐标的位置
import numpy as np
from PIL import Image
import os
BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work")
CW, CH = 827, 1417; OX, OY = 28, 27
CHAR_H = dict(calm=0.68, rage=0.66, divine=0.68, classic=0.68)
MAXW = CW - 150
REGS = {  # must_fix 卡面坐标
 "calm":   {"shL": (300,330,360,430)},
 "rage":   {"armpit": (230,390,430,570), "shR": (540,380,610,430)},
 "divine": {"shL": (260,360,320,470), "shR": (540,360,600,470),
            "bodyL": (230,500,310,640), "bodyR": (560,560,620,630),
            "belly": (220,460,630,660)},
 "classic":{"shR": (495,310,575,505), "shL": (280,380,340,490)},
}
for name in ["calm","rage","divine","classic"]:
    for tag, path in [("cur", os.path.join(WORK, f"role_{name}.png")),
                      ("pre", os.path.join(WORK, f"role_{name}_pre2d.png"))]:
        a = np.array(Image.open(path).convert("RGBA"))[:,:,3]
        ys, xs = np.where(a > 16)
        x0,x1,y0,y1 = xs.min(), xs.max(), ys.min(), ys.max()
        crop = (x1+1-x0, y1+1-y0)
        s = (CHAR_H[name]*CH) / crop[1]
        if crop[0]*s > MAXW: s = MAXW / crop[0]
        ch_c, cw_c = int(crop[1]*s), int(crop[0]*s)
        px = OX + CW//2 - cw_c//2 - OX   # 卡面坐标
        py = OY + 1106 - ch_c - OY
        print(f"{name} {tag}: bbox=({x0},{y0},{x1},{y1}) scale={s:.4f} paste_card=({px},{py}) size=({cw_c},{ch_c})")
        if tag == "pre":
            # 卡面 -> pre role: xr = (xc - px)/s + x0 ; yr = (yc - py)/s + y0
            for rname,(cx0,cy0,cx1,cy1) in REGS[name].items():
                rx0 = (cx0-px)/s + x0; ry0 = (cy0-py)/s + y0
                rx1 = (cx1-px)/s + x0; ry1 = (cy1-py)/s + y0
                print(f"   {rname}: card({cx0},{cy0},{cx1},{cy1}) -> pre_role({rx0:.0f},{ry0:.0f},{rx1:.0f},{ry1:.0f})")
