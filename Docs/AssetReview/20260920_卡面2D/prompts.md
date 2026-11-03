# 塔罗奶蛙 · 卡面出图提示词（v6 · gpt-image-2.5 版）

> **2026-09-21收口**：四款定稿与Vuforia上传JPG已经存在，现役文件/尺寸/评级/接入状态见 [四色资产映射](../../四色奶蛙资产映射.md)。本文保留提示词迭代历史与复现参考，下面“再出/待修改”的历史步骤不作为当前待办。新规格用于四张角色目标，旧“Unity中两个ImageTarget”不代表最终上限。

目标：出 **4 张竖版塔罗风识别卡**（无文字、全彩），交回本项目脚本排版与识别自检。

- 出图放 `raw/`（命名 `calm_01.png` 等），挑中的放 `picked/`，每版记一行到 `raw/生成记录.md`
- **提示词主稿在 `deliver/` 四个目录**：每目录 = 塔罗原稿参考图 + 奶蛙立绘参考图 + 该卡提示词（英文主稿 + 中文对照 + 迭代片段）
- 参考图来源：塔罗 `20260920_塔罗卡面参考/`（RWS 1909 公版，用户 2026-09-20 筛选定稿）；立绘 `20260919_卡面基准形象/out/role_*.png`（1100×1650，自家 3D 渲染）

