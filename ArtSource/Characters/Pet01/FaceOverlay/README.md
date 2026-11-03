# Pet01 二维嘴眼表情贴脸面片（P5）

奶蛙（Pet01）的嘴眼表情用**两片贴脸面片**实现，不改冻结模型的网格、UV 和权重。面片 UV 固定 0..1，正好对应图集里的**一个格子**；换表情只改 `_MainTex_ST`，不换网格。

- `Pet01_FaceEyes`：x∈[-0.024, 0.024]、z∈[0.1785, 0.1965]（Blender 世界坐标，脸朝 +Y），96×36 段（0.5mm）；
- `Pet01_FaceMouth`：x∈[-0.0172, 0.0172]、z∈[0.1655, 0.1827]，28×14 段。

Unity 接线（P5-B，2026-10-27）：`ARPet/Assets/_Project/Scripts/Editor/FaceOverlaySetup.cs` 由菜单 5/6 调用，把两片挂到每只宠物的 Head 骨骼下；`PetPresenter.SetExpression` 按 `FaceExpressionTable` 选格，用 per-Renderer `MaterialPropertyBlock` 写 `_MainTex_ST`。着色器 `ZQXNXS/FaceDecal`（`Art/Characters/Pet01/Shaders/FaceDecal.shader`）。

## 产物

| 文件 | 说明 |
| --- | --- |
| `ARPet/.../Textures/Pet01_FaceEyes.png` | 2048×2048 RGBA，格子 1024×384，2 列 × 5 行，前 4 格有表情 |
| `ARPet/.../Textures/Pet01_FaceMouth.png` | 512×512 RGBA，格子 256×128，2 列 × 4 行，前 6 格有表情 |
| `ARPet/.../Models/Pet01_FaceOverlay.fbx` | 只含两个网格物体，导出参数与 `Pet01.fbx` 同一套 |
| `ARPet/.../Pet01/face_overlay_layout.json` | Unity 读取的布局：每格 `_MainTex_ST`、四色遮盖 UV、`viewOffsetMeters`；每次 Blender 运行重写 |
| `FaceOverlay/face_atlas_manifest.json` | 每格名称、像素矩形、`_MainTex_ST`、实测 Oral / 嘴缝范围 |
| `FaceOverlay/face_overlay_report.json` | 源 SHA 前后、几何统计、各视角遮挡与前移量扫描、原眼嘴覆盖审计、遮盖 UV、导出哈希、预览清单 |
| `Logs/FaceOverlay_20261027/*.png` | 16 张预览；`diagnose/` 下 7 张伪色诊断图（`--diagnose`） |
| `Logs/FaceOverlay_20261027/visual_review*.md` | 七轮视觉审查记录 |
| `Tools/Art/make_pet01_face_atlas.py` | 画图集（系统 python + PIL/numpy） |
| `Tools/Blender/build_pet01_face_overlay.py` | 生成面片、校验、导出 FBX、写布局与报告、渲预览 |

## 调用命令

```powershell
# 1) 画图集（输出 PNG；.meta 由 Unity 生成，导入设置由 FaceOverlaySetup 改为关 mip / Clamp / 双线性）
python Tools/Art/make_pet01_face_atlas.py

# 2) 生成面片 + 校验 + 导出 FBX + 布局 JSON + 报告 + 预览
D:/blender.exe -b ArtSource/Characters/Pet01/pet01.blend --python-exit-code 1 `
    --python Tools/Blender/build_pet01_face_overlay.py

#    可选参数：--no-preview（不渲图）、--no-compare（跳过遮盖方式对比）、--diagnose（加渲伪色诊断图）

