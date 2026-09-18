"""Rebuild comparison evidence, export/validate and package the revised asset.

Run with system Python after build_pet01.py --phase build. All outputs stay
inside the project; each dependent stage must succeed before the next starts.
"""
import subprocess
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
LOGS=ROOT/"ArtSource/Characters/Pet01/Logs/20260911_meshy"
LOGS.mkdir(parents=True,exist_ok=True)
BLENDER=["D:/blender.exe","--background","--factory-startup","--python-exit-code","1"]


def blender(script,*arguments):
    return BLENDER+["--python",str(ROOT/"Tools/Blender"/script)]+(["--",*arguments] if arguments else [])


stages=[
    ("comparison",blender("compare_pet01_proportions.py")),
    ("proportions",[sys.executable,"Tools/Blender/analyze_pet01_proportions.py"]),
    ("export",blender("build_pet01.py","--phase","export")),
    ("validation",blender("validate_pet01.py","--roundtrip")),
    ("views",blender("build_pet01.py","--phase","preview","--views","hero,front,side,back,face","--size","900","--samples","32")),
    ("blink",blender("build_pet01.py","--phase","preview","--action","Idle","--frame","37","--views","face","--size","850","--samples","32")),
    ("eat_keyposes",blender("build_pet01.py","--phase","preview","--action","Eat","--frame","29","--views","hero,side,face","--size","850","--samples","32")),
    ("idle_sequence",blender("build_pet01.py","--phase","sequence","--action","Idle","--size","600","--samples","16","--step","2")),
    ("eat_sequence",blender("build_pet01.py","--phase","sequence","--action","Eat","--size","600","--samples","16","--step","2")),
    ("review",[sys.executable,"Tools/Blender/make_pet01_review.py"]),
]
for name,command in stages:
    print("START",name,flush=True)
    logfile=LOGS/(name+".log")
    with logfile.open("w",encoding="utf-8") as stream:
        result=subprocess.run(command,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,timeout=900,
                              creationflags=subprocess.CREATE_NO_WINDOW if sys.platform=="win32" else 0)
    if result.returncode:
        print(logfile.read_text(encoding="utf-8",errors="replace")[-6000:],flush=True)
        raise SystemExit(f"Stage {name} failed; see {logfile}")
    print("DONE",name,flush=True)
print("Comparison, 3 FBX exports, validation, stills and motion previews are ready.",flush=True)
