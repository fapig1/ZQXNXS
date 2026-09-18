"""Build local review boards, silent MP4/GIF previews and an offline viewer.

Run with system Python after Blender renders. Requires Pillow and the already
installed imageio-ffmpeg; does not download assets or use external web scripts.
"""
from __future__ import annotations

import hashlib
import json
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps
import imageio_ffmpeg

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/"ArtSource/Characters/Pet01"
PREVIEWS=SOURCE/"Previews"
ART=ROOT/"ARPet/Assets/_Project/Art/Characters/Pet01"
MANIFEST=json.loads((SOURCE/"asset_manifest.json").read_text(encoding="utf-8"))
SOURCE_HASH=hashlib.sha256((SOURCE/"pet01.blend").read_bytes()).hexdigest()
INK="#253F36"
MUTED="#69766E"
PAPER="#F4F3E9"


def font(size,bold=False):
    return ImageFont.truetype("C:/Windows/Fonts/"+("msyhbd.ttc" if bold else "msyh.ttc"),size)


def title(canvas,heading,subheading):
    d=ImageDraw.Draw(canvas)
    d.text((42,27),heading,font=font(48,True),fill=INK)
    d.text((44,90),subheading,font=font(23),fill=MUTED)


def panel(canvas,file,xy,size,label,detail=""):
    x,y=xy
    w,h=size
    d=ImageDraw.Draw(canvas)
    d.rounded_rectangle((x,y,x+w,y+h+76),radius=16,fill="white",outline="#DDDFD2",width=2)
    source=Image.open(file).convert("RGBA")
    image=Image.new("RGB",source.size,"#FAFAF6")
    image.paste(source,mask=source.getchannel("A"))
    image=image.resize((w-16,h-16),Image.Resampling.LANCZOS)
    canvas.paste(image,(x+8,y+8))
    d.text((x+18,y+h+5),label,font=font(25,True),fill=INK)
    if detail:
        d.text((x+18,y+h+42),detail,font=font(17),fill=MUTED)


def turnaround():
    canvas=Image.new("RGB",(1920,790),PAPER)
    title(canvas,"奶蛙 · Pet01", "按 Meshy 候选重做比例 / 站姿轮廓与配色 / 2026.09.11")
    for index,(view,label,detail) in enumerate((
        ("hero","斜视","按用户提供的 Meshy 对照重建"),
        ("front","正面","金黄色身体 · 奶油色腹部"),
        ("side","侧面","保留鼓腹，双臂弯曲放在腹前"),
        ("back","背面","收窄肩背，修顺肩髋衔接"),
    )):
        panel(canvas,PREVIEWS/("idle_001_"+view+".png"),(42+index*470,146),(426,469),label,detail)
    geo=MANIFEST["geometry"]
    ImageDraw.Draw(canvas).text((44,734),
        f"高 20 cm     {geo['triangles']:,} 三角面     26 根骨骼     2048 × 2048 色彩图集     Blender 资产预览，Unity / 真机待验证",
        font=font(23),fill=INK)
    canvas.save(PREVIEWS/"Pet01_Turnaround.png")


def proportion_comparison():
    directory=PREVIEWS/"Proportions"
    report=json.loads((directory/"proportion_report.json").read_text(encoding="utf-8"))
    if report["source_files"]["after"]["sha256"]!=SOURCE_HASH:
        raise RuntimeError("Proportion comparison does not match current source")
    canvas=Image.new("RGB",(1920,1760),PAPER)
    title(canvas,"比例修正 · 以你提供的 Meshy 为准", "相同 20 cm 身高、正交相机与灯光 / 候选为无贴图灰模 / 2026.09.11")
    for row,view in enumerate(("front","side")):
        for col,label in enumerate(("before","candidate","after")):
            name={"before":"调整前","candidate":"你提供的 Meshy","after":"调整后"}[label]
            side_name="正面" if view=="front" else "侧面"
            axis=0 if view=="front" else 1
            dimension=report["geometry"][label]["dimensions_m"][axis]*100
            note=("全宽" if view=="front" else "全厚")+f" {dimension:.2f} cm"
            if label=="candidate":
                note+=" · 仅作比例基准"
            elif label=="after":
                note+=" · 含可动的眼睑与嘴部"
            panel(canvas,directory/(label+"_"+view+".png"),(42+col*628,146+row*745),
                  (592,651),side_name+" · "+name,note)
    ImageDraw.Draw(canvas).text((44,1689),
        "收窄肩腹，双臂改为屈肘扶腹，重新调整腿脚和脸的位置；候选原文件及修改前版本均已保留。",
        font=font(24),fill=INK)
    canvas.save(PREVIEWS/"Pet01_Proportion_Comparison.png")