> ## 版本历史
> - v1 初版 → v2 加逐卡明度 → v3 修"灰度"字样导致黑白输出 → v4 融合塔罗主题 → v5 按用户筛选锁定四张塔罗原稿并逐卡写入审查风险。
> - **v6（当前）**：改用 **gpt-image-2.5**（`gpt-image-2.5-flare` / `gpt-image-2.5-sunburst`）出图后重写。依据 OpenAI 官方 Image prompting 指南（developers.openai.com/api/docs/guides/image-prompting，2026-09-08 随 GPT Image 2.5 更新）：
>   ① **参考图按编号指派角色**，逐图说明"只取什么、不得影响什么"（v5 只有一句"都上传"）；
>   ② **先命名交付物与用途**（AR 跟踪卡、灰度识别、哑光打印），给模型成功标准；
>   ③ **改动与保持分开写**、**一回合只改一处**（v6 每个交付文件附"迭代片段"）；
>   ④ **设置项不放提示词**：model/quality/size/background 全部走参数；
>   ⑤ 新增 **定稿卡→其余三张的 Image 3 风格参考**工作流（官方明确指出跨次生成的角色/风格会漂移，用参考图锁定而非重复文字描述；原定 calm 先行，v6.3 起锚点为 classic）；
>   ⑥ 提示词改为**英文主稿**（模型对英文指令最稳），中文仅作审阅对照；v5 的完整中文提示词并入各交付文件存档。
> - v6 发布时不改变任何设计参数（四卡映射、背景四档明度、底纹改造、无文字、8% 边带），QC 指标（灰度差 ≥80、两两 MAD ≥60）不变。
> - **v6.3（2026-09-20）**：**classic 成品经用户确认，定为全套风格锚点**（微笑、大黑瞳、无脖一体轮廓、米色手脚、边框、底纹、符号全部达标）——工作流由"calm 先行"改为 **classic 定稿 → `picked/classic.png` → calm / rage / divine 各喂 3 张参考图（含 Image 3）**；三份提示词已改，classic 自身撤下 Image 3 只喂 2 张。**定稿文件已归档（2026-09-20）**：源图 1049×1499 px（比例 0.6998，与 66×94 mm 仅差 0.4%），已复制到 `picked/classic.png`（供其余三张作 Image 3）、`raw/classic_定稿.png` 与 `deliver/04_classic_暖黄_经典_星币侍从/标准卡面_classic定稿.png`；打印时裁到 780×1110 即可（等效约 404 DPI），**无需按新尺寸重出**。
> - **v6.6（2026-09-21 晚，rage 专属修订）**：第二稿（`raw/rage_02.png`，1049×1499，审查记录 `deliver/02_rage_朱红_愤怒_权杖骑士/findings_rage_02_子代理审查.md`）经用户确认**除嘴型外全部达标**——v6.4 三项修复（盔甲全套贴合圆身、栗红棕活战马无木马残留、威武基调）全部落实，角色锁定、风格、边框、零文字、明暗、构图、锚点一致性全部通过，尺寸比例 0.6998 与 66×94 mm 差 0.33% 无需重出。**唯一硬伤：嘴仍是闭合短弧微笑**。用户拍板"这是一个愤怒形态，嘴要改成怒吼"，**同时关闭 v6.4 留的"反差萌待拍板"开放项**。v6.6 只改 rage 一份主稿、只改嘴/表情：新增独立 Mouth 段——大张怒吼嘴（横向大椭圆张口、约占面宽 1/3～1/2、双眼下方居中偏低）、**暗红褐纯色平涂口腔** + 粗黑硬边、**嘴角水平或略下扣绝不上扬**、上沿**一排小而均匀的白色圆角三角牙（只画上排）**、可选浅粉红小圆舌；眼睛圆睁直视**不得挤成大笑的开心眯弧**、可选倒八字怒眉；否定句"never a smile / gentle upturned arc / closed short line / calm or happy expression"；微笑句从角色锁定段**移出**（表情单独成段，避免与"冲突以图 2 为准"互相拉扯）；References 段声明"嘴是相对图 2 的唯一刻意例外"。**设计依据来自用户提议参考的角色表情包实证**（一次性子代理逐图审查，落盘 `deliver/02_rage_朱红_愤怒_权杖骑士/findings_表情参考_奶蛙表情包.md`：该角色张嘴帧为"横椭圆口 + 暗红褐口腔 + 上排小圆角三角牙"，且张嘴默认绑定"开心眯眼 + 上扬嘴角"——故怒吼必须同时否定上扬嘴角与眯弧眼；meme_rage 的倒八字怒眉是角色唯一的现成愤怒信号）。顺带把两条已有规格写硬防回退（胸甲只护上胸、奶油白大肚皮必须完整露出；盔顶火焰形羽饰清晰可见）。**其余三卡表情不变**（calm / divine / classic 仍是恬静微笑，v6.2 决策）。
> - **v6.5（2026-09-21，divine 专属修订）**：用户提出"紫色需要穿上教皇的类似服饰，这样更能显示出威严"。divine 原先只有三重冠、三重十字杖、双柱、交叉钥匙、台阶这些**符号**，角色身上没有任何教皇服饰，"教皇感"全靠道具撑。v6.5 只改 divine 一份主稿：新增独立 Vestments 段——奶白长袍（金边领口、前襟敞开露出奶油白大肚皮）+ 深紫圆罩袍（覆肩背、胸前圆金扣、金滚边）+ 三十字胸前垂带（呼应三重十字杖），下摆宽不过圆腹、停在脚上（短腿与三趾宽脚可见）、连指手裸露不戴手套，每道褶裥随奶蛙圆身走（不要人形肩/领口/棱角衣纹）；三重冠加"冠坐头顶、脸和两只纯白椭圆眼完全露出"约束；姿态段加"solemn and majestic: upright, frontal, perfectly symmetrical, utterly still"回应用户的威严诉求；新增防漂浮硬约束（袍服贴身带接触阴影、不得漂浮脱开、不得遮脸/白椭圆眼/肚皮/连指手/三趾脚）；明暗段补"牙白长袍与金饰各独立一档，避免人物糊成一团亮块"；References 段改为"袍服形制可取，颜色按本卡（牙白 + 金 + 深紫）重画，不得照抄原稿橙红配色"。顺带修两个与 rage v6.4 同源的副作用：Subject 段"无任何衣物或配饰"改为"唯一穿着就是下述教皇袍服与三重冠"；嘴型由 line 改为 clearly upturned arc。**其余三卡不动**；divine 表情仍是纯白椭圆无瞳眼 + 恬静微笑（神格设定，不变）。
> - **v6.4（2026-09-21，rage 专属修订）**：愤怒卡首稿（`raw/rage_01.png`，1049×1500）经一次性子代理逐图审查（`deliver/02_rage_朱红_愤怒_权杖骑士/findings_rage_01_子代理审查.md`）判定**不可采用**，用户三不满全部成立——① **盔甲整套缺失**（塔罗骑士的头盔/胸甲/肩甲/护臂/披风一件都没穿到奶蛙身上）；② **马被画成木制摇摇马**（浅棕木身 + 弧形摇椅底座，呆滞无动势）；③ **基调不威武**（端坐无前倾、无披风羽饰、无尘土）。v6.4 只改 rage 一份主稿：新增独立 Armor 段（开面头盔 + 火焰羽饰、锁链领、圆胸甲、圆肩甲、护臂、火焰纹短披风、踝甲，每片甲贴合奶蛙圆身、不遮肚皮/手脚/脸）+ Hard constraints 防盔甲漂浮；马改为"活战马（栗红棕、明显暗于蛙身、肌肉隆起、鬃尾飘起）"并硬禁"木马/摇摇马：无木纹、无平板身、无弧形摇椅底座"；补前倾、披风羽饰后扬、蹄下尘土、左下至右上斜向动势线造威武气势；顺带修两个副作用——嘴型从 line 改为 clearly upturned arc（防平直线）、Subject 段"无任何衣物配饰"改为"唯一穿着就是盔甲"。**其余三卡不动**；rage 表情按 v6.2 保留恬静微笑（反差萌），是否改怒意表情留待用户拍板。
> - **v6.1（2026-09-20 晚，主稿就地加强，版本号不变）**：针对首次出图暴露的"**角色被画成普通青蛙**"问题（gpt 把"frog"当通用物种先验，立绘没被当成权威参照）——
>   ① 四份提示词的 Subject 段全部改为"**角色是定死的设计，严格照图 2 复刻**"：钟形/梨形一体轮廓（无脖）、巨大圆腹是最宽处、浅米色连指手 + 三趾宽脚（无蹼）、头顶 dome 大眼（淡黄绿虹膜 + 极大近黑瞳）、哑光黏土质感；并显式禁止写实/通用青蛙特征（湿反光、疣粒、蹼足、长腿、侧凸眼）；
>   ② References 段改为"**图 2 是本人渲染，冲突时以图 2 为准，绝不替换成普通青蛙**"；
>   ③ 迭代片段新增"角色漂移回普通蛙"专用句；排查表补两条（普通蛙化 / 表情被改成微笑，后者已被 v6.2 改写）；
>   ④ 依据：一次性子代理对四张立绘的逐张核实记录 `deliver/findings_角色形象核实.md`（角色特征以该文件与图 2 为准，文字仅为识图辅助）。
> - **v6.2（2026-09-20 晚）**：两项用户决策回写——
>   ① **尺寸改为 66×94 mm**：用户已选 **70×99 mm 内径卡片保护套**，成品 **780×1110 px @300 DPI**（替代 70×120 mm / 827×1417，套内间隙左右约 2 mm、上下约 2.5 mm、**不做出血**）；生成尺寸 **784×1120**（打稿）/ **800×1136**（定稿，留裁切余量）；Vuforia 端 ImageTarget 尺寸待同步改 **0.066×0.094 m**（Unity 中两个 ImageTarget 的宽高）。
>   ② **微笑与黑瞳保留**：classic 首张成品显示"微笑丢失、黑瞳变小"——口型统一改为**恬静微笑**（短弧线、嘴角微扬、完全闭合、不夸张咧嘴也不画平直线），黑瞳保持极大占比；该项以用户决策/梗图印象为准，与 3D 立绘的平直线嘴有意不同，其余角色锁定特征不变。

