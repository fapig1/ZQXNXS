# AGENTS.md

## 项目与事实入口

增强现实课程设计“基于图像跟踪与行为状态驱动的移动 AR 萌宠互动系统设计与实现”，产品暂名“桌上有龙”。1 人开发，Android 手机和平板；首测 iQOO Z9 Turbo / Android 15。奶蛙显示在摄像头观察的真实桌面中。

**更新日期：2026-10-27。用户最新决定优先；设计目标与源码现状必须分开。**

- 当前产品范围：[交互与皮肤扩展设计](Docs/交互与皮肤扩展设计.md) v0.3。
- 资产、卡面、稳定 ID、哈希与接入缺口：[四色奶蛙资产映射](Docs/四色奶蛙资产映射.md)。本表是四色交接的唯一现役清单。
- 架构现状与迁移：[项目架构与技术架构](Docs/项目架构与技术架构.md)；详细 plan 为 [四色改版设计 v1](designs-oo/202609211530_四色奶蛙互动改版/design_v1.md)，动作选型已由用户定稿为四色候选A（2026-09-22，见[专属动作与阈值参数建议 v0.1](designs-oo/202609211530_四色奶蛙互动改版/专属动作与阈值参数建议_v0.1.md)），差异化数值参数已于 2026-10-27 由用户定稿（同稿 §3，已进入 `PetBehaviorConfig.asset`），独立代理审查未完成。
- 工程使用：[ARPet/README.md](ARPet/README.md)、[ARPet/SETUP.md](ARPet/SETUP.md)；冻结与解冻：[奶蛙最终版资产说明](Docs/奶蛙最终版资产说明.md) 第 7 节。
- 不阻塞当前阶段的遗留事项（四宠APK/真机、诊断脚本去留等）：[后续修改问题清单](Docs/后续修改问题清单.md)。
- 文档分类、事实来源与同步清单：[文档导航与同步规则](Docs/README.md)；本次清查结论见 [2026-09-21 文档同步清查](Docs/文档同步清查_20260921.md)。
- 旧开发流水与故障证据保留于 [项目开发历史](项目开发历史_截至20260921.md)，不据其旧待办覆盖当前范围。原课件、白皮书、已提交开题材料保留为历史。

## 已确定的新版范围

1. 四色奶蛙：`classic` 暖黄、`calm` 湖蓝、`rage` 朱红、`divine` 紫。每只拥有独立状态，角色卡映射对应实例。
2. 开心值与好感度**合并为一个值**，统一叫开心值；单击、双击等触屏交互提高对应奶蛙的开心值。
3. 每款在 **100% 开心值**触发一个专属动作；动作选型已定候选A：classic_proud / calm_zen / rage_roar / divine_awaken（2026-09-22）。共享大笑按用户要求夸张化制作，身体后弯至90°可接受，须逐段FBX回读验证不破面。加分、满值后回落、重新武装、冷却与时长已由用户 2026-10-27 定稿（四色数值见建议稿 §3，已进入 `PetBehaviorConfig.asset`），可用作报告口径。
4. 同屏不同奶蛙靠近后共同大笑；最终支持四色四只同屏，双宠只是第一阶段验证目标。
5. **删除食物卡玩法**。2026-10-27 已从运行代码删除投喂事件、饥饿/精力衰减、Eat 行为配置与相关测试；Eat 片段、`PetState` 的旧字段和旧存档字段仅作历史资产/迁移保留。不得再要求先完成进食真机验证才能改版。
6. 本地存档、手机/平板适配仍在范围内。追球、拍照、反应解锁未被用户取消；追球延期、收藏记录简化等属于方案建议，不擅自当成已删功能。

单机、本地存档、实体卡定位不变。不增加对战、校园跑、集市联动、联网商业系统；这些仅作未来展望（2026-10-27 开发者补充的奶蛋多形态、AR 精灵卡牌对战与收服、AR 体验馆 NPC/Boss 也只写入报告与 PPT，见 [未来展望](Docs/未来展望_AR精灵对战与体验馆.md)）。视频导出、AI对话、多人联网、手部跟踪、真实遮挡、任意物体识别和整屋漫游不属于本轮。