def skeleton():
    projection=json.loads((PREVIEWS/"skeleton_projection.json").read_text(encoding="utf-8"))
    if projection["source_sha256"]!=SOURCE_HASH:
        raise RuntimeError("Skeleton projection does not match the current blend")
    canvas=Image.open(PREVIEWS/"idle_001_front.png").convert("RGB").resize((600,660),Image.Resampling.LANCZOS)
    canvas=Image.blend(canvas,Image.new("RGB",canvas.size,"#F6F4EA"),.48)
    d=ImageDraw.Draw(canvas)
    for bone in projection["bones"]:
        name=bone["name"]
        color="#277BBA"
        if any(name.startswith(p) for p in ("UpperArm.","Forearm.","Hand.")):
            color="#078366"
        elif any(name.startswith(p) for p in ("Thigh.","Shin.","Foot.")):
            color="#895DB0"
        elif any(name.startswith(p) for p in ("Eye.","Lid", "Mouth")):
            color="#D67825"
        head=tuple(round(v*s) for v,s in zip(bone["head"],canvas.size))
        tail=tuple(round(v*s) for v,s in zip(bone["tail"],canvas.size))
        d.line((head,tail),fill="white",width=7)
        d.line((head,tail),fill=color,width=4)
        for x,y in (head,tail):
            d.ellipse((x-4,y-4,x+4,y+4),fill=color,outline="white",width=1)
        if name in ("Root","Head","Chest","Pelvis"):
            x,y=head
            dx=70 if name in ("Root","Chest") else -140
            label="Root · 脚底原点" if name=="Root" else name
            d.line(((x,y),(x+dx,y)),fill=color,width=2)
            d.text((x+dx+4 if dx>0 else x+dx-5,y-26),label,font=font(18,True),fill=color)
    canvas.save(PREVIEWS/"Pet01_Skeleton.png")

    board=Image.new("RGB",(1920,1020),PAPER)
    title(board,"骨骼与表情", "26 根骨骼 / FK 控制 / 骨骼驱动眼睑与嘴部 / 可编辑 Blender 源文件")
    panel(board,PREVIEWS/"Pet01_Skeleton.png",(42,146),(560,616),"骨骼投影叠加","根节点位于双脚底中心")
    faces=(("idle_001_face.png","默认","绿色眼睛 · 细嘴缝"),
           ("idle_037_face.png","眨眼","上下眼睑合拢"),
           ("eat_029_face.png","咀嚼","口腔 · 钝牙 · 舌头"))
    for index,(file,label,detail) in enumerate(faces):
        panel(board,PREVIEWS/file,(632+index*420,146),(396,436),label,detail)
    d=ImageDraw.Draw(board)
    d.text((642,707),"身体 6  ·  双臂 6  ·  双腿 6  ·  面部 8",font=font(31,True),fill=INK)
    d.text((642,763),"首轮动作：Idle 待机 / Eat 进食",font=font(26),fill=INK)
    d.text((642,809),"每顶点最多 3 个骨骼影响，权重已归一化。",font=font(23),fill=MUTED)
    d.text((44,943),"已检查连续表面、嘴部开合、眼睑、循环端点和脚底固定；大幅挥手、行走和下蹲仍需单独制作与检查。",
           font=font(23),fill=INK)
    board.save(PREVIEWS/"Pet01_RigAndFace.png")