---

## 1. v6 相对 v5 的实质变化（为什么会更好）

| 维度 | v5（即梦/diffusion 思路） | v6（gpt-image-2.5 思路） |
|---|---|---|
| 提示词结构 | 五条"串"拼接，【】括号权重式 | 制作简报式：交付物与用途 → 风格 → 主体 → 姿态 → 符号 → 构图 → 边框 → 明暗 → 文字禁令 → 参考图角色 |
| 参考图 | "两张都上传" | 逐图编号派活：Image 1 只借塔罗姿态构图、Image 2 是**角色锁定源**（轮廓/比例/配色/眼睛/手脚严格复刻，冲突以图为准）、Image 3（classic 定稿）只借风格；各附"不得照抄什么" |
| 角色保真 | 文字描述（"卡通青蛙"）——被模型当通用物种先验，画出普通青蛙 | Subject 段写明"不是普通青蛙、不是写实青蛙，禁止改良"，逐特征（无脖一体轮廓、圆腹最宽、米色连指手、三趾脚无蹼、dome 大眼、恬静微笑嘴 + 极大黑瞳、哑光黏土）写死，并声明文字与图 2 冲突时图 2 赢；微笑与黑瞳为 v6.2 用户决策，优先于 3D 立绘的平直线嘴 |
| 全套一致性 | 提示词里重复描述，靠运气 | calm 先定稿，成品作为风格参考传给其余三张（官方建议的防漂移做法） |
| 修改方式 | 整段重贴 | 一回合只改一处 + "保持清单"（`Only change X. Keep ... exactly as they are.`） |
| 明暗 | 百分比写进提示词 | 保留百分比（本卡是规格驱动资产，官方指南也允许 spec 式写法），但渲染参数全部移到 API 参数 |
| 文字 | 加 Avoid 栏 | 提示词内明确"零文字"禁令（该模型文字渲染很强，必须显式禁止）；API 无独立负向栏 |

