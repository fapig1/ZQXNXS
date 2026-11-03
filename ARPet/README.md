# ARPet — Unity 6 LTS 主工程

本目录是“桌上有龙”的 **Unity 主工程根目录**，Android 应用与 APK 都由这里产出。

> **2026-10-27 现役状态（P2 多宠竖切 + P3 手势与满值闭环完成）**：范围仍为四色独立奶蛙、合并开心值、单击/双击、100% 专属动作、靠近共同大笑、最终四只同屏，删除食物卡。源码已是多宠：`Pet/PetRuntime` 每宠独立数值与行为机，`App/PetSession` 逐宠跟踪/事件路由/丢失隐藏，`AppRoot.pets` 装配四只；食物链（`FeedingRule`、饥饿/精力衰减、Eat 行为配置）已退出运行；存档为 schema 2（v1 只读迁移）。P3 后：`Interaction/TapGestureRecognizer` 每宠独立 0.28s 双击窗口，满值周期（`Pet/HappyCycle`）直接播四段正式专属片段，TouchReact 已进 Animator，四色参数已定稿进 `PetBehaviorConfig.asset`，面板显示逐宠开心值与选中宠开心条。`02_Main` 已重建为四色多宠主场景（四个 `Target_card_*` 各挂一只），仍是唯一构建场景。P4 接近组共同大笑（两卡靠近整组同帧播 social_laugh，丢失整组取消）与 P5 二维嘴眼（Head 骨骼下两片贴片，`ZQXNXS/FaceDecal`，专属按形象出招牌表情）已接。真实 Unity EditMode **231/231 通过**（2026-10-27，`TestResults-P5Face-20261027.xml`；P4 223/223、P3 161/161、P2 112/112 为阶段证据）；**四宠场景尚未构建 APK、未真机测试**，手势手感、共同大笑与表情观感未经真机验证。特效、音效未做。四卡与接入缺口见 [资产映射](../Docs/四色奶蛙资产映射.md)，新架构与阶段计划见 [范围v0.3](../Docs/交互与皮肤扩展设计.md)。
>
> 下面是 2026-09-18 的单宠历史基线（98/98、Den/Food 双卡），仅用于追溯，不代表当前场景。

> 历史基线（2026-09-18，已被上方 2026-10-27 状态取代）：**工程已在真实 Unity 6 中编译、测试并产出 Android 基础测试 APK。**
> 环境为 Unity `6000.0.83f1`（Unity 6.0 LTS）+ 真实 Vuforia Engine `11.4.4`（本地 UPM 包）。
>
> | 已确认（真实 Unity 实测） | 数值 / 证据 |
> | --- | --- |
> | 11 个程序集编译 | 全部通过，无 `error CS`（`Library/ScriptAssemblies/`） |
> | EditMode 测试 | **98 / 98 通过，0 失败**（`TestResults-EditMode.xml`，含 5 项 `02_Main` 场景接线测试） |
> | Vuforia 真实 SDK 编译 | 通过（`AR.dll` 中含 `VuforiaApplication` 等类型名） |
> | 场景与配置生成 | `01_TrackingSmoke`、`02_Main`、`PetBehaviorConfig.asset` 均由编辑器工具生成，序列化引用正确 |
> | 本地替身检查 | 106 / 106 通过（83 规则 + 23 装配），基线来自 2026-09-16，未随新增测试更新 |
>
> **2026-09-18 更新**：Pet01、材质、Animator、触屏输入、状态面板与 EventSystem 已接入 `02_Main`；
> `autoCreateStubSource` 保持关闭，当时 Pet01 挂在 `DenCardTarget` 下并使用导出坐标系单位旋转（2026-10-27 已改为四张 `Target_card_*`）。
> 同时保留 `01_TrackingSmoke` 的 Animator 与材质接入（见下方"Animator 与材质"一节）；
> `TouchInputBridge` 触屏输入链路已接入；Android 构建目标已切换（IL2CPP / 仅 ARM64 / minSdk API 29）；
> 当时 `02_Main` 的 Vuforia ARCamera、两张占位 ImageTarget 与 `VuforiaTrackingSource` 已接好（见下方"02_Main 的 Vuforia 接线"一节）。
> **截至 2026-09-18 仍待完成**：四色业务Prefab、音效与二维表情资产、完整真机交互回归；新四卡数据库已于2026-09-22导入，但当时场景仍使用旧目标。Android 基础测试 APK 已生成并在
> iQOO Z9 Turbo / Android 15 上验证启动、双目标观察者创建及两张占位卡识别。四个候选动作中 Happy 已决定不做，
> 另外三段（TouchReact / DozeLoop / WalkLoop）于 2026-09-17 做完了两段半——见下方"补充动作"一节。
> 占位卡打印页为 `../ArtSource/Markers/Placeholder/print_test_cards_A4.pdf`，必须按 100% 打印。
>
> 2026-09-16 本轮修掉一个 P1 缺陷：两个 `.asmdef` 的 `versionDefines` 表达式原写为 `[0.0.0,)`，
> 被 Unity 解析器拒绝（`ExpressionNotValidException`），导致 `VUFORIA_ENGINE` 宏从未定义、
> Vuforia 真实实现一直被条件编译排除。已改为裸版本号 `"0.0.0"`，详见
> [代码审查问题与修复记录](../代码审查问题与修复记录.md) 第 4.5 节。