**语音口令（2026-10-29 已实现，真机未测）**：用户按住屏幕底部"按住说话"按钮，喊四句口令之一（安迪 / 嘎嘎爹拉筛 / 雷霆大笑 / 欧拉，开发者自录配音），**所有在场**奶蛙用同一句录音回应：安迪→打招呼 classic_proud + 眯眼微笑，嘎嘎爹拉筛→TouchReact + o 嘴，雷霆大笑→social_laugh 动作 + 眯眼大笑，欧拉→rage_roar + 怒眼怒吼。加分等同该宠一次单击（同样走满值周期，跨越满值改播专属），同一只 3 秒语音冷却，执行中拒绝不排队。同时：共同大笑开始时整组只播一次"雷霆大笑"，classic 专属播"安迪"、rage 专属播"欧拉"。识别为 sherpa-onnx v2.0.1 C API 离线关键词识别（zipformer wenetspeech 3.3M int8，`StreamingAssets/Voice/kws/`），Android `Plugins/Android/sherpa-onnx/arm64-v8a/*.so`、编辑器 `Plugins/x86_64/sherpa-onnx/*.dll`。口令、动作、表情、音效、冷却全在 `PetBehaviorConfig.asset` 的 `VoiceCommands` 等字段；改口令后用 `Tools/Voice/make_keywords.py` 重生成关键词文件。识别结果经 `Interaction/VoiceCommandRule` 变成每只在场宠物一个 `InteractionEventKind.Voice` 事件，进 `BehaviorStateMachine` 唯一结算点。

## 当前已经做到哪里