## 2. 规格与生成参数

| 项 | 值 |
|---|---|
| 成品尺寸 | **780×1110 px**（66×94 mm @300 DPI，适配 **70×99 mm 内径卡片保护套**，左右各约 2 mm、上下约 2.5 mm 间隙、不做出血；Vuforia ImageTarget 目标尺寸 **0.066×0.094 m**） |
| 生成尺寸 | **784×1120**（打稿，约 66.4×94.2 mm）/ **800×1136**（定稿，约 67.7×96.2 mm，留裁切余量后裁到 780×1110） |
| 质量 | 打稿 `medium`；定稿 `high`，不够再 `xhigh`（仅 2.5 型号有 `xhigh`/`max`） |
| 模型 | 打稿 `gpt-image-2.5-flare`；定稿 `gpt-image-2.5-sunburst`（精度优先） |
| background | `opaque`（要满幅卡面，不要透明） |
| Vuforia 上传版 | 定稿 PNG 常 3 MB 上下，**超 Target Manager 单图 2 MB 上限**——用 `tools/make_vuforia_image.py` 转 **780×1110 JPEG**（居中裁到卡片比例 + 逐级降质至 <2 MB；classic 实转 0.494 MB / q92，PSNR 36.45 dB）；打印仍用 PNG 母版 |
| 入口 | API：`images.edit`，`image[]` 传参考图（2 张或 3 张）;ChatGPT：消息里附图 + 粘提示词 |
| 已知限制 | 复杂提示最长约 2 分钟；>2560×1440 官方标注实验性；文字位置/密集小字仍可能出错——本项目索性零文字 |

## 3. 四卡对应表与参考图角色（2026-09-20 用户定稿）

| 卡 | 肤色 | 塔罗牌 | Image 1 只借 | Image 2 只借 | Image 3 |
|---|---|---|---|---|---|
| calm 平静 | 湖蓝 | 倒吊人 XII | 倒挂姿态、竖构图、光轮垂藤 | 角色严格复刻（冲突以图 2 为准，不得画成普通青蛙）；姿势不借 | classic 定稿成品（`picked/classic.png`） |
| rage 愤怒 | 朱红/暖橙 | 权杖骑士 | 全副武装骑士主题（盔甲按奶蛙圆身重画）、活马动势、高举冲锋姿态、单主体构图 | 同上 | classic 定稿成品（`picked/classic.png`） |
| divine 神格 | 紫 | 教皇 V | 教皇袍服形制（长袍/罩袍/垂带，按本卡配色重画）、祝福手势、正面端立、冠-柱-钥匙 | 同上 | classic 定稿成品（`picked/classic.png`） |
| classic 经典 | 暖黄 | 星币侍从 | 居中挺立、双手举币 | 同上（角色严格复刻；立绘站姿刻意不借，改为举币） | —（**本卡即锚点**，2026-09-20 定稿） |