## 目录说明

2026-09-22新增 `Assets/_Project/Scenes/03_ExclusiveAnimationPreview.unity`：进入Play后用四个按钮播放对应骨骼片段。四色基础材质、四专属Animator状态与 `PetExclusiveAnimations.asset` 已生成。classic_proud（打招呼）与 divine_awaken（水平展翅）已于 2026-09-29 重做，calm_zen 已于 2026-10-26 改为合十；2026-10-27 起 TouchReact、满值业务触发、共同大笑与嘴眼二维表情已接通（03 预览场景不显示表情贴片，表情在 02_Main 中由行为驱动），特效与音效仍未做。制作与验收证据见[手臂分离修订记录](../Docs/手臂分离修订记录_20260929.md)与[合十改版记录](../Docs/calm_zen合十改版记录_20261026.md)。

~~~text
ARPet/
  SETUP.md                        首次导入 / 初始化 / 验证步骤（先读这个）
  Assets/
    _Project/                     本项目自己的资产，全部代码在这里
      Scenes/                     场景：01_TrackingSmoke、02_Main、03_ExclusiveAnimationPreview、04_FourCardTracking
      Scripts/                    分层 C# 代码，见下表
      Art/
        Characters/Pet01/         ← 已存在的奶蛙模型 FBX 与贴图
        UI/  VFX/                 二维表情、界面图、特效
      Animations/                 Animator Controller 等播放配置
      Audio/                      角色、交互与界面音效
      Prefabs/                    宠物、球、界面等预制体
      Configs/                    ScriptableObject 配置资产 + JSON 模板
    StreamingAssets/              Vuforia 设备数据库（.dat/.xml）运行期读取位置
    Plugins/Android/              按需接入的原生插件
  Packages/manifest.json          包依赖（Unity 首次打开时补全 packages-lock.json）
  ProjectSettings/                工程设置（首次打开时生成 *.asset）
~~~

