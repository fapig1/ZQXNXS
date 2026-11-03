# 修补：arm 填充膜 UV 改为从相邻原始臂面拷贝周圈 UV（读作手臂内侧皮肤）
s = open("pose_render3.py", encoding="utf-8").read()
old = """        n_uv = 0
        for f in body_fill_faces:
            for loop in f.loops:
                loop[uvlay].uv = belly_anchor; n_uv += 1
        for f in arm_fill_faces:
            for loop in f.loops:
                loop[uvlay].uv = arm_anchor; n_uv += 1
        p(f"SURGERY uv anchors: belly={tuple(round(x,3) for x in belly_anchor)} "
          f"arm={tuple(round(x,3) for x in arm_anchor)} loops={n_uv} skin_slot={skin_idx}")"""
new = """        n_uv = 0
        for f in body_fill_faces:
            for loop in f.loops:
                loop[uvlay].uv = belly_anchor; n_uv += 1
        # arm 膜：周圈 UV 从相邻原始臂面拷贝（膜朝向镜头时读作手臂内侧皮肤）
        fill_ids = set(id(f) for f in body_fill_faces) | set(id(f) for f in arm_fill_faces)
        for f in arm_fill_faces:
            for loop in f.loops:
                v, e = loop.vert, loop.edge
                src = None
                for f2 in e.link_faces:
                    if id(f2) in fill_ids: continue
                    if not f2.is_valid: continue
                    for l2 in f2.loops:
                        if l2.vert.index == v.index:
                            src = l2; break
                    if src: break
                if src is not None:
                    loop[uvlay].uv = src[uvlay].uv
            n_uv += 1
        p(f"SURGERY uv: belly_anchor={tuple(round(x,3) for x in belly_anchor)} loops={n_uv} skin_slot={skin_idx}")"""
assert old in s
s = s.replace(old, new)
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched arm membrane uv")