四张卡的背景明度分四档（亮 78–86 / 中深 32–39 / 近黑 8–14 / 深灰 24–31），灰度下两两可区分；与塔罗原稿底色相反是刻意设计。

## 4. 出图工作流

1. **风格锚点已定（2026-09-20）**：**classic 卡成品经用户确认**（微笑、大黑瞳、无脖一体轮廓、米色手脚、边框与底纹全部达标），复制到 `picked/classic.png`，作为全套风格标准。
2. **其余三张**：calm / rage / divine 各喂 **3 张参考图**（塔罗原稿 + 立绘 + `picked/classic.png`）；打稿 flare + medium + 784×1120，迭代用各卡提示词文件里的片段（一改一回合）。Image 3 只锁风格、边框、纹理密度与渲染方式，本卡的配色/姿态/符号/背景按各自主稿。**rage 已升到 v6.4 + v6.6**（v6.4：盔甲上身 + 活战马 + 威武基调；v6.6：嘴改大张怒吼嘴——第二稿除嘴外已全部达标，第三稿只差嘴）、**divine 已升到 v6.5**（教皇袍服上身 + 威严气场写进主稿），直接按新版出图，不要再沿用 v6.2 主稿。
3. **各自定稿**：sunburst + high + 800×1136（裁到成品 780×1110 px = 66×94 mm @300 DPI）。
4. **classic 自身**：若现有成品是旧 70×120 比例，用 classic 提示词（已是新尺寸）按 784×1120 / 800×1136 重出一版即可，风格不变。
5. **Vuforia 上传版（必做）**：定稿 PNG 通常 3 MB 上下，超过 Target Manager 单图 2 MB 上限——用 `tools/make_vuforia_image.py` 转 780×1110 JPEG（classic 转出 0.494 MB / q92，PSNR 36.45 dB）：`python tools/make_vuforia_image.py ../picked/<卡名>.png -o ../picked/<卡名>_vuforia.jpg`。打印仍用 PNG 母版。
6. **交付回本项目**：命名存 `../raw/`，在 `../raw/生成记录.md` 登记（提示词版本填 v6，记录模型/size/quality 与喂图清单）→ 后处理：裁到 780×1110（66×94 mm，配套 70×99 mm 内径保护套，左右各约 2 mm、上下约 2.5 mm 间隙）→ 排中文标题与编号（倒吊人 XII / 教皇 V；权杖骑士、星币侍从无编号留空；**生成图本身零文字，标题带与顶部牌位为空是设计，文字由本步排版**）→ 成品/灰度两版（66×94 mm 无出血，不必再出出血版）→ 量化自检（角色-背景灰度差 ≥80、两两 MAD ≥60、边缘密度 ≥12）→ Target Manager 星级实测（≥3，争 4–5；**classic 已实测 4 星**，压缩转档不影响评级）与两两交叉干扰真机评分；通过后把 Unity 两个 ImageTarget 尺寸改 0.066×0.094 m 并重新打包。

## 5. 提示词主稿

| 卡 | 文件 |
|---|---|
| calm | `deliver/01_calm_湖蓝_平静_倒吊人XII/提示词.md` |
| rage | `deliver/02_rage_朱红_愤怒_权杖骑士/提示词.md` |
| divine | `deliver/03_divine_紫_神格_教皇V/提示词.md` |
| classic | `deliver/04_classic_暖黄_经典_星币侍从/提示词.md` |

每个文件：生成参数表 → 参考图角色分配 → **英文主稿（整段粘贴）** → 中文对照 → 该卡常用迭代片段。

## 6. 排查表（gpt-image-2.5 版）

