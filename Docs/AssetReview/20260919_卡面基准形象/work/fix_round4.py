# 第四轮修复驱动：预览/应用两模式
# 用法: python fix_round4.py preview   —— 生成轮廓/掩码叠加预览图
#       python fix_round4.py apply     —— 备份后应用全部修复
import numpy as np
from PIL import Image, ImageDraw
import os, sys, shutil
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from retouch2 import (load, save, catmull_rom, poly_mask, fill_zones,
                      force_alpha_fill, reshape_edge, radial_glow_rebuild,
                      inpaint_lowfreq, feather_apply, curve_band_mask, WORK, AUD)

MODE = sys.argv[1] if len(sys.argv) > 1 else "preview"

# ---------- 各卡修复定义（role 坐标） ----------
# reshape: (rect, curve_pts, closure_pts)  closure_pts 追加在曲线尾端之后围合"外侧"多边形
# fill: (rect, polys, tag)  forcefill: (rect, polys, tag)

FIXES = {
 "calm": dict(
   reshape=[
     # 左肩：去除 y425-470 的 x=394 直边台阶，平滑衔接上段(419@410)与下段(381@500)
     ((350,370,470,600),
      [(434,370),(433,374),(432,378),(426,394),(420,408),(412,422),(403,438),(396,455),(390,472),
       (384,488),(379,502),(371,514),(362,528),(353,542),(346,556),(341,572),(340,590)],
      [(-60,590),(32,370)]),
   ],
   fill=[
     # 肩内淡色矩形色差块（旧填充边界）
     ((378,418,502,602),
      [(398,438),(458,432),(482,458),(487,520),(479,578),(434,586),(400,568),(390,498)],
      "calm_shade"),
   ],
   forcefill=[],
   glow=None,
 ),
 "rage": dict(
   reshape=[
     # 头左缘直边(x448, y395-425) + 头臂 V 型口 + 臂上缘平滑衔接
     ((280,384,470,540),
      [(454,388),(450,406),(445,424),(441,440),(439,452),(437,460),(433,468),
       (428,475),(423,481),(415,486),(405,490),(395,494),(385,497),(375,500),
       (365,503),(355,506),(345,509),(335,511),(325,513),(315,515),(305,516),(292,517)],
      [(-108,517),(-108,380),(454,380)]),
     # 右侧：头右缘 + 臂顶直角台阶(x825-835) + 外缘，整条平滑
     ((690,350,970,660),
      [(722,358),(726,372),(730,388),(735,404),(740,420),(745,436),(750,450),
       (755,462),(762,472),(772,478),(784,481),(798,484),(812,487),(826,491),
       (840,498),(850,507),(858,517),(864,528),(870,540),(876,553),(883,567),
       (891,582),(900,598),(909,614),(917,630),(923,646)],
      [(1323,646),(1323,346),(722,346)]),
     # 左臂下缘曲线 A（腋下楔形上缘；外侧=下方缝隙，体在上方）
     ((285,586,440,662),
      [(438,594),(420,597),(400,600),(380,605),(360,611),(340,618),(320,626),
       (305,634),(295,640),(287,647)],
      [(-113,647),(-113,1047),(838,1047),(838,594)]),
   ],
   fill=[
     # 腋下阴影涂抹条痕（保持暗影渐变，去条痕）
     ((285,528,470,632),
      [(295,620),(310,610),(330,600),(355,592),(385,586),(415,582),(435,580),
       (442,560),(430,545),(390,540),(340,544),(305,552),(290,572),(288,600)],
      "rage_band"),
   ],
   forcefill=[
     # 腋下大洞闭合（B 曲线以下全部填实，楔形缝隙保留在 A-B 之间）
     ((283,590,452,712),
      [(292,652),(310,649),(330,644),(350,638),(370,631),(390,624),(410,617),
       (425,608),(435,600),(441,595),(446,612),(446,650),(446,706),(306,706),
       (298,688),(292,672),(290,660)],
      "rage_hole"),
   ],
   glow=None,
 ),
 "divine": dict(
   reshape=[
     # 左侧：上段自然边缘(400@430→355@590)平滑衔接带左缘(300@600-690)，
     # 消除 y590-600 台地跳变与台阶，闭合 337-390,596-625 矩形凹口
     ((280,420,420,710),
      [(400,428),(397,440),(394,455),(390,470),(386,482),(382,495),(378,507),
       (373,520),(369,532),(364,545),(360,557),(356,570),(352,582),(347,594),
       (340,604),(330,613),(320,622),(312,632),(305,643),(300,655),(297,668),
       (295,682),(294,700)],
      [(-106,700),(0,428)]),
     # 右缘：去除 y490(y=716) 与 y570(y=780) 两处锯齿凹陷
     ((700,470,850,670),
      [(724,476),(725,492),(727,502),(736,510),(746,519),(757,529),(766,538),
       (773,548),(780,558),(783,570),(786,582),(790,592),(794,602),(799,612),
       (803,622),(808,632),(813,642),(818,652),(822,664)],
      [(1222,664),(1222,468),(724,468)]),
   ],
   fill=[
     # 光晕左带：灰条痕/涂抹竖纹 + 凹口周边（radial rebuild 前的基础平滑）
     ((288,520,420,700),
      [(298,600),(310,586),(325,572),(345,560),(368,552),(390,550),(405,560),
       (410,585),(404,615),(392,640),(370,652),(344,656),(322,650),(306,636),(296,618)],
      "div_band"),
     # 光晕右缘：粉色涂抹块与台阶带
     ((740,690,840,830),
      [(748,700),(775,694),(800,698),(820,712),(828,738),(822,768),(804,792),
       (780,806),(760,800),(750,776),(746,738)],
      "div_bandR"),
   ],
   forcefill=[],
   glow=dict(
     fit_rect=(330,420,950,1000),
     zones=[
       ((280,510,430,720), [(296,598),(308,584),(324,570),(344,558),(368,550),(392,548),
                            (408,560),(412,588),(406,618),(394,644),(370,656),(342,660),
                            (320,652),(304,636),(294,616)]),
       ((735,680,845,835), [(746,698),(774,692),(800,696),(820,710),(828,736),
                            (822,766),(804,790),(780,804),(758,798),(748,774),(744,736)]),
     ],
   ),
 ),
 "classic": dict(
   reshape=[
     # 右肩：下颌-肩-臂顶整条平滑（去除肩顶横直边、碎屑岛、矩形凹角）
     ((640,400,850,540),
      [(746,426),(716,431),(690,435),(676,438),(676,443),(684,445),(698,447),
       (716,450),(736,455),(754,461),(768,471),(780,483),(792,495),(804,507),
       (816,519),(828,532),(838,544)],
      [(1238,544),(1238,416),(750,416)]),
     # 左臂上缘台阶区轻微平滑（x380-430, y490-520）
     ((350,460,470,640),
      [(452,444),(444,470),(436,488),(428,494),(420,497),(410,503),(400,511),
       (390,519),(380,533),(370,546),(360,557),(350,568),(342,580),(334,594)],
      [(-66,594),(52,444)]),
   ],
   fill=[
     # 右肩槽底碎屑 + 槽右淡色竖条
     ((688,570,790,712),
      [(696,580),(718,578),(724,622),(744,636),(766,654),(770,680),(756,698),
       (720,702),(700,690),(690,652),(692,604)],
      "cls_debris"),
     # 左臂-肚皮交界深色三角碎屑链（对角带）
     ((316,620,402,740),
      [(390,632),(380,646),(370,660),(358,676),(348,692),(338,710),(328,726),
       (318,730),(314,712),(322,692),(334,672),(350,652),(368,636),(382,626)],
      "cls_flecks"),
   ],
   forcefill=[
     # 右肩"1"字槽整体闭合（x675-704, y440-662，样条圆化边界）
     ((664,436,712,668),
      [(676,442),(692,440),(700,446),(704,482),(705,540),(703,590),(699,630),
       (691,656),(679,660),(672,652),(670,600),(671,520),(672,470)],
      "cls_slot"),
   ],
   glow=None,
 ),
}

