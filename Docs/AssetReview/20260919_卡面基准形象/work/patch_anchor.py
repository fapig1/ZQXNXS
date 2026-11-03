# 修补：body 填充面 UV 改统一锚点（肚皮区身体顶点平均 UV）；arm 填充面用前臂区平均 UV
s = open("pose_render3.py", encoding="utf-8").read()
old = """    # UV：每个填充面 loop 取"最近肚皮顶点"的 UV（贴图与肚皮一致，避免采样到臂面棕区）
    uvlay = bm.loops.layers.uv.active
    if uvlay is not None:
        bpos, buv = [], []
        for v in bm.verts:
            if not v.is_valid: continue
            if pos2armw.get(pos_key(v.co), 0.0) >= 0.2: continue
            uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            if not uvs: continue
            bpos.append([v.co.x, v.co.y, v.co.z])
            buv.append([sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)])
        B = np.array(bpos); U = np.array(buv)
        n_uv = 0
        step = max(1, len(B)//64)
        for f in body_fill_faces + arm_fill_faces:
            for loop in f.loops:
                co = np.array([loop.vert.co.x, loop.vert.co.y, loop.vert.co.z])
                best = None
                for s0 in range(0, len(B), 4096):
                    d2 = ((B[s0:s0+4096] - co)**2).sum(-1)
                    j = int(np.argmin(d2))
                    if best is None or d2[j] < best[0]:
                        best = (d2[j], U[s0+j])
                loop[uvlay].uv = best[1]
                n_uv += 1
        p(f"SURGERY uv nearest-body assigned loops={n_uv} skin_slot={skin_idx}")"""
new = """    # UV：body 填充面统一锚点=肚皮区身体顶点平均 UV（肚皮贴图近纯色，避免焊缝过渡色）；
    # arm 填充面锚点=前臂区顶点平均 UV（隐藏膜，颜色近似即可）
    uvlay = bm.loops.layers.uv.active
    if uvlay is not None:
        belly_uvs, arm_uvs = [], []
        for v in bm.verts:
            if not v.is_valid: continue
            uvs = [l[uvlay].uv for f2 in v.link_faces for l in f2.loops if l.vert.index == v.index]
            if not uvs: continue
            au = [sum(u.x for u in uvs)/len(uvs), sum(u.y for u in uvs)/len(uvs)]
            aw_v = pos2armw.get(pos_key(v.co), 0.0)
            co = v.co
            if aw_v < 0.05 and abs(co.x) < 0.035 and co.y > 0.03 and 0.04 < co.z < 0.13:
                belly_uvs.append(au)
            if aw_v > 0.8 and d_zone(co) < 0.02:
                arm_uvs.append(au)
        belly_anchor = (sum(u[0] for u in belly_uvs)/len(belly_uvs), sum(u[1] for u in belly_uvs)/len(belly_uvs)) if belly_uvs else (0.5, 0.5)
        arm_anchor = (sum(u[0] for u in arm_uvs)/len(arm_uvs), sum(u[1] for u in arm_uvs)/len(arm_uvs)) if arm_uvs else belly_anchor
        n_uv = 0
        for f in body_fill_faces:
            for loop in f.loops:
                loop[uvlay].uv = belly_anchor; n_uv += 1
        for f in arm_fill_faces:
            for loop in f.loops:
                loop[uvlay].uv = arm_anchor; n_uv += 1
        p(f"SURGERY uv anchors: belly={tuple(round(x,3) for x in belly_anchor)} "
          f"arm={tuple(round(x,3) for x in arm_anchor)} loops={n_uv} skin_slot={skin_idx}")"""
assert old in s
s = s.replace(old, new)
open("pose_render3.py", "w", encoding="utf-8").write(s)
print("patched anchor uv")