def encode(action):
    directory=PREVIEWS/"Frames"/action
    meta=json.loads((directory/"sequence.json").read_text(encoding="utf-8"))
    if meta.get("source_sha256")!=SOURCE_HASH:
        raise RuntimeError(action+" frames do not match the current blend")
    paths=sorted(directory.glob(action+"_*.png"))
    if len(paths)!=len(meta["source_frames"]):
        raise RuntimeError(action+" preview frame count mismatch")
    frames=[]
    for index,path in enumerate(paths):
        frame=Image.new("RGB",(600,730),PAPER)
        d=ImageDraw.Draw(frame)
        label="Idle · 待机" if action=="Idle" else "Eat · 进食"
        d.text((18,10),label,font=font(23,True),fill=INK)
        note="轻呼吸 / 眨眼 / 3.0s 循环" if action=="Idle" else "低头 / 咀嚼 / 吞咽 / 3.2s 单次"
        d.text((18,41),note,font=font(15),fill=MUTED)
        frame.paste(Image.open(path).convert("RGB"),(0,70))
        frames.append(frame)
    ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
    output=PREVIEWS/("Pet01_"+action+".mp4")
    command=[ffmpeg,"-hide_banner","-loglevel","error","-y","-f","rawvideo",
             "-pixel_format","rgb24","-video_size","600x730","-framerate",str(meta["preview_fps"]),
             "-i","-","-an","-c:v","libx264","-preset","medium","-crf","20",
             "-pix_fmt","yuv420p","-movflags","+faststart",str(output)]
    process=subprocess.Popen(command,stdin=subprocess.PIPE,stderr=subprocess.PIPE)
    try:
        for frame in frames:
            process.stdin.write(frame.tobytes())
        process.stdin.close()
        code=process.wait(timeout=60)
        errors=process.stderr.read().decode("utf-8",errors="replace")
        if code:
            raise RuntimeError(errors)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        process.stderr.close()
    decode=subprocess.run([ffmpeg,"-v","error","-i",str(output),"-f","null","-"],
                          capture_output=True,timeout=60)
    if decode.returncode:
        raise RuntimeError(decode.stderr.decode("utf-8",errors="replace"))
    small=[f.resize((420,511),Image.Resampling.LANCZOS) for f in frames]
    palette_source=Image.new("RGB",(420,511*3))
    for index,f in enumerate((small[0],small[len(small)//3],small[len(small)//2])):
        palette_source.paste(f,(0,index*511))
    palette=palette_source.quantize(colors=256)
    gif=[f.quantize(palette=palette,dither=Image.Dither.NONE) for f in small]
    durations=[(70,60,70)[i%3] for i in range(len(gif))]
    # GIF loops are for reviewing the one-shot action repeatedly; FBX Eat does not loop.
    if action=="Eat":
        durations[-1]+=800
    gif[0].save(PREVIEWS/("Pet01_"+action+".gif"),save_all=True,append_images=gif[1:],
                duration=durations,loop=0,optimize=False,disposal=2)
    print("PREVIEW",action,len(frames),"frames; MP4 fully decoded",flush=True)
    return {"frames":len(frames),"preview_fps":meta["preview_fps"],"clip_duration_seconds":meta["duration_seconds"],
            "mp4_decoded":True,"silent":True,"mp4_sha256":hashlib.sha256(output.read_bytes()).hexdigest()}


def storyboard():
    board=Image.new("RGB",(1920,590),PAPER)
    title(board,"Eat · 进食动作分镜", "3.2 秒 / 三次咀嚼 / 一次吞咽结算 / 回到中性站姿")
    for i,(index,label) in enumerate(((0,"准备"),(6,"低头"),(14,"第一口"),(29,"第三口"),(36,"吞咽"),(47,"恢复"))):
        panel(board,PREVIEWS/"Frames/Eat"/("Eat_%04d.png"%index),
              (42+i*314,146),(290,319),label,f"{index/15:.2f} s")
    ImageDraw.Draw(board).text((44,551),"Consume：2.4s（Blender 第 73 帧）。这里只提供动作标记，Unity 业务结算需由单一接收入口完成。",
                              font=font(21),fill=INK)
    board.save(PREVIEWS/"Pet01_Eat_Storyboard.png")


def html_viewer():
    html="""<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>奶蛙 Pet01 · 模型与动作预览</title>
<style>
:root{font-family:system-ui,"Microsoft YaHei",sans-serif;color:#253f36;background:#f4f3e9}*{box-sizing:border-box}
body{margin:0 auto;padding:32px;max-width:1360px}h1{font-size:40px;margin:0 0 8px}p{line-height:1.7;margin:10px 0}
.meta{color:#69766e}.facts{display:flex;gap:10px;flex-wrap:wrap;margin:24px 0}.facts span{background:#e4e8d9;border-radius:24px;padding:9px 18px}
main{display:grid;grid-template-columns:1.1fr 1fr;gap:24px}.card{background:white;border:1px solid #dddfd2;border-radius:18px;overflow:hidden;padding:20px}
h2{margin:0 0 14px;font-size:23px}.tabs{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px}button,a{font:inherit}
button{border:1px solid #cbd4c6;background:#f7f8f1;color:#253f36;padding:9px 16px;border-radius:20px;cursor:pointer}
button[aria-pressed=true]{background:#253f36;color:white}button:focus-visible,a:focus-visible{outline:3px solid #d3a12e;outline-offset:3px}
#shape{width:100%;height:590px;object-fit:contain;background:#f5f5ee;border-radius:12px}video{width:100%;height:520px;background:#e9eee4;border-radius:12px}
.links{display:flex;gap:12px;flex-wrap:wrap;margin:26px 0}.links a{display:inline-block;color:#253f36;background:#e4e8d9;padding:12px 18px;border-radius:10px;text-decoration:none}
details{background:white;padding:18px 22px;border-radius:12px;margin-top:20px}summary{cursor:pointer;font-weight:600}details img{width:100%;margin-top:16px}
footer{font-size:14px;color:#69766e;margin:22px 0}@media(max-width:800px){body{padding:18px}main{grid-template-columns:1fr}h1{font-size:30px}#shape{height:480px}}
</style>
<header><h1>奶蛙 · Pet01</h1><p class="meta">按 Meshy 候选重做比例 · 2026.09.11</p>
<p>依据你提供的 <a href="Candidates/Meshy_AI_Golden_Guardian_0910135714_generate.glb" download>Meshy 模型</a>，收窄肩腹、改为屈肘扶腹，并重新调整腿脚站姿和脸的位置。配色继续参考 <a href="../../../Docs/AssetReview/20260908_naiwa/expression_frames.jpg">奶蛙图片</a>。</p>
<div class="facts"><span>20 cm</span><span>__TRIANGLES__ 三角面</span><span>26 根骨骼</span><span>Idle + Eat</span><span>Blender / FBX</span></div></header>
<main><section class="card"><h2>造型与骨骼</h2><div class="tabs" id="views">
<button aria-pressed="true" data-src="idle_001_hero.png">斜视</button><button aria-pressed="false" data-src="idle_001_front.png">正面</button>
<button aria-pressed="false" data-src="idle_001_side.png">侧面</button><button aria-pressed="false" data-src="idle_001_back.png">背面</button>
<button aria-pressed="false" data-src="Pet01_Skeleton.png">骨骼</button></div><img id="shape" src="Previews/idle_001_hero.png" alt="奶蛙斜视预览"></section>
<section class="card"><h2>动作播放</h2><div class="tabs" id="actions"><button aria-pressed="true" data-action="Idle">待机 · 3.0s</button>
<button aria-pressed="false" data-action="Eat">进食 · 3.2s</button></div>
<video id="player" controls playsinline loop preload="metadata" src="Previews/Pet01_Idle.mp4" poster="Previews/idle_001_hero.png"></video>
<p id="action-note">轻微呼吸、眨眼和摆尾，循环播放。</p><p class="meta">预览为无声视频。眼睑和嘴部由骨骼控制。</p></section></main>
<div class="links"><a href="pet01.blend" download>Blender 源文件</a><a href="../../../ARPet/Assets/_Project/Art/Characters/Pet01/Models/Pet01.fbx" download>模型 FBX</a>
<a href="../../../ARPet/Assets/_Project/Art/Characters/Pet01/Models/Pet01_Idle.fbx" download>Idle FBX</a><a href="../../../ARPet/Assets/_Project/Art/Characters/Pet01/Models/Pet01_Eat.fbx" download>Eat FBX</a>
<a href="../../../Docs/奶蛙首轮资产与动作说明.md">资产与导入说明</a><a href="validation_report.json">检查记录</a></div>
<details open><summary>调整前 / 你提供的 Meshy / 调整后</summary><img src="Previews/Pet01_Proportion_Comparison.png" alt="相同尺度下，调整前、Meshy 候选、调整后的正面与侧面比较"></details>
<details><summary>轮廓叠图</summary><p>灰色为重合，蓝色为候选多出的部分，橙色为当前模型多出的部分。此图只用于检查轮廓差异。</p><img src="Previews/Proportions/after_front_overlay.png" alt="当前模型与候选的正面轮廓叠图"><img src="Previews/Proportions/after_side_overlay.png" alt="当前模型与候选的侧面轮廓叠图"></details>
<details><summary>模型三视图与骨骼、表情检查图</summary><img src="Previews/Pet01_Turnaround.png" alt="奶蛙多视角检查图">
<img src="Previews/Pet01_RigAndFace.png" alt="骨骼与表情检查图"></details>
<details><summary>进食动作分镜与结算时刻</summary><img src="Previews/Pet01_Eat_Storyboard.png" alt="进食动作分镜">
<p>2.4 秒是唯一建议结算时刻。Blender 标记尚未接入 Unity 业务事件，重复投喂和跟踪恢复规则需在游戏逻辑中实现。</p></details>
<footer>已进行 Blender 源资产与 FBX 回读检查。Unity Generic 导入、渲染管线材质、Android 设备表现待验证。侧背不可见结构按本项目需求补设计。</footer>
<script>
document.querySelectorAll('#views button').forEach(b=>b.addEventListener('click',()=>{
document.querySelectorAll('#views button').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));
const img=document.getElementById('shape');img.src='Previews/'+b.dataset.src;img.alt='奶蛙'+b.textContent+'预览';}));
document.querySelectorAll('#actions button').forEach(b=>b.addEventListener('click',()=>{
document.querySelectorAll('#actions button').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));
const player=document.getElementById('player'),a=b.dataset.action;player.pause();player.src='Previews/Pet01_'+a+'.mp4';player.loop=a==='Idle';
document.getElementById('action-note').textContent=a==='Idle'?'轻微呼吸、眨眼和摆尾，循环播放。':'低头、三次咀嚼、吞咽、恢复站姿。动作单次播放。';player.load();player.play().catch(()=>{});}));
</script></html>"""
    (SOURCE/"review.html").write_text(html.replace("__TRIANGLES__",f"{MANIFEST['geometry']['triangles']:,}"),encoding="utf-8")


def main():
    if MANIFEST.get("source_sha256")!=SOURCE_HASH or not MANIFEST["status"].get("blender_fbx_roundtrip"):
        raise RuntimeError("Validate the current source and exports before packaging")
    turnaround()
    proportion_comparison()
    skeleton()
    videos={name:encode(name) for name in ("Idle","Eat")}
    storyboard()
    html_viewer()
    report={"source_sha256":SOURCE_HASH,"videos":videos,"still_review_scope":"Front, side, back, hero and facial key poses",
            "note":"Video decoding is checked; Unity and Android are not tested."}
    (PREVIEWS/"preview_manifest.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    MANIFEST["status"]["previews_generated"]=True
    MANIFEST["review_page"]="ArtSource/Characters/Pet01/review.html"
    MANIFEST["previews"]=["Pet01_Turnaround.png","Pet01_Proportion_Comparison.png","Pet01_RigAndFace.png","Pet01_Eat_Storyboard.png",
                         "Pet01_Idle.mp4","Pet01_Eat.mp4","Pet01_Idle.gif","Pet01_Eat.gif"]
    for target in (SOURCE/"asset_manifest.json",ART/"asset_manifest.json"):
        target.write_text(json.dumps(MANIFEST,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print("Review package written:",SOURCE/"review.html",flush=True)


if __name__=="__main__":
    main()
