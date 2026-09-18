"""Compare actual orthographic silhouettes and cross-sections; no AI image use."""
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"ArtSource/Characters/Pet01/Previews/Proportions"
manifest=json.loads((OUT/"capture_manifest.json").read_text(encoding="utf-8"))
geometry={label:np.load(OUT/(label+"_geometry.npz")) for label in ("before","candidate","after")}
SIZE=1200
SPAN=.24


def mask(label,view):
    data=geometry[label]
    v=data["vertices"]
    coords=np.stack((SIZE/2+v[:,0 if view=="front" else 1]*SIZE/SPAN,
                     SIZE*(.215-v[:,2])/SPAN),axis=1)
    canvas=Image.new("1",(SIZE,SIZE),0)
    draw=ImageDraw.Draw(canvas)
    for indices in data["triangles"]:
        draw.polygon([tuple(p) for p in coords[indices]],fill=1)
    return np.array(canvas,dtype=bool)


def section(label,z):
    data=geometry[label]
    pts=data["vertices"][data["triangles"]]
    pts=pts[(pts[:,:,2].min(axis=1)<=z)&(pts[:,:,2].max(axis=1)>=z)]
    intersections=[]
    for i,j in ((0,1),(1,2),(2,0)):
        a,b=pts[:,i],pts[:,j]
        valid=((a[:,2]-z)*(b[:,2]-z)<=0)&(np.abs(b[:,2]-a[:,2])>1e-12)
        a,b=a[valid],b[valid]
        t=(z-a[:,2])/(b[:,2]-a[:,2])
        intersections.append(a+(b-a)*t[:,None])
    points=np.concatenate(intersections,axis=0)
    return {"width_m":float(np.ptp(points[:,0])),"back_y_m":float(points[:,1].min()),
            "front_y_m":float(points[:,1].max())}


result={"source_files":manifest["files"],"method":
        "Triangle-rasterized orthographic silhouettes at 0.2 mm/pixel; intersection-over-union is only a shape diagnostic, not user approval.",
        "geometry":manifest["geometry"],"silhouettes":{},"cross_sections":[]}
for view in ("front","side"):
    reference=mask("candidate",view)
    result["silhouettes"][view]={}
    for label in ("before","after"):
        compared=mask(label,view)
        intersection=(reference&compared).sum()
        union=(reference|compared).sum()
        result["silhouettes"][view][label]={"intersection_over_union":float(intersection/union)}
        image=np.full((SIZE,SIZE,3),248,dtype=np.uint8)
        image[reference&compared]=(190,190,166)
        image[reference&~compared]=(78,149,191)
        image[~reference&compared]=(222,139,64)
        Image.fromarray(image).save(OUT/(label+"_"+view+"_overlay.png"))
for z in (.02,.04,.06,.075,.09,.105,.115,.13,.14,.15,.16,.175,.185,.195):
    result["cross_sections"].append({"height_m":z,**{label:section(label,z) for label in geometry}})
for label,geo in result["geometry"].items():
    geo["width_to_height"]=geo["dimensions_m"][0]/geo["dimensions_m"][2]
    geo["depth_to_height"]=geo["dimensions_m"][1]/geo["dimensions_m"][2]
result["notes"]=["The supplied GLB has no materials, UVs, textures, rig or clips.",
                  "The revised mesh is locally authored against its proportions; expressions and the small tail remain project-authored.",
                  "Hand clearance and eye details intentionally differ. Unity and Android have not been tested."]
(OUT/"proportion_report.json").write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps({"silhouettes":result["silhouettes"],"dimensions":{k:v["dimensions_m"] for k,v in result["geometry"].items()}},indent=2))