- Unity `6000.0.83f1`、Vuforia `11.4.4`、Built-in / Gamma；11 个程序集。Android 构建入口设置 IL2CPP、仅 ARM64、minSdk 29 / targetSdk 36。Active Input Handling = Both（`activeInputHandler: 2`，Vuforia 依赖 Input System）；`TouchInputBridge` 用旧 `Input` API，不得改为仅新输入系统。
- 2026-09-18 历史实测：**98/98 EditMode 通过**；APK 在 iQOO Z9 Turbo / vivo V2352A、Android15 上启动、预览相机，两个占位观察者均进入 TRACKED，Pet01 锚定显示。证据包括 `ARPet/TestResults-EditMode.xml` 与历史记录。不是新四宠回归。
- 当前业务源码已为多宠（P2 竖切，2026-10-27）：`Pet/PetRuntime` 每宠独立数值+行为机；`App/PetSession` 逐宠持 `TrackingStateMachine`，事件按 `PetId` 路由，丢失立刻不可交互并隐藏角色 Renderer/Collider（不 SetActive、不动 Observer），行为时钟冻结、恢复不重建运行时也不重置数值（开心值自然衰减已于 P3 关闭）；`AppRoot` 用 `PetBinding[] pets` 装配。食物链已退出运行（`FeedingRule`、Eat 行为配置、饥饿/精力衰减删除）；存档为 schema 2 `zqxnxs_save_v2.json`，按稳定 `PetId` 逐只保存，以 schema 2 为准；v1 未实际投入使用，加载时仅兼容读取迁移，`SaveService.Delete` 会连同 v1 遗留文件一并删除（用户 2026-10-27 确认）。`02_Main` 已重建为四宠主场景：四个平级 `Target_card_*`（ZQXS 库，宽高读 XML 0.066×0.093923m）各挂 `PetRoot_<petId>`，`VuforiaTrackingSource` 4 条 targets、`AppRoot.pets` 4 条；旧 Den/Food 与 DefaultObserverEventHandler 已删，仍是唯一构建场景。2026-09-29 的四卡独立实测场景 `04_FourCardTracking`（菜单8）与四卡 APK 保留，**真机未测**。**新 02_Main 尚未构建 APK、未真机测试。**
- P3 手势与满值闭环已完成（2026-10-27，EditMode **161/161**，`ARPet/TestResults-P3Gesture-20261027.xml`）：`Interaction/TapGestureRecognizer` 每宠独立 0.28s 双击窗口（跨宠不合成，Cancel/CancelAll/CancelUnavailable 丢弃不补发，事件 Id 每宠递增）；`TouchPokeRule` 删掉 0.45s 冷却，移动阈值 = max(40px, dpi×0.25in)；`TouchInputBridge` 按 Began 的 fingerId 跟踪单指。`BehaviorStateMachine` 仍是唯一结算点，`Pet/HappyCycle` 管满值周期（Armed/PendingAuto/PendingConfirm/Released），满值直接播正式片段；`Petted`（TouchReact 0.7s）已启用且执行中新点按拒绝、不排队。四色参数（单击/双击加分、回落、重新武装、冷却、时长）已定稿进 `PetBehaviorConfig.asset`，开心值自然衰减关闭。`PetStatusPanel` 多宠列表显示开心值百分比，选中宠有 `happinessBar`。存档仍为 schema 2，`PetSaveEntry` 增加满值周期与冷却/计数四字段，旧档缺字段读成 Armed/0。
- P4 接近组共同大笑已完成（2026-10-27，EditMode **223/223**，`ARPet/TestResults-P4Social-20261027.xml`）：`Interaction/ProximityRule` 只读位姿判进入 0.18m / 退出 0.23m（只看水平距离）、高差 0.035m、夹角 20°、停留 0.6s（"起始已贴近先锁住"已于 2026-10-30 删除，任一卡连续丢失 ≥2s 也算分开，见下条）；`Pet/SocialCoordinator` 管组划分、整组同帧开始（`PetBehaviorId.Social`=social_laugh 2.4s，不加开心值）、任一成员丢失/切后台整组取消、成员冷却 5s、分开 0.3s 后重新武装；PendingAuto 挡社交、PendingConfirm 不挡。参数在 `PetBehaviorConfig.asset` 的 `Social`。相位错开与 ±3° 随机未做（后续清单 L10）。
- P5 二维嘴眼已接线（2026-10-27，EditMode **231/231**，`ARPet/TestResults-P5Face-20261027.xml`）：不改冻结网格，Blender 脚本 `Tools/Blender/build_pet01_face_overlay.py`（只读冻结源、SHA 校验）生成 `Pet01_FaceOverlay.fbx` 两片贴片与 `face_overlay_layout.json`，图集由 `Tools/Art/make_pet01_face_atlas.py` 画（眼 2048²：squint/closed/divine/rage；嘴 512²：smile/laugh/roar/o/serene/flat）。Unity 侧 `ZQXNXS/FaceDecal` 着色器按 cos(视线夹角) 朝相机前移最多 10mm、不写深度、不投影；`FaceOverlaySetup` 把贴片挂到每只宠物 Head 骨骼下，`PetPresenter.SetExpression` 用 per-Renderer `MaterialPropertyBlock` 选格、贴片显示时把 Eyes/Oral 槽换成该色 Skin 副本钉纹素的遮盖材质。映射：专属=`PetExpression.Signature`（classic 眯眼+微笑、calm 闭眼+舒展、rage 怒眼+怒吼、divine 白眼+舒展），共同大笑=眯眼+大笑，被点=o 嘴。七轮视觉审查记录在 `ArtSource/Characters/Pet01/Logs/FaceOverlay_20261027/`，残留观感问题见后续清单 L12/L13；特效、音效未做（L11）。
- P6 代码侧整合（2026-10-27，EditMode **234/234**，`ARPet/TestResults-P6Integration-20261027.xml`）：`Platform/PerformanceRecorder`（全局一份，挂 AppRoot）每 5 秒把平均 FPS、最慢帧、慢于 30FPS 帧占比、电量追加到 `persistentDataPath/perf/perf_<启动时间>.csv`，切后台的长间隔丢弃；诊断面板右下角 `FpsText` 显示最近窗口 FPS（UI 经 Core `IFrameRateSource` 读取）。温升仍需人工读数。**真机帧率、温升与四卡同屏仍无实测。** 同日三路代码自查后修复（EditMode **236/236**，`ARPet/TestResults-BugFix-20261027.xml`）：LIMITED 跟踪改为"显示但不可交互"（`TrackingStateMachine.CanShow`）、`File.Replace` 抛 IOException 的存档兜底、触屏同帧换指与切后台残留 fingerId、社交与面板的每帧分配；详见 [项目使用与调试说明](Docs/项目使用与调试说明.md) 第 7 节。2026-10-29 13:47 加入语音后重建 APK `Builds/Android/ARPet-phone-test.apk`（83.22 MiB，SHA256 `5CB28D44DE9E85E881B2EAABF8FFC54A022C947B0A579C3775099C9EA067773D`；已核对包内含 arm64 原生库、KWS 模型与 `RECORD_AUDIO`），尚未安装上真机。
- **2026-10-30 首轮四宠真机（iQOO Z9 Turbo / Android 15）与修复**：单卡识别、单/双击、满值专属、丢失恢复、语音"安迪"、v1→v2 迁移、单卡 59 FPS 已通过；发现的 BUG-01（首次未授权麦克风语音永久停用）、BUG-02（上半身点不到）、BUG-03（Android `File.Replace` 每次存档异常 + SELinux 刷屏）、BUG-04（"启动先上锁"导致按演示脚本共同大笑永不触发）与 O-9（竖屏不够宽、只支持竖屏）已修：识别器初始化与麦克风权限/设备解耦；无碰撞体命中区改为脚底到头顶的竖直胶囊（`PetHitTarget.hitHeightMeters=0.2`）；Android 直接走备份兜底；删除启动锁并新增 `SocialTuning.LostSeparationSeconds=2`；开放竖屏 + 左右横屏，面板 `UI/OrientationAnchors` 横屏缩到左上。EditMode **268/268**（`ARPet/TestResults-RealDevice-20261030.xml`），APK 已重建（98.9 MiB，SHA256 `3A35C6CEACD27ADF3BC1679064CF3FB6E3750E01A74720E468F5C8232D05EBA2`，`screenOrientation=fullUser`）。14:47 构建真机已验证 BUG-03/04 与四卡同屏 60.4 FPS，同时暴露 BUG-05（演出中被拒的点按把招牌表情关掉）与 O-10（四卡不动时共同大笑每 5 秒重演）。15:36 构建修复：`Refuse()` 只在 Idle 换表情；LIMITED 不计丢失，丢失满 2s 后须放回时相对位置变化 ≥ `SocialTuning.RelocationDistance=0.03m` 才解锁，并加 `[ProximityRule]` 上锁/解锁日志。EditMode **273/273**（`ARPet/TestResults-RealDevice2-20261030.xml`），APK SHA256 `C115D3366DA770788FA6970A2C2BA767639900364DB3036C495042688980BFF0`。**BUG-01/02/05、O-10 与横屏仍待真机复测**；切后台存档、温升未测；O-4～O-7 未处理。详见 [真机测试问题记录](Docs/真机测试_20261030_问题记录.md) 与 [项目使用与调试说明](Docs/项目使用与调试说明.md) 第 10 节。
- 四张定稿PNG、四份Vuforia JPG与四色2048²贴图已存在；四色Unity材质及独立开发预览已生成；Prefab未建，多宠业务已接（见上条）。classic历史评级4星，其余评级待记录。
- 冻结主模型为 `ArtSource/Characters/Pet01/pet01.blend`，26骨骼；当前 `20260928_arm_separation` 为12,452顶点/24,536三角面（原冻结20,528）。Idle 已在 Unity 接线；Eat 保留为旧资产（仍在 Animator），不是新版玩法。
- `Pet01Controller` 现有状态：Idle、Eat（旧资产）、TouchReact（0.7s 单次，2026-10-27 接入）、classic_proud、calm_zen、rage_roar、divine_awaken、social_laugh。DozeLoop 4s、WalkLoop 1s 有FBX但**未接Animator**；WalkLoop穿地未过（当前报告最低−0.00083m，判据−0.0005m）。
- 动作现状以 [手臂分离修订记录](Docs/手臂分离修订记录_20260929.md) 为准，09-22 [专属接入记录](Docs/四色专属动作接入记录_20260922.md) 中“classic收势/divine张臂不通过”已被09-29重做取代：classic_proud=打招呼2.0s、divine_awaken=水平展翅2.5s，可读性各4/5；rage_roar（叉腰撑臂+后仰+抖动）曲线沿用09-22。calm_zen已于2026-10-26重做为双手合十+闭眼+三次递减点头（2.5s，可读性4/5，见[合十改版记录](Docs/calm_zen合十改版记录_20261026.md)），其FBX以 `CalmGassho/validation_report.json` 为准；其余10个Unity FBX与 `ArmSeparation/validation_report.json` 的 `exports_sha256` 一致（2026-10-26核对）。
- 已知演出缺口（中等，均未否决）：calm_zen合十掌根有缝（更像指尖相触），刚合十帧眼睛只闭到细缝；social_laugh第40帧一只手埋入肚皮、第19/56帧后颈深折痕（曲线受保护，改动须用户同意并重做验证+视觉审查）；静止手指尖轻压肚皮。右肩关节外露（球心位于躯干表面、约80°接缝）经2026-10-26专项检查判定填补不可行，用户决定先不管，作为已知外观缺口（证据 `ArtSource/Characters/Pet01/Logs/ShoulderCheck_20261026/`）。满值触发、接近组共同大笑与二维嘴眼已于 2026-10-27 接通；特效、音效未做。
- `social_laugh`：2.4s，两波后弯65°/105°，拉伸上限经用户同意放宽到2.25（峰值2.13）。逐宠丢失恢复、v2 迁移与四宠性能已代码级实现，真机验证待补；照片保存与平板实测仍待验证。