| 目录 | 命名空间 | 层次 / 职责 |
| --- | --- | --- |
| `Scripts/Core/` | `ZQXNXS.ARPet.Core` | 共享契约：接口、枚举、只读数据结构。**不依赖任何程序集。** |
| `Scripts/AR/` | `ZQXNXS.ARPet.AR` | ① 跟踪层：按 `TargetId` 出世界米制位姿、有效性、逐宠恢复状态 |
| `Scripts/Interaction/` | `ZQXNXS.ARPet.Interaction` | ② 交互判定层：触屏逗弄事件 + 总闸门（投喂已删除，追球待实现） |
| `Scripts/Pet/` | `ZQXNXS.ARPet.Pet` | ③ 行为状态机层：状态数值 + 行为优先级 + 唯一结算点 |
| `Scripts/Presentation/` | `ZQXNXS.ARPet.Presentation` | ④ 表现层：动画、表情、音效、特效 |
| `Scripts/Persistence/` | `ZQXNXS.ARPet.Persistence` | ⑥ 数据层：版本化 JSON 存档 + 写盘节流 |
| `Scripts/UI/` | `ZQXNXS.ARPet.UI` | ⑤ 界面层：状态面板、提示、安全区、输入隔离 |
| `Scripts/Platform/` | `ZQXNXS.ARPet.Platform` | ⑦ 平台层：权限、生命周期、相册 |
| `Scripts/App/` | `ZQXNXS.ARPet.App` | 装配层：`AppRoot`（唯一组装点）+ `PetSession`（逐宠运行容器）+ `PetHitTarget`（触屏命中标记） |
| `Scripts/Editor/` | `ZQXNXS.ARPet.EditorTools` | 编辑器工具：一键生成配置与场景、工程设置检查 |
| `Scripts/Tests/EditMode/` | `ZQXNXS.ARPet.Tests.EditMode` | 规则测试：重复结算、优先级、阈值边界、手势识别、满值周期、存档 v2/迁移、逐宠丢失恢复与事件路由（14 份文件） |

共 **11 个 `.asmdef`**。依赖方向是**单向**的：

~~~text
Core ← AR / Pet / Presentation / UI / Persistence / Platform
Pet  ← Interaction
Core / Pet ← Persistence
全部 ← App（装配层，无人依赖它）
~~~

两个关键约束：**`Presentation` 不依赖 `Pet`**（通过 `Core.IPetPresenter` 被反向驱动，
所以行为规则不会被写进动画脚本）；**`AR` 只依赖 `Core`**（换掉 Vuforia 或换成模拟源，
业务层一行都不用改）。详见 [Docs/项目架构与技术架构.md](../Docs/项目架构与技术架构.md)。

## Animator 与材质（2026-09-17）

菜单 **桌上有龙 / 3. 接入角色材质、Animator 与触屏输入**（`ProjectSetup.AttachMaterialsAndAnimatorToSmokeScene`）：

- 创建 `Art/Characters/Pet01/Materials/Pet01_Skin.mat` / `Pet01_Oral.mat` / `Pet01_Eyes.mat`
  （Standard 着色器，Metallic=0，Smoothness 依次 0.43/0.57/0.64，均引用 `Pet01_BaseColor.png`），
  并按 Pet01.fbx 的**实际子网格顺序**（`[0]=Skin, [1]=Oral, [2]=Eyes`，与
  `final_manifest.json` 里 `materials_planned` 的列出顺序不同，已用编辑器菜单核对过）赋给
  `SkinnedMeshRenderer.sharedMaterials`。
- 创建 `Assets/_Project/Animations/Pet01Controller.controller`：初始为 Idle（循环）/ Eat（单次）两个状态，
  分别挂 `Pet01_Idle.fbx` / `Pet01_Eat.fbx` 里的同名 AnimationClip。四专属动作、social_laugh 与 TouchReact 状态随后由专属动作工具追加（TouchReact 0.7s 非循环，2026-10-27 接入）；
  **Eat 现在是旧资产**（片段仍在 Controller，进食已不属于新版玩法）。**状态之间没有连过渡边**——
  `PetPresenter.PlayBehavior` 用 `Animator.CrossFadeInFixedTime(状态名哈希, ...)` 直接切状态，
  完全绕开 Controller 自己的过渡图，连一条没有 Exit Time / 条件的边只会让 Unity 打印
  "transition will be ignored" 的噪音警告。
- 把该 Animator Controller 挂到 `01_TrackingSmoke` 场景里 Pet01 实例的 `Animator` 组件上，
  并把 `PetPresenter.animator` 序列化字段指向它。
- 幂等：已存在的材质、Controller、场景引用不会被覆盖，可重复运行。

`Assets/_Project/Scripts/Tests/EditMode/PetPresenterAnimatorTests.cs` 用真实的
`Pet01Controller.controller` 驱动 `PetPresenter`，断言 Idle 片段循环、Eat 片段不循环、
时长为 3.0s/3.2s，并让 `Animator` 真的走完 Idle→Eat→Idle 的状态切换（不是只检查引用非空）。

