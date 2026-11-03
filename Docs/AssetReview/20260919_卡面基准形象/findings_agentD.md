# Agent D 调研报告：Vuforia Image Target 官方设计要求与识别卡规格建议

日期：2026-09-19
调研方式：WebFetch / web_reader 抓取 Vuforia 官方文档库（developer.vuforia.com/library/vuforia-engine/）原文页面。
文中凡标注【官方原文】均为 developer.vuforia.com 官方文档逐句或近逐句引用；标注【推断】为本报告基于官方原文的推理，不代表官方立场。

引用的官方页面（本次全部实测可达，404 的旧路径已换为新路径）：

| 简称 | URL |
|---|---|
| [Overview] | https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/image-targets/ |
| [Physical] | https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/best-practices/physical-properties-image-based-targets |
| [BestPractices] | https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/best-practices/best-practices-designing-and-developing-image-based-targets |
| [Optimization] | https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/best-practices/image-targets-optimization-techniques |
| [TargetManager] | https://developer.vuforia.com/library/vuforia-engine/getting-started/engine-developer-portal/vuforia-target-manager |
| [DeviceDB] | https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/device-databases/device-databases |

注意：旧文档路径（如 `.../image-targets/physical-properties-of-image-targets/`、`vuforia-image-recognition-and-relevance/`）现返回 "Engine Developer Portal" 空壳页；新文档把相关内容归入 `/best-practices/` 子目录。本文引用一律用新路径。

---

## 1. 长宽比：支持非正方形；Width/Height 的定义

【官方原文 · DeviceDB】"The dimensions you provide to an image-based target return the pose information in that scale. For example, if your Image Target is 16 units wide, moving the camera from the target's left border to the target's right border will change the returned position by 16 units along the x-axis."
即 **Width 是目标左边界到右边界的距离（目标平面内 x 轴方向的那条边）**；Height 对应目标平面内的另一条边（竖直边）。

【官方原文 · TargetManager】"You must enter a name, dimensions, and image source for each target. **The target size that you define should represent the actual aspect ratio of your image.** For example, a 20-centimeter-wide printed Image Target will have a width of 0.20 in meters."
即 Target Manager 允许你分别为目标定义宽和高，**只要宽高比与上传图像的实际宽高比一致即可**——非正方形（如 63:88 竖矩形）是明确支持的。DeviceDB 页还提到运行期动态加载时 "A message will indicate the expected aspect ratio if the aspect ratio is incorrect"，即引擎会校验宽高比。

【官方原文 · BestPractices · Rating 一节】"The following image targets have ratings that range between 0 and 5 stars. The Target Star Rating ... includes both **rectangular and non-rectangular shapes**. This is intended as a guideline as it includes the shape of a target, non-rectangular shapes such as circles, triangles, and polygons may be used as well."
即连非矩形（圆、三角、多边形）都可用于评级与跟踪，竖直矩形不在话下。

【推断 · 比例建议】官方没有为普通 Image Target 规定"推荐比例"（那是 VuMark 的设计规范才有的事）。评级只针对图像本身的特征质量，不因长宽比本身扣分。竖矩形完全可行，只要在 Target Manager 与 Unity `ImageTargetBehaviour`（`mWidth`/`mHeight`，即 Inspector 的 Width/Height）中按实际印刷尺寸分别填入即可。结合本项目已反射核实的字段：竖卡应填 `mWidth=0.070`（短边，对应场景 X 轴）、`mHeight=0.120`（长边，对应目标平面内竖直边），单位米。

## 2. 物理尺寸：官方建议范围与"尺寸须与实际印刷一致"的依据

【官方原文 · Physical】
- "A physical printed Image Target **should be at least 5 inches, or 12 centimeters, in width** and of reasonable height for a good AR experience."
- "An Image Target should be **flat, rigid, and made of matte materials**. A foam board or thick paper are examples of good substrates."
- "Avoid glossy materials, as they can cause specular reflections and affect the tracking performance."
- "The recommended size varies based on the **actual target rating and the distance to the physical Image Target**."

【官方原文 · Physical / BestPractices · 尺寸与识别距离】"the larger the distance to the target, the bigger your target should be. As a rule of thumb, Image Targets that are 20 to 30 cm in width are detectable from about 2 to 3 meters away."（即约 10 倍关系的经验法则：目标宽度 ×10 ≈ 可识别距离。）

【官方原文 · TargetManager / DeviceDB · 尺寸一致性】"The target size that you define should represent the actual aspect ratio of your image. For example, a 20-centimeter-wide printed Image Target will have a width of 0.20 in meters."；DeviceDB："The dimensions you provide to an image-based target return the pose information in that scale."
—— 印刷尺寸与数据库/场景中定义的尺寸不一致不会让识别立刻失败（特征匹配与绝对尺度无关），**但会直接导致 AR 内容比例失真、锚定位置错乱**，所以"必须与实际印刷尺寸一致"是官方对尺度正确性的明确要求（0.1 m 卡就填 0.1 m）。