## 开发顺序

1. **文档与资产映射收口已完成**；运行源沿用冻结 Pet01 + 四色贴图。`pet01_work.blend` / `pet01_surgery.blend` 仅是卡面制作工作副本，不自动升级成运行源。
2. 新四卡数据库与2→4目标早期实测；验证实际印刷尺寸、星级、六组配对错认和四目标稳定性。
3. 泛化目标/角色身份，双宠独立实例、逐宠显示/丢失恢复、食物依赖退出与v1→v2存档迁移。**已完成（2026-10-27，EditMode 112/112；真机待测）**。
4. 单击/双击与开心值闭环，先接TouchReact；专属行为和组互动可用明确标注的开发占位验证。**已完成（2026-10-27，EditMode 161/161；真机待测）**：单/双击按宠独立识别，满值直接播四段正式片段，未走占位方案。
5. 实现接近组协调，接共享大笑；四段专属动作已随第 4 步接线，此处补嘴眼表情；完成四宠真机和UI整合。**接近组与共同大笑已完成（P4，EditMode 223/223），二维嘴眼已接线（P5，EditMode 231/231）；P6 代码侧加了帧率记录（234/234）；四宠真机待做。**
6. 补齐剩余交付、性能记录、报告、打印卡、APK及演示。详细阶段和工期估算见设计v1。

