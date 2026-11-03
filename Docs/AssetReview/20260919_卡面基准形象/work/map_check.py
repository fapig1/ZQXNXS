import numpy as np
from PIL import Image
import os, json

BASE = r"D:\ZengQiangXianShi\Docs\AssetReview\20260919_卡面基准形象"
WORK = os.path.join(BASE, "work"); OUT = os.path.join(BASE, "out")
OX, OY = 28, 27
CH = 1417
PLACEMENT = {  # from comp_cards output: char w,h at px,py (bleed canvas)
 "calm":   dict(w=527,h=963,px=178,py=170),
 "rage":   dict(w=575,h=935,px=154,py=198),
 "divine": dict(w=530,h=963,px=176,py=170),
 "classic":dict(w=507,h=963,px=188,py=170),
}
info = {}
for name, pl in PLACEMENT.items():
    role = Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")
    a = np.array(role)[:,:,3]
    ys, xs = np.where(a > 16)
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())
    s = pl["h"] / (y1+1-y0)
    info[name] = dict(x0=x0, y0=y0, s=s, role_size=role.size)

def card2role(name, cx_, cy_):
    pl = PLACEMENT[name]; inf = info[name]
    bx, by = cx_ + OX, cy_ + OY
    lx, ly = bx - pl["px"], by - pl["py"]
    return inf["x0"] + lx/inf["s"], inf["y0"] + ly/inf["s"]

# 审查员 flagged 区域（card 坐标）：(name, x0,y0,x1,y1, tag)
REGIONS = [
 ("calm",   290, 320, 375, 440, "calm_Lsh"),
 ("rage",   520, 350, 630, 450, "rage_Rsh"),
 ("rage",   150, 340, 300, 490, "rage_Larmpit"),
 ("divine", 240, 340, 320, 470, "div_Lsh"),
 ("divine", 510, 340, 600, 470, "div_Rsh"),
 ("divine", 210, 490, 320, 650, "div_Lbelly"),
 ("divine", 545, 545, 640, 645, "div_Rwedge"),
 ("classic",480, 320, 590, 470, "cls_Rsh"),
 ("classic",270, 370, 350, 500, "cls_Lsh"),
]
os.makedirs("audit", exist_ok=True)
for name, cx0, cy0, cx1, cy1, tag in REGIONS:
    rx0, ry0 = card2role(name, cx0, cy0)
    rx1, ry1 = card2role(name, cx1, cy1)
    pad = 20
    box = (max(0,int(rx0-pad)), max(0,int(ry0-pad)), min(info[name]["role_size"][0],int(rx1+pad)), min(info[name]["role_size"][1],int(ry1+pad)))
    im = Image.open(os.path.join(WORK, f"role_{name}.png")).convert("RGBA")
    bg = Image.new("RGB", im.size, (240,240,240)); bg.paste(im,(0,0),im)
    c = bg.crop(box); w,h = c.size
    z = min(6, max(3, int(900/max(w,h))))
    c.resize((w*z,h*z), Image.LANCZOS).save(f"audit/insp_{tag}.png")
    print(tag, "role box", box, "zoom", z)
json.dump(info, open("audit/role_info.json","w"), indent=1)