| 症状 | 原因 | 改法 |
|---|---|---|
| **出了黑白/灰度卡面** | 明暗要求被过度执行 | 主稿已首尾强调 must be a vivid fully colored illustration；仍不行就在迭代片段里说 `Only restore full color: the skin colors must be vivid again. Change nothing else.` |
| **背景大片纯色无纹** | 该模型爱出干净平涂 | 用迭代片段：`Only increase the density of the fine background texture until no flat blank patch remains. Keep everything else exactly as it is.` |
| **画面冒出文字/罗马数字** | 塔罗原型带题名 | 主稿有零文字禁令；再犯就用：`Only remove all text ... Change nothing else.` |
| **青蛙长得不像立绘** | 跨次生成角色漂移（官方已知限制） | 迭代片段：`Only restore the frog to match Image 2 exactly ... Change nothing else.`；仍漂移就在下一轮提高 Image 2 权重或减少同时改动项 |
| **青蛙被画成普通青蛙/写实蛙**（湿反光皮肤、疣粒、蹼足、长腿、侧凸眼、绿蛙脸） | "frog"一词触发通用物种先验，图 2 的权威没被声明 | 主稿 Subject 段已写死"不是普通青蛙、不是写实青蛙"并禁止上述特征；再犯用：`Only restore the character to match Image 2 exactly - silhouette, no neck, dome eyes, tan hands and feet with three toe lobes. Do not let it become a generic or realistic frog. Change nothing else.` |
| **微笑丢了 / 黑瞳变小 / 嘴画成平直线** | v6.1 按渲染实况把嘴写成平直线；gpt 也爱把瞳孔缩小 | v6.2 起口型是恬静微笑（短弧、嘴角微扬、不咧嘴）、黑瞳极大占比；再犯用：`Only restore the frog's small gentle closed smile with slightly upturned corners, and enlarge the near-black pupils to fill most of each eye. Change nothing else.` |
| **愤怒卡盔甲没穿到奶蛙身上 / 盔甲漂浮脱开**（v6.4 首稿实测：零盔甲） | 原提示词只字未提盔甲，References 段还写着"不得照抄它的盔甲"；提了也容易画成独立浮甲 | v6.4 已新增独立 Armor 段 + Hard constraints（甲片贴身、接触阴影、不得漂浮/脱开、不得放在蛙外）；再犯用：`Only refit all the armor onto the frog's body so every plate touches the body with contact shading - no loose, floating or detached pieces, and no armor anywhere except on the frog. Change nothing else.` |
| **愤怒卡的马变成木马 / 摇摇马**（v6.4 首稿实测：浅棕木身 + 弧形摇椅底座） | 提示词里写的是 wooden rocking horse，模型照做且"玩具"语义冲掉愤怒主题 | v6.4 已改为活战马并硬禁木马特征；再犯用：`Only replace the horse with a living muscular warhorse - powerful chest and hind legs, flowing mane and tail, alive eyes - and remove every wooden-toy feature: no wood grain, no plank body, no curved rocker base. Change nothing else.` |
| **愤怒卡气势不足、不威武** | 木马玩具感 + 无前倾 + 无飘动元素，动势线撑不起冲锋 | v6.4 已补前倾、披风羽饰后扬、蹄下尘土、左下至右上斜向动势线；再犯用：`Only strengthen the charge: lean the frog's body further forward, stream the cape and plume backward, and add dust clouds at the horse's hooves. Change nothing else.` |
| **愤怒卡嘴还是微笑 / 不够怒**（v6.4 两稿实测：嘴始终是闭合短弧微笑） | 角色表情包里"张嘴"默认绑定开心眯眼 + 上扬嘴角（大笑语义），模型会顺手画成大笑；只否微笑不禁眯弧眼会被回退成开心表情 | v6.6 已把嘴独立成段并按表情包实证写具体：暗红褐平涂口腔 + 嘴角水平/略下扣绝不上扬 + 上排小圆角三角牙，同时否定"微笑弧 / 闭合线 / 开心眯弧眼"；再犯用：`Only restore the frog's wide open roaring mouth - a large horizontally stretched oval opening on the lower face filled with solid dark maroon, corners pulled wide apart and held level or slightly downturned (NOT upturned), a row of small uniform rounded white teeth along the top inside edge, and the eyes wide open staring straight ahead (no happy squint arcs). Do not return it to a smile, a gentle arc or a closed line. Change nothing else.` |
| **神格卡没有教皇袍服 / 袍服漂浮脱开**（v6.5 起才写入袍服） | 原主稿只有冠/杖/柱/钥匙符号，角色光身，教皇感撑不起来 | v6.5 已新增 Vestments 段（奶白长袍 + 深紫罩袍 + 三十字垂带）+ 防漂浮硬约束；再犯用：`Only refit all the vestments onto the frog's body so every fold touches the body with contact shading - no floating or detached cloth, and no vestment anywhere except on the frog. Change nothing else.` |
| **神格卡袍服遮住肚皮/手脚/白椭圆眼** | 袍服按人形衣纹画，容易盖住锁定特征 | v6.5 已写"下摆宽不过圆腹、停在脚上、连指手裸露、前襟敞开露肚皮、冠不遮脸"；再犯用：`Only keep the frog's locked features visible through the vestments: the big cream belly patch, both white oval eyes, the mitten hands and the broad three-toed feet. Change nothing else.` |
| **画得太"手绘/发光/渐变"** | 默认审美倾向 | `Only flatten the rendering: hard edges, flat color fields, no shading, no glow, no gradients. Keep everything else exactly as it is.` |
| **两张参考图混血（蛙长出了塔罗的袍子/马）** | 参考图角色没派活 | 确认主稿 References 段每图的"只取/不得"都在；必要时把 Image 1 的描述再收紧 |
| **三张卡风格不一致** | 没传 calm 定稿 | 确认 rage/divine/classic 都传了 Image 3；用 `Only match the style to Image 3 exactly ...` 迭代 |
| **倒吊人出现绳索/绳套** | 原稿有系绳 | 主稿已禁；再犯用 `Only change the hanging to one foot hooked over the branch, no rope anywhere. Change nothing else.` |
| **教皇款灰白柱子** | 照抄原稿 | `Only darken the pillars and add dense fine carving texture ...` |
| **星币侍从变回明黄底** | 照抄原稿 | `Only replace the bright yellow background with a dark grey ground ...` |
| **边框太粗挤小画面** | 双层塔罗框被放大 | 主稿限 3%；再犯用 `Only thin the frame to under 3% of the card width ...` |