## 分层与交互约束

- `.asmdef` 强制依赖：Core不依赖其他项目程序集；AR只依赖Core；Presentation不依赖Pet，通过Core契约被驱动；App唯一装配。
- 保留七层结构，多宠模型已落地。每宠独立运行状态；相机、输入采集、存档写入全局各一份，不能复制四个AppRoot。
- 跟踪层只提供目标ID、位姿、有效性和时间，不结算开心值。使用统一米制、同一世界坐标系的新鲜有效位姿；不再有小窝局部坐标换算。
- 跟踪失效立即阻止该宠新空间交互，恢复不伪造离开/重新进入；不使用过期位置。隐藏角色子树，不关闭Vuforia Observer来隐藏角色。
- 手势绑定命中PetId、区分Canceled与Ended；UI不得穿透。单击/双击互斥、按实例去重，双击不得叠加两次单击奖励。触屏不称为现实手部接触。
- 开心值与执行中行为分离；满值触发需周期资格、冷却、重新武装与加载规则，不能每帧满值重播。数值只由行为层结算一次，时钟和Animation Event不能重复结算。
- 接近规则归Interaction，跨宠资格/预留/统一开始/取消归Pet协调器；不把业务堆进AppRoot。三四宠不能按每对分别让同一只重复笑。
- 阈值与`形象ID → 行为ID → 触发条件`进入配置。四色参数与Animator能力清单已在 `PetBehaviorConfig.asset`（`VariantTunings`、`AnimatorStates`），不得在代码里另写死一份。
- 多宠存档按稳定ID版本化（2026-10-27：以 schema 2 为准；v1 仅兼容迁移，删除存档时一并清除），不保存场景引用或临时位姿；旧黄蛙承接旧状态，其他初始化。异常存档、权限拒绝、切后台、照片失败均有明确恢复路径。

## 资产与卡片约束