# ---------- 外侧收口（每条曲线显式给出追加点） ----------
def close_outside(curve, closure_pts):
    return list(curve) + list(closure_pts)

def preview():
    os.makedirs(AUD, exist_ok=True)
    for name, fx in FIXES.items():
        im = load(name)
        bg = Image.new("RGB", im.size, (255, 0, 255))
        bg.paste(im, (0, 0), im)
        regions = []
        for (rect, pts, side) in fx["reshape"]:
            regions.append(rect)
        for (rect, polys, tag) in fx["fill"] + fx["forcefill"]:
            regions.append(rect)
        if fx["glow"]:
            for zr, zp in fx["glow"]["zones"]:
                regions.append(zr)
        # 合并所有区域到一张大图（逐区域单独出图更清晰：每区域一张）
        for i, (x0, y0, x1, y1) in enumerate(sorted(set(regions))):
            pad = 30
            vx0, vy0 = max(0, x0-pad), max(0, y0-pad)
            vx1, vy1 = min(im.width, x1+pad), min(im.height, y1+pad)
            z = 3 if (x1-x0) < 260 else 2
            c = bg.crop((vx0, vy0, vx1, vy1)).resize(((vx1-vx0)*z, (vy1-vy0)*z), Image.NEAREST)
            d = ImageDraw.Draw(c)
            for gx in range(vx0//50*50, vx1+1, 50):
                X = (gx-vx0)*z; col = (255,255,0) if gx % 100 == 0 else (140,140,0)
                d.line([(X,0),(X,c.height)], fill=col, width=1)
                if gx % 100 == 0: d.text((X+2,2), str(gx), fill=(255,255,0))
            for gy in range(vy0//50*50, vy1+1, 50):
                Y = (gy-vy0)*z; col = (255,255,0) if gy % 100 == 0 else (140,140,0)
                d.line([(0,Y),(c.width,Y)], fill=col, width=1)
                if gy % 100 == 0: d.text((2,Y+2), str(gy), fill=(255,255,0))
            # 叠加
            for (rect, pts, clo) in fx["reshape"]:
                if not (rect[0] < x1 and rect[2] > x0 and rect[1] < y1 and rect[3] > y0):
                    continue
                cur = catmull_rom(pts, samples=30)
                d.line([( (x-vx0)*z, (y-vy0)*z) for x, y in cur], fill=(0,255,0), width=2)
                outp = close_outside(cur, clo)
                d.line([( (x-vx0)*z, (y-vy0)*z) for x, y in outp] + [((outp[0][0]-vx0)*z, (outp[0][1]-vy0)*z)],
                       fill=(255,120,0), width=1)
            for kind, colr in (("fill", (255,255,0)), ("forcefill", (0,255,255))):
                for (rect, polys, tag) in fx[kind]:
                    if not (rect[0] < x1 and rect[2] > x0 and rect[1] < y1 and rect[3] > y0):
                        continue
                    den = catmull_rom(list(polys)+[polys[0]], samples=18, closed=True) if len(polys) > 2 else polys
                    d.line([((x-vx0)*z, (y-vy0)*z) for x, y in den] +
                           [((den[0][0]-vx0)*z, (den[0][1]-vy0)*z)], fill=colr, width=2)
            if fx["glow"]:
                for zr, zp in fx["glow"]["zones"]:
                    if not (zr[0] < x1 and zr[2] > x0 and zr[1] < y1 and zr[3] > y0):
                        continue
                    den = catmull_rom(list(zp)+[zp[0]], samples=18, closed=True)
                    d.line([((x-vx0)*z, (y-vy0)*z) for x, y in den] +
                           [((den[0][0]-vx0)*z, (den[0][1]-vy0)*z)], fill=(255,0,255), width=2)
            c.save(os.path.join(AUD, f"pv_{name}_{i}.png"))
        print(f"preview {name}: {len(set(regions))} regions")

def apply():
    import cv2
    bk = os.path.join(AUD, "backup")
    os.makedirs(bk, exist_ok=True)
    for name in FIXES:
        src = os.path.join(WORK, f"role_{name}.png")
        dst = os.path.join(bk, f"role_{name}.png")
        if not os.path.exists(dst):
            shutil.copy2(src, dst)
    # divine 肚皮椭圆拟合（白色核心）
    a = np.array(load("divine"))
    gx0, gy0, gx1, gy1 = FIXES["divine"]["glow"]["fit_rect"]
    sub = a[gy0:gy1, gx0:gx1]
    m = (sub[..., 3] > 200) & (sub[..., :3].astype(int).min(axis=2) > 246)
    ys, xs = np.where(m)
    pts = np.stack([xs + gx0, ys + gy0], axis=1).astype(np.float32)
    (cx, cy), (MA, ma), ang = cv2.fitEllipse(pts)
    print(f"belly ellipse center=({cx:.0f},{cy:.0f}) axes=({MA:.0f}x{ma:.0f}) ang={ang:.1f}")

    only = sys.argv[2] if len(sys.argv) > 2 else None
    for name, fx in FIXES.items():
        if only and name != only:
            continue
        im = load(name)
        # 顺序：reshape（alpha 重塑）→ forcefill（闭洞）→ fill（颜色重建）→ glow（径向重建）
        for (rect, pts, clo) in fx["reshape"]:
            cur = catmull_rom(pts, samples=30)
            outp = close_outside(cur, clo)
            im = reshape_edge(im, rect, cur, outp, fill_band=8, sigma_lf=10)
            print(f"{name}: reshaped {rect}")
        for (rect, polys, tag) in fx["forcefill"]:
            im = force_alpha_fill(im, rect, [list(polys)], sigma_lf=12)
            print(f"{name}: forcefill {tag}")
        for (rect, polys, tag) in fx["fill"]:
            im = fill_zones(im, [(rect, [list(polys)])], sigma_lf=13, erode=10, sigma=9)
            print(f"{name}: fill {tag}")
        if fx["glow"]:
            zones = [(zr, list(zp)) for zr, zp in fx["glow"]["zones"]]
            im = radial_glow_rebuild(im, fx["glow"]["fit_rect"], zones,
                                     (cx, cy), (MA/2.0, ma/2.0), ang_deg=ang,
                                     r0=0.98, r1=1.5)
            print(f"{name}: glow rebuilt")
        save(name, im)
        print(f"{name}: saved")
    print("APPLY DONE")

if MODE == "preview":
    preview()
elif MODE == "apply":
    apply()
else:
    print("usage: python fix_round4.py preview|apply")