**历史真机记录（2026-09-18）**：当时 `02_Main` 的摄像头背景、Den/Food 两张占位卡识别和 Idle 显示正常。
2026-10-27 起 `02_Main` 已改为四色多宠主场景（四个 `Target_card_*` 各挂一只对应颜色的宠物），
上述单宠实测结论不再代表当前场景；丢失恢复、触屏命中和真实光照下的材质观感仍待真机验证。
场景接线约束由 `MainSceneWiringTests.cs` 固定。

## 补充动作（2026-09-17，2026-10-27 部分接入）

`01_TrackingSmoke` 里最早只有 Idle / Eat 两段动画在跑。同一天在冻结骨架上又做了三段补充动作，
FBX 都在 `Art/Characters/Pet01/Models/` 里。**TouchReact 已于 2026-10-27 接入 Animator 并由 `Petted` 使用；DozeLoop 与 WalkLoop 仍未接**：

| 动作 | 文件 | 对应行为 | 状态 |
| --- | --- | --- | --- |
| TouchReact | `Pet01_TouchReact.fbx` (0.7 s 单次) | `Petted` | **已接**：`ExclusiveAnimationSetup.Install` 建进 Controller（第 0 层、非循环），FBX 按 Custom/8 影响导入；单击未满值时播放 |
| DozeLoop | `Pet01_DozeLoop.fbx` (4.0 s 循环) | `Resting` | 未接（不在 `AnimatorStates` 清单，行为被禁用） |
| WalkLoop | `Pet01_WalkLoop.fbx` (1.0 s 循环) | `Chasing` | 未接；穿地检查未过：最低点 -0.00079 m，判据 -0.0005 m |

证据与完整调参记录见 [奶蛙最终版资产说明](../Docs/奶蛙最终版资产说明.md) 第 4.1 节与
[奶蛙首轮资产与动作说明](../Docs/奶蛙首轮资产与动作说明.md) 第 9 节；机器可读记录是
`ArtSource/Characters/Pet01/validation_report_extra_actions.json`（当前 `passed: false`）。

**DozeLoop / WalkLoop 接入清单（仍未做）**：

1. 把两份 FBX 的 Rig 设为 Generic、从主模型复制 Avatar；勾 Loop Time。
2. 把两个 `AnimationClip` 加进 `Animations/Pet01Controller.controller`（状态名需与行为名一致，
   `PetPresenter.PlayBehavior` 用状态名哈希 `CrossFade` 直切）。
3. 把状态名加进 `PetConfig.AnimatorStates` 清单——旧 `Aspect.HasAnimatorClip` 硬编码已于 2026-10-27 删除，可用性只看能力清单。**这一步不做，动画做完了状态机也不会切过去**。
4. `WalkLoop` 先解决穿地（或明确接受 -0.79 mm 并记录理由）再接入。

**仍未验证**：DozeLoop / WalkLoop 在 Unity 里的播放效果与真机表现；`WalkLoop` 的穿地问题。TouchReact 已接线，观感待真机确认。

## 触屏输入链路（2026-09-17）

`ARPet/Assets/_Project/Scripts/App/TouchInputBridge.cs`：把 `Input`（编辑器/PC 走鼠标，
Android 真机走触屏）转换成 `AppRoot.OnTouchPointerDown/Up` 需要的屏幕坐标与命中的 `petId`。
命中目标由场景里各宠物根上的 `PetHitTarget` 提供（一个标记 = 一只宠物），多宠同屏时按 `petId` 路由。
两条纪律：

- 命中判定是**从摄像机出发的射线**，有碰撞体时用 `Collider.Raycast`；没有碰撞体时退化为
  "射线到某个锚点变换的最近距离是否小于半径"的近似判定（`HitsPet` 方法，公开出来是为了能在
  EditMode 测试里直接构造场景验证，不需要真的触发一次 `Input` 事件）。