【官方原文 · 上传图像规格 · TargetManager/Overview】图像格式 JPG 或 PNG、RGB 或灰度、**不含 alpha 通道**；单图上限 2.25 MB；**最小分辨率 320 像素**。（低分辨率上传会提示 "Low resolution" 并影响评级上限。）

【推断 · 上限】官方未给出普通 Image Target 的最大尺寸上限（只受上传图像分辨率约束）。本项目是"手持手机俯视桌面 20~40 cm"的场景，物理上限不构成约束。

【推断 · 对本项目的含义】现有占位卡 100×100 mm 的"宽度"是 10 cm，**低于官方 12 cm 宽度的建议值**，但已在 iQOO Z9 Turbo 真机进入 TRACKED -- NORMAL（见 AGENTS.md 2026-09-18 记录）——这与官方说法不矛盾：官方的 12 cm 是"良好 AR 体验"的经验建议，实际可用尺寸取决于目标评级（星级越高、近距离使用，可越小）。换成更小的竖矩形后更应把星级做高、并控制使用距离。

## 3. 星级（0~5 星）评级标准

【官方原文 · TargetManager · Target Rating 一节】"Ratings range from 0 to 5 stars. The higher the rating, the stronger the expected performance of the Image Target."
"5 stars - strong - easily detected ... 0 stars - cannot be tracked."
（1~2 星通常可检测但跟踪较弱；官方明确 **0 星目标无法跟踪使用**。）

【官方原文 · BestPractices · Rating 一节】评级考察的维度（近逐句）：
- "The number of **sharply defined, chiseled features** in an image. Poorly lit, organic, soft, or blurred shapes affect the rating negatively."（特征数量：尖锐、边界分明的特征；模糊、有机圆润的形状扣分）
- "The **even distribution** of the extracted features across the target image."（特征分布均匀性）
- "The **contrast** (local and global) of the extracted features."（局部与全局对比度）
- "The **independence** of the local features ... no repetitive patterns."（局部特征独立性，不能是重复花纹）
- "**Cropping** the image to remove featureless areas may improve the overall rating."（裁掉无特征区域可提分）
- "The features should not concentrate on the **edges** of the target ... the 8% margin around the target is considered a **feature-exclusion buffer**."（特征不应依赖边缘；目标外围约 8% 是"特征排除缓冲区"，靠边缘的特征不算数）

【官方原文 · BestPractices · 评级使用建议】"The overall goal is to aim for a **4 or 5 star** rating for the best performance."；"A zero-star target **cannot be used** for detection and tracking. Use Image Targets with one to two stars, **only if** the overall scene setup allows for it and expect sub-optimal tracking results."

【推断 · 对本项目】两卡（小窝卡/食物卡）都应做到 **4 星以上**（评级越高的目标在更小尺寸、更近距离下越稳）。同时本项目的"两两交叉干扰评分"（一张卡出现在镜头里不误识别成另一张）不是 Target Manager 单卡星级能反映的，属于项目自设的验收项，需真机实测（这也与 AGENTS.md 中"仍需星级 ≥3 与两两交叉干扰评分验证"一致；官方口径是争取 4~5 星，项目自设的下限 3 星更保守）。

## 4. 图案设计纪律

【官方原文 · BestPractices】（逐条近原文）
- "Ideally, an image target should have a **significant amount of detail, contrast, and sharp edges** that are evenly distributed throughout the entire image."（细节丰富、对比强、边缘锐利、全图均匀分布）
- "Avoid **repetitive patterns** ... these affect the tracking negatively."（避免重复图案）
- "Avoid images with large **featureless areas** ... may contain a limited amount of featureless areas."（避免大面积无特征/均匀色块）
- "Avoid **organic, soft, or blurred** shapes such as clouds, grass, or smooth gradients."（避免柔和渐变、有机模糊形状——对本项目"圆润萌宠"是个真实风险，需用清晰的描边、硬边背景纹理补足）
- "Poorly lit or low contrast images are **not recommended**."
- "Cropping the image to remove featureless areas **may improve** the overall rating."

【官方原文 · 彩色问题 · Overview】"The image is tracked by comparing extracted natural features from the camera image against a known target resource database."
【官方原文 · BestPractices · Rating】"Vuforia Engine uses the **grayscale version** of your target image to identify features. Color **does not** affect the rating."
—— 结论：**彩色本身不加分也不扣分，引擎内部转灰度，起作用的是灰度后的明暗对比与特征**。红/蓝等在灰度下亮度接近的配色要避免（否则对比度塌掉）。【推断】

【官方原文 · 边缘细节】特征不应集中在边缘（见第 3 节引用的 8% feature-exclusion buffer）。设计上等价于：**距卡边约 8% 以内的花纹对评级无贡献**，重要细节要么内收，要么接受它不被计入。【官方原文为"8% 边缘为特征排除缓冲区"，具体换算成 mm 属推断】

## 5. 打印要求