- 形体、骨骼、Idle/Eat按2026-09-11用户决定冻结；加外观和新Action允许，改形体/权重须按冻结说明先归档、新开shape_revision。不运行重建脚本覆盖用户最终资产。
- 当前运行源源哈希 `a289a8db…`（2026-10-26只改Action：calm_zen合十版，脚本 `Tools/Blender/build_pet01_calm_gassho.py`，工作目录 `CalmGassho/`，上一版 `35c5625d…` 归档于 `Archive/20261026_before_calm_gassho/`）。形体为用户授权的 `20260928_arm_separation`（2026-09-29提升）：躯干不带手臂重建，双臂为独立闭合娃娃关节外壳，面部与上一版差异<1µm，静止骨架与8段旧Action曲线不变；制作脚本 `Tools/Blender/build_pet01_arm_separation.py`，工作目录 `ArtSource/Characters/Pet01/ArmSeparation/`。上一版 `9ecd9ddd…`（09-22权重修订+social_laugh）归档于 `Archive/20260928_before_arm_separation/`；更早的 `308d2060…`、`8dc3dc92…` 与原冻结 `fad23c5c…` 均在本机Archive。最多8影响，Unity须Custom/8、SkinWeights.Unlimited；旧WalkLoop穿地仍未通过。`validate_pet01.py`（原32项，≤4影响）不适用于修订后的源，且会覆盖受控的 `validation_report.json`，不要运行。
- 用户要求直接调用 `D:/blender.exe` 与Python/bpy处理资产；不使用已要求忽略的blender-asset-package skill。源放ArtSource，FBX/贴图进Unity，保留meta，不依赖Unity自动导入.blend。
- 非根骨骼平移超过1～2mm有FBX回读问题；手臂/手指与肚皮权重污染会牵拉破面。不要仅放大动作幅度解决表达问题；表情走二维贴图/面片，新Action须检查并FBX回读后接线。
- 四色源贴图和定稿卡不移动、不重复生成。卡面道具/袍服不代表有同款三维网格或动作。参考视频有音轨不等于已有可用音效。
- 当前成品 **66×94mm、300DPI约780×1110px、无出血**；保护套70×99mm。旧70×120mm、0.1m占位规格不适用于新卡。
- Vuforia使用完整上传图对应的实际印刷尺寸，约0.066×0.094m（高按实际比例核对）；不是保护套尺寸。卡面为零文字定稿，不新增名称条改变目标图。
- 彩色图可上传，SDK按灰度特征识别；每卡星级≥3、六组交叉干扰与四卡同屏真机验证。打印哑光，实测保护套反光和小卡识别距离；四目标支持必须有实测证据。
- **主对话不看图片**。需要视觉审查时，按既定工作流派一次性子代理，边看边落盘；文本/路径/哈希/图像尺寸核查无需视觉代理。

## 工具链与已知坑