- 是否落在 UI 上交给 `EventSystem.IsPointerOverGameObject` 判断，与命中判定完全独立，
  互不依赖，符合"界面输入不得穿透到角色"的项目纪律。

已通过 `ProjectSetup.AttachMaterialsAndAnimatorToSmokeScene`（菜单"3. 接入角色材质、Animator 与
触屏输入"）接入 `01_TrackingSmoke`；`Assets/_Project/Scripts/Tests/EditMode/TouchInputBridgeTests.cs`
覆盖有碰撞体 / 无碰撞体两条判定路径、命中/未命中、摄像机背后等边界情况。

2026-10-27 起 Android 分支在 `Began` 时记住该触点的 `fingerId`，之后每帧按 `fingerId` 找回（不是固定 `GetTouch(0)`），其他手指忽略；合格点按交给 `Interaction/TapGestureRecognizer`：每只宠物一个 0.28s 双击窗口，窗口内同一只的第二次合格抬起算双击，跨宠不合成。移动阈值改为 `max(40px, dpi×0.25in)`（约 450dpi 时约 112px），输入侧原先的 0.45s 冷却已删除。

## 02_Main 的 Vuforia 接线（2026-10-27 改为四色多宠）

菜单 **桌上有龙 / 5. 接入 02_Main 的四色多宠 Vuforia 接线**
（`ProjectSetup.WireUpMainSceneVuforia`，只在 `VUFORIA_ENGINE` 宏点亮时可用；菜单 6 负责补齐
材质/Animator/触屏/UI，批处理入口是 `ProjectSetup.RebuildMainSceneForBatch`）：

- 复用场景里已有的 ARCamera（缺失时才调用 `GameObject/Vuforia Engine/AR Camera`）。
- 对身份目录 `PetIdentityCatalog.Default` 的四个身份各建/复用一张平级 ImageTarget：
  `Target_card_classic` / `Target_card_calm` / `Target_card_rage` / `Target_card_divine`，
  都挂在场景根、互不为父子；`mDataSetPath` = `Vuforia/ZQXS.xml`（StreamingAssets 相对路径），
  `mTrackableName` = 对应 `card_*`，`mWidth`/`mHeight` 取自 `ZQXS.xml`（0.066×0.093923 m），
  `mInitializedInEditor` = `true`、`mTrackingOptimizationNeedsUpgrade` = `false`。
  这些字段没有官方文档，是通过反射 `Vuforia.Unity.Engine.dll` 与
  `Vuforia.Unity.Editor.dll` 核实的；Vuforia 升级后如果接线失效，要用同样的反射方法
  重新核对字段名是否还存在。
- 每张卡下建 `PetRoot_<petId>`：`PetPresenter`（`animator` 与 `visualRoot` 指向 Pet01 实例）、
  `PetHitTarget`（`petId` 与身份目录一致，命中半径 0.08 m）、对应颜色的 Pet01 实例
  （`Pet01_{Skin,Oral,Eyes}_<variant>` 三个材质 + `Pet01Controller`）。朝向沿用导出坐标约定：
  局部位置/旋转/缩放均不附加补偿，`+Y` 向上、`+Z` 向前。
- 不保留 `DefaultObserverEventHandler`：显隐只由 `PetPresenter.SetTrackingVisibility` 按逐宠
  跟踪状态切 `Renderer.enabled`。
- `VuforiaTrackingSource`（挂在 App 物体上）的 `targets` 写 4 条，`AppRoot.pets` 写 4 条
  （`petId` + `presenter`）；`trackingSourceBehaviour`/`markerRegistryBehaviour` 都指向它。
- 旧单宠的 `DenCardTarget` / `FoodCardTarget`（连同其下旧 Pet01 与示例观察者）会被删除。
- `VuforiaConfiguration.MaxSimultaneousImageTargets` 只上调到至少 4（默认 1）。
- 幂等：重复运行不会产生第二套 ARCamera / ImageTarget / 宠物。
- 诊断面板新增 `PetListText`，逐行显示四只宠物的变体与跟踪状态（选中项前加 "▶"）。