【官方原文 · Physical】
- "An Image Target should be **flat, rigid**, and made of **matte materials**. A foam board or thick paper are examples of good substrates."
- "Avoid **glossy materials**, as they can cause specular reflections and affect the tracking performance."
- "Avoid warped targets. You should be able to keep the target's physical plane as flat as possible while tracking it."
- [Overview] "Glossiness and reflections degrade tracking performance."
- [BestPractices] 建议打印目标保持在 **moderately bright and evenly lit diffused lighting**（适中、均匀的漫射光照）下使用。

【推断 · 落到本项目】识别卡用 **200~220 g/m² 以上的厚卡纸、哑光、不覆亮膜**（可覆哑膜），打印后避免卷曲；桌面场景注意不要让台灯直射在卡面产生高光。

## 6. 推荐卡面规格（含推断与理由）

候选对比（印刷面积 = 特征承载上限的第一因素，同等级图案下面积越大特征越多）：

| 规格 | 尺寸 (mm) | 面积 (mm²) | 300 DPI 像素 | 与官方 12cm 宽度建议的差距 |
|---|---|---|---|---|
| 扑克 63×88 | 63×88 | 5 544 | 744×1039 | 长短边都 <12 cm |
| 桥牌 57×89 | 57×89 | 5 073 | 673×1051 | 最小，最不利 |
| **塔罗 70×120** | 70×120 | 8 400 | 827×1417 | 长边恰为 12 cm |

**推荐：塔罗牌规格 70×120 mm，竖向；300 DPI 出图为 827×1417 px（含 3 mm 出血则为 883×1472 px，成品仍裁到 70×120）。**

理由：
1. **贴近真实卡牌**：70×120 mm 是 TCG/LCG（塔罗、桌游配件）的通行规格，观感上就是"一张实体卡"；63×88 扑克也可，但特征承载面积少约 34%。
2. **特征数量**：评级第一因素是特征数量与分布，面积是上限。70×120 在三候选中面积最大（8400 vs 5544 mm²），同样画满细节时更容易冲上官方建议的 4~5 星。
3. **识别距离**：按官方 10 倍经验法则，70 mm 宽边 ≈ 0.7 m、120 mm 长边 ≈ 1.2 m 理论可识别距离；本项目手机俯视桌面实际使用距离约 0.25~0.40 m，留有 2~4 倍裕量，配合 4 星以上目标足够。
4. **官方最小建议的补偿**：三候选的短边都低于官方"至少 12 cm 宽"的建议（该建议针对一般 AR 体验的中远距离）。补偿手段【推断】：a) 把两卡都做到 4~5 星；b) 使用距离控制在 0.5 m 内；c) 保持 Unity 中 `mWidth=0.070`、`mHeight=0.120` 与印刷严格一致；d) 真机验证若识别不稳，退路是按 1.2 倍打印（84×144 mm）重测——物理布局不变，只改 Target Manager/Unity 中的数值。
5. **配套工程数值**：`ZQXS` 数据库两目标尺寸从 0.1 m 正方形改为 0.070 m × 0.120 m；`mWidth` 对应卡的横向短边（场景 X 轴），`mHeight` 对应纵向长边（第 1 节官方依据）。MaxSimultaneousImageTargets=2 维持不变。
6. **卡面设计纪律**（把第 3、4 节官方要求换算到 70×120 卡上，【推断】）：主体插画明暗对比拉开（彩色在灰度下要有明显明度差）；避免大面积同亮度纯色底、重复花纹、柔焦渐变；关键高对比细节放在距卡边 ≥6~10 mm（约 8% 缓冲）以内区域；卡名、数字等高对比元素本身就是好特征。四张肤色卡的姿态/表情/构图差异化设计正好满足"局部特征独立性"要求，但仍须做两两交叉干扰真机评分。

## 结论速查

| 问题 | 结论 | 依据 |
|---|---|---|
| 非正方形支持 | 支持，任意比例/非矩形均可，宽高须与图像实际比例一致 | 官方 TargetManager/BestPractices/DeviceDB |
| Width 定义 | 目标平面内 x 轴方向（左边界到右边界）的距离；Height 为竖直边 | 官方 DeviceDB 原文 |
| 物理尺寸 | 官方建议宽度至少 5 in/12 cm、"reasonable height"；无上限；尺寸须与印刷一致（尺度正确性） | 官方 Physical/TargetManager/DeviceDB |
| 尺寸↔距离 | 约 10 倍经验法则（20~30 cm 宽 → 2~3 m） | 官方 Physical/BestPractices |
| 星级 | 0~5 星；0 星不可用；争取 4~5 星；考察特征数量/分布/对比度/独立性 | 官方 TargetManager/BestPractices |
| 设计纪律 | 细节丰富、均匀分布、强对比、锐利边缘；避免重复图案、大面积无特征区、柔和渐变；8% 边缘为特征排除缓冲 | 官方 BestPractices |
| 彩色 | 无影响，内部转灰度，只看灰度对比 | 官方 BestPractices |
| 打印 | 平整、硬挺、哑光；忌光泽/镜面反射；忌翘曲；均匀漫射光 | 官方 Physical/Overview |
| 推荐规格 | 塔罗 70×120 mm 竖卡，300 DPI 827×1417 px；数据库尺寸 0.070×0.120 m | 本报告推断（依据上述官方条款） |