- Unity：`D:/APP/Unity/6000.0.83f1/Editor/Unity.exe`；配套NDK r27c/OpenJDK。VS2022 `17.14.37628.2`、Windows SDK `10.0.26100.0`。不反复扫描旧安装或重新选型。
- 构建入口 `Tools/Unity/build-android-apk.ps1`；sdkmanager挂起用 `SDKMANAGER_OPTS=-Dsun.net.client.defaultConnectTimeout=5000 -Dsun.net.client.defaultReadTimeout=5000`，Hub重启后继承。SDK检测仍约3～4分钟；不再放shim。Gradle镜像只解决依赖下载，不解决SDK检测。
- 测试：Unity内 Window→General→Test Runner→EditMode→Run All；最新证据 `ARPet/TestResults-RealDevice2-20261030.xml`（273/273，2026-10-30 第二轮真机问题修复后）；首轮修复 `TestResults-RealDevice-20261030.xml`（268/268）；语音 `TestResults-Voice-20261029.xml`（255/255，含编辑器加载真实 sherpa-onnx 识别"雷霆大笑"）；自查修复 `TestResults-BugFix-20261027.xml`（236/236）、P6 `TestResults-P6Integration-20261027.xml`（234/234）、P5 `TestResults-P5Face-20261027.xml`（231/231）、P4 `TestResults-P4Social-20261027.xml`（223/223）、P3 `TestResults-P3Gesture-20261027.xml`（161/161）、P2 `TestResults-P2MultiPet-20261027.xml`（112/112）保留为阶段证据。批处理命令：Unity 关闭时 `Unity.exe -batchmode -projectPath <ARPet> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>`（不要加 `-quit`；退出码 2 表示有失败）。`pwsh -NoProfile -File Tools/Validation/run-rule-checks.ps1` 只是Roslyn+替身的规则/装配检查（2026-10-29 语音后：0 编译错误，248 passed / 25 failed；基线 21 条 + 4 条 `VoiceAssetsTests` 依赖真实资产与原生库，离线替身必然失败，以 Unity 结果为准），不能替代Unity测试。
- Blender动作管线：`D:/blender.exe -b --python-exit-code 1 --python Tools/Blender/build_pet01_arm_separation.py -- --phase <stage|actions|probe|validate|preview|promote>`；单段动作改版用独立脚本（如 `build_pet01_calm_gassho.py`、`build_pet01_social_laugh.py`），流程为 probe→build→validate→preview→promote，其 `SOURCE_SHA` 须等于当前运行源；`promote` 会覆盖 `pet01.blend` 和Unity FBX，须先归档并经用户同意。改曲线后重跑 validate+preview，再派视觉子代理审查。
- Vuforia Key入口 Window→Vuforia Configuration；11版没有Load Database勾选。包导入的“Package does not contain valid import settings”历史警告无害。
- `versionDefines` 使用裸版本号 `"0.0.0"`，`[0.0.0,)` 会导致宏失效。
- ImageTarget必须初始化：保留`mInitializedInEditor=true`、`mTrackingOptimizationNeedsUpgrade=false`修复。创建多个目标前清Selection，核对平级父子关系和实际宽高。
- ProjectSetup 菜单5把02_Main接成四张平级`Target_card_*`（尺寸读XML），菜单6补齐材质/Animator/触屏/四宠列表；批处理入口 `ProjectSetup.RebuildMainSceneForBatch`，共用工具 `Editor/PetSceneAssets.cs`。`04_FourCardTracking` 由 `FourCardTrackingSetup` 独立生成/构建。
- Vuforia本地tgz不在GitHub仓库，组员按SETUP安装；Archive也仅本机保留。保留 `.meta`、Assets/Packages/ProjectSettings，排除Library/Temp/构建缓存。Git上传故障的既有处置见历史记录，不擅自改remote。

## 文档同步约定

- 用户最新决定优先；产品目标查范围文档，实际实现查源码/序列化配置，已验证结果查带日期的报告。设计稿、历史记录不能证明功能已经接线或通过测试。
- 修改现役文档必须检查正文、表格、待办和目录 README，不能只在开头追加“最新进展”而保留相反结论。详细同步对象见 [Docs/README.md](Docs/README.md)。
- 范围变化同步范围文档、架构迁移说明与入口摘要；目标库/卡片变化同步资产映射、元数据和 StreamingAssets/SETUP；工具链变化同步环境两表、SETUP 和工程 README；运行接线变化同步对应目录说明和架构状态表。
- 卡片 ID、路径、尺寸、哈希与评级以资产映射及其 JSON 为详细清单；已完成、仅资源存在、尚未接线、历史测试、提案五种状态分别写清。原始指令、已提交报告/演讲稿、归档与设计快照保留，通过导航说明适用日期。
- 文档变更验证本地链接、相关 JSON 唯一键、资产路径/哈希和 `git diff --check`；测试数字带执行日期及验证层级。只改文档不重跑 Unity/Blender，也不把历史 98/98 当作本轮测试。

## 工作方式与验证

- 使用中文；限定相关目录以rg搜索。在已授权范围内直接推进，不重复询问已确认方向。
- 保留用户现有文件和未提交工作，不删除课件、工作副本或归档。文档同步不等于代码改版、数据库导入或发布。
- 完成阶段后同步现役文档和映射；历史流水放历史文档，AGENTS保留约束和短状态索引。
- 对业务去重、手势边界、优先级、多宠组同步、丢失恢复和迁移作必要验证；文档修改检查链接、JSON和资产映射，不机械添加业务测试。
- 真机记录设备、系统、APK版本、实际尺寸、光照、距离和测试时长；30FPS是目标，非已有结果。未经实测的平台/功能标pending，不虚构评分或满意度。
- 最终交付APK、源码、源资产与导出资源、打印卡、使用说明、测试和演示材料。已提交开题保持历史，范围变化由现役文档明确说明。