**排雷记录**：Unity 的 `GameObject/...` 菜单默认把新物体建成"当前选中物体的子物体"，
嵌套会让 Vuforia 把子级目标的宽高静默改回默认 0.2 m（2026-09-17 实测）。现在每次调用菜单项前
都清空 `Selection.activeGameObject`，创建后显式 `transform.SetParent(null)` 并重新写一次尺寸。
batchmode 下 Vuforia 的 Image Target 菜单可能不执行，此时退化为直接挂 `ImageTargetBehaviour`
组件并打印警告——这条路径没有经过 Vuforia 自己的菜单，真机识别前应当用编辑器菜单 5 复核。

**2026-09-18 单宠真机记录（已过时）**：当时发现 `DenCardTarget.mInitializedInEditor` 被序列化为
`false`，Vuforia 11 会在 `ObserverBehaviour.OnVuforiaStarted` 直接跳过该目标。现由
`ApplyImageTargetProperties` 写入完整初始化状态，菜单 6 在每次构建前对四张卡重新套用属性。
四色多宠场景本身**尚未真机验证**（识别稳定性、四宠同屏性能、丢失恢复）；场景接线约束由
`MainSceneWiringTests` 固定。

## Android 构建入口（2026-09-18）

命令行构建：

```powershell
pwsh -File D:\ZengQiangXianShi\Tools\Unity\build-android-apk.ps1
```

它做三件事：构建前单跑一次 `sdkmanager --list` 早检（确认超时参数真的生效）、给 sdkmanager
的 JVM 带上连接/读取超时、构建后核对 APK 是否被本次构建刷新并打印 SHA256。等价的编辑器菜单
是 **桌上有龙 / 7. 构建 Android 基础测试 APK**。

之所以需要脚本，是因为本机到 `dl.google.com` 的请求会连上却不返回，`CheckAndroidSDK`
默认会永久停在 `Detecting Android SDK`。根因、环境变量与实测无效的替代做法见
[SETUP.md](SETUP.md) 第 7 节"构建不再卡死"，速查表见同文件第 8 节常见坑。

## 三层可运行程度

| 层 | 是否需要 Vuforia | 现状 |
| --- | --- | --- |
| Core / Pet / Interaction / Persistence | 否 | 真实 Unity EditMode **161/161**（2026-10-27）；多宠路由、逐宠丢失恢复、单击/双击识别、满值周期、存档 v2 与 v1 迁移已覆盖 |
| UI / Presentation / Platform | 否 | 状态面板（含四宠列表开心值与选中宠开心条）、安全区、输入隔离和 Animator（含 TouchReact）已接入；表情、特效、拍照待实现 |
| AR（`VuforiaTrackingSource`） | 是 | 2026-09-18 单宠真机已验证初始化与两张占位卡识别；2026-10-27 四卡四宠 `02_Main` **未构建 APK、未真机测试**（逐宠丢失/恢复、触屏命中与手势手感、满值演出、v1→v2 迁移 pending） |

未导入 Vuforia 时，`StubTrackingSource` 提供手动摆位的模拟目标，让状态机与界面能先跑通。

本地替身检查入口：`pwsh -NoProfile -File ../Tools/Validation/run-rule-checks.ps1`（2026-10-27：0 编译错误，158 passed / 21 failed，21 条为已知基线）。真实 Unity 结果以
`TestResults-P3Gesture-20261027.xml`（161/161）为准，P2 的 112/112 见 `TestResults-P2MultiPet-20261027.xml`；两者都不能替代 Android 真机跟踪验证。

## 约定

- 单位统一为**米**，角色高约 0.2 m；所有跟踪目标共用同一世界米制坐标系，接近判定直接使用世界位姿（`Interaction.ProximityRule`，2026-10-27 P4 实现）。
- 提交源码时保留 `Assets` 的 `.meta`、`Packages/`、`ProjectSettings/`；排除 `Library`、`Temp`、`Logs`、`Obj`。
- 不在 `Update` 里改状态数值；一次行为只在确定时刻结算一次（见 `BehaviorStateMachine`）。