## 7. 注意事项

- 官方指南来源：OpenAI《Image prompting》（GPT Image 2/2.5）developers.openai.com/api/docs/guides/image-prompting；拆解参见 PixVerse 2026-09-10 评测文。核心八条：命名交付物与用途；描述可见内容；参考图编号派角色；确切文字加引号限制；改动与保持分开；一回合一改；设置项走参数；迭代时回传上版并重复保持清单。
- v5（即梦版）的完整中文提示词没有丢失：已并入 `deliver/*/提示词.md` 的「中文对照」存档；换回其他平台时按那段拼装即可。
- 四张塔罗原稿均为 RWS 1909 公版（Pamela Colman Smith），可自由改编打印；不要引入托特等有版权牌组；奶蛙立绘为本项目自有资产。
- **卡片尺寸以保护套为准**：成品 66×94 mm（780×1110 px @300 DPI），装用户选的 70×99 mm 内径卡套，左右各约 2 mm、上下约 2.5 mm 间隙——因此**不做出血版**，Vuforia 目标尺寸按 0.066×0.094 m 录入。
- **Vuforia 上传必须压到 2 MB 以内**：满幅细纹的卡面 PNG 普遍 3 MB 上下，Target Manager 会拒收；用 `tools/make_vuforia_image.py` 转 JPEG（脚本在 `20260920_卡面2D/tools/`，四张卡通用），打印母版保持 PNG 不动。
- **生成图上下文字区为空是设计**：画面零文字（渲染质量与 Vuforia 特征考虑），中文标题与编号由本项目脚本在成品上排版；若要让文字进画面，需先把"零文字"决策改掉。
- 先在 calm 上定风格再出其余三张；肤色与塔罗对应关系待用户回写《交互与皮肤扩展设计》时确认，卡面制作按本表执行。