# 3) 改了图集或面片后，让 Unity 重新装配 02_Main（Unity 须关闭）
Unity.exe -batchmode -projectPath ARPet -executeMethod ZQXNXS.ARPet.EditorTools.ProjectSetup.RebuildMainSceneForBatch -logFile <log>
```

顺序不能反：Blender 脚本读 `face_atlas_manifest.json` 校验格子宽高比，并做原眼嘴覆盖审计。

## 图集格子

`_MainTex_ST` 原点在左下。

| 图集 | 格子 | `_MainTex_ST` | 内容 |
| --- | --- | --- | --- |
| Eyes | `squint` | 0.5, 0.1875, 0, 0.8125 | 开心眯眼"^ ^" |
| Eyes | `closed` | 0.5, 0.1875, 0.5, 0.8125 | 平静闭眼细弧 + 短睫毛 |
| Eyes | `divine` | 0.5, 0.1875, 0, 0.625 | 纯白无瞳发光眼，白盘盖住整个原眼球，外圈短柔紫光 |
| Eyes | `rage` | 0.5, 0.1875, 0.5, 0.625 | 怒眉圆睁：白眼底盖住原眼球 + 大瞳孔 + 高光 |
| Mouth | `smile` | 0.5, 0.25, 0, 0.75 | 上扬微笑弧，线条中心在 Oral 中线 |
| Mouth | `laugh` | 0.5, 0.25, 0.5, 0.75 | 大笑口腔 + 上排白牙 + 粉舌，上沿盖过嘴缝 |
| Mouth | `roar` | 0.5, 0.25, 0, 0.5 | 怒吼：横向拉开、嘴角不上扬、小尖牙 |
| Mouth | `o` | 0.5, 0.25, 0.5, 0.5 | o 嘴，宽度吞掉整条嘴缝 |
| Mouth | `serene` | 0.5, 0.25, 0, 0.25 | 舒展细弧 |
| Mouth | `flat` | 0.5, 0.25, 0.5, 0.25 | 平直略波浪 |

## 几何与遮盖

- REST 姿态下逐点沿 −Y 投到 `Pet01_Mesh`，再沿命中三角形平滑法线外推（眼 1.2mm、嘴 1.0mm）。
- 四角有一角未命中的四边形直接删除，不插值；头部轮廓 3mm 内、法线朝前分量 <0.45 的**皮肤**四边形也删除（眼球侧面保留，否则原眼会从白盘边缘露出）。
- 嘴区用闭运算（先 max 再 min，半径 2 格，抬高上限 2mm）填平嘴缝凹槽，只填窄缝不抬凸面。
- 剩余遮挡由着色器解决：`ZQXNXS/FaceDecal` 把顶点朝相机前移 `viewOffsetMeters`（10mm）× max(0, cos(脸前方, 视线))，正面 10mm、35° 约 8mm、侧视 0。报告的 `hidden_by_offset_mm` 扫描显示 8mm 起正面与 ±35° 无被遮挡三角形。
- 遮盖：贴片显示时，原模型 Eyes / Oral 槽换成该色 Skin 材质副本，`_MainTex_ST=(0,0,u,v)` 钉在眼周 (0.250023, 0.896489) / 嘴周 (0.251083, 0.849268) 的皮肤纹素上，四色同 UV，颜色与光照与皮肤一致。`original_footprint_cover` 审计 divine / rage / laugh / roar / o 对原眼嘴的覆盖率为 100%。

## 不改冻结源的保证

- 脚本开头校验 `pet01.blend` SHA256（`a289a8db…3c9b`），不符退出码 2；脚本里没有 `save_mainfile` / `save_as_mainfile`，结束时再算一次 SHA 写进报告。
- 不运行 `validate_pet01.py`，不跑 promote；预览材质、相机与渲染设置都只在内存中。

## 已知限制

- 贴片只随 Head 骨骼整体动，不随眼睑 / 嘴骨变形；只在行为切换时换格（后续清单 L13）。
- 视觉审查第六轮：closed_serene 4.5/5、squint_laugh 3.9/5、rage_roar 3.8/5、nocover_o 3.5/5、divine_serene 2.7/5；第七轮只改 divine / rage，结论见 `visual_review_round7.md`。35° 掠射角的边缘梳齿、白盘 / 牙列台阶等残留问题记在后续清单 L12。
- 预览是正交相机 + EEVEE，Unity 实际观感（透视、Built-in 光照、手机屏幕）未审。
