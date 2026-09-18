# ARPet — Unity 6 LTS 主工程

本目录是“桌上有龙”的 **Unity 主工程根目录**，Android 应用与 APK 都由这里产出。

> 现状（2026-09-18）：**工程已在真实 Unity 6 中编译、测试并产出 Android 基础测试 APK。**
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
> `autoCreateStubSource` 保持关闭，Pet01 挂在 `DenCardTarget` 下并使用导出坐标系单位旋转。
> 同时保留 `01_TrackingSmoke` 的 Animator 与材质接入（见下方"Animator 与材质"一节）；
> `TouchInputBridge` 触屏输入链路已接入；Android 构建目标已切换（IL2CPP / 仅 ARM64 / minSdk API 29）；
> `02_Main` 场景的 Vuforia ARCamera、两张 ImageTarget 与 `VuforiaTrackingSource` 已接好（见下方"02_Main 的 Vuforia 接线"一节）。
> **仍然没有的**：Prefab、音效与二维表情资产、正式标记卡与完整真机交互回归；Android 基础测试 APK 已生成并在
> iQOO Z9 Turbo / Android 15 上验证启动、双目标观察者创建及两张占位卡识别。四个候选动作中 Happy 已决定不做，
> 另外三段（TouchReact / DozeLoop / WalkLoop）于 2026-09-17 做完了两段半——见下方"补充动作"一节。
> 占位卡打印页为 `../ArtSource/Markers/Placeholder/print_test_cards_A4.pdf`，必须按 100% 打印。
>
> 2026-09-16 本轮修掉一个 P1 缺陷：两个 `.asmdef` 的 `versionDefines` 表达式原写为 `[0.0.0,)`，
> 被 Unity 解析器拒绝（`ExpressionNotValidException`），导致 `VUFORIA_ENGINE` 宏从未定义、
> Vuforia 真实实现一直被条件编译排除。已改为裸版本号 `"0.0.0"`，详见
> [代码审查问题与修复记录](../代码审查问题与修复记录.md) 第 4.5 节。

## 目录说明

~~~text
ARPet/
  SETUP.md                        首次导入 / 初始化 / 验证步骤（先读这个）
  Assets/
    _Project/                     本项目自己的资产，全部代码在这里
      Scenes/                     场景：01_TrackingSmoke、02_Main
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
| `Scripts/AR/` | `ZQXNXS.ARPet.AR` | ① 跟踪层：目标位姿、有效性与恢复、小窝局部坐标换算 |
| `Scripts/Interaction/` | `ZQXNXS.ARPet.Interaction` | ② 交互判定层：投喂 / 逗弄 / 追球事件 + 总闸门 |
| `Scripts/Pet/` | `ZQXNXS.ARPet.Pet` | ③ 行为状态机层：状态数值 + 行为优先级 + 唯一结算点 |
| `Scripts/Presentation/` | `ZQXNXS.ARPet.Presentation` | ④ 表现层：动画、表情、音效、特效 |
| `Scripts/Persistence/` | `ZQXNXS.ARPet.Persistence` | ⑥ 数据层：版本化 JSON 存档 + 写盘节流 |
| `Scripts/UI/` | `ZQXNXS.ARPet.UI` | ⑤ 界面层：状态面板、提示、安全区、输入隔离 |
| `Scripts/Platform/` | `ZQXNXS.ARPet.Platform` | ⑦ 平台层：权限、生命周期、相册 |
| `Scripts/App/` | `ZQXNXS.ARPet.App` | 装配层：`AppRoot` —— 唯一知道各层如何拼装的地方 |
| `Scripts/Editor/` | `ZQXNXS.ARPet.EditorTools` | 编辑器工具：一键生成配置与场景、工程设置检查 |
| `Scripts/Tests/EditMode/` | `ZQXNXS.ARPet.Tests.EditMode` | 规则测试：重复结算、优先级、阈值边界、存档恢复 |

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
- 创建 `Assets/_Project/Animations/Pet01Controller.controller`：Idle（循环）/ Eat（单次）两个状态，
  分别挂 `Pet01_Idle.fbx` / `Pet01_Eat.fbx` 里的同名 AnimationClip。**两个状态之间没有连过渡边**——
  `PetPresenter.PlayBehavior` 用 `Animator.CrossFadeInFixedTime(状态名哈希, ...)` 直接切状态，
  完全绕开 Controller 自己的过渡图，连一条没有 Exit Time / 条件的边只会让 Unity 打印
  "transition will be ignored" 的噪音警告。
- 把该 Animator Controller 挂到 `01_TrackingSmoke` 场景里 Pet01 实例的 `Animator` 组件上，
  并把 `PetPresenter.animator` 序列化字段指向它。
- 幂等：已存在的材质、Controller、场景引用不会被覆盖，可重复运行。

`Assets/_Project/Scripts/Tests/EditMode/PetPresenterAnimatorTests.cs` 用真实的
`Pet01Controller.controller` 驱动 `PetPresenter`，断言 Idle 片段循环、Eat 片段不循环、
时长为 3.0s/3.2s，并让 `Animator` 真的走完 Idle→Eat→Idle 的状态切换（不是只检查引用非空）。

**已接入且完成基础真机验证**：`02_Main` 的摄像头背景、小窝/食物占位卡识别和 Idle 显示正常；
仍待验证 Eat 触发、丢失恢复、触屏命中和不同真实光照下的材质观感。场景接线约束由
`MainSceneWiringTests.cs` 固定。

## 补充动作（2026-09-17）

`01_TrackingSmoke` 里目前只有 Idle / Eat 两段动画在跑。同一天在冻结骨架上又做了三段补充动作，
FBX 已经躺在 `Art/Characters/Pet01/Models/` 里，但**还没有接进 Unity**：

| 动作 | 文件 | 对应行为 | Blender 侧状态 |
| --- | --- | --- | --- |
| TouchReact | `Pet01_TouchReact.fbx` (0.7 s 单次) | `Petted` | 检查全部通过 |
| DozeLoop | `Pet01_DozeLoop.fbx` (4.0 s 循环) | `Resting` | 检查全部通过 |
| WalkLoop | `Pet01_WalkLoop.fbx` (1.0 s 循环) | `Chasing` | 穿地检查未过：最低点 -0.00079 m，判据 -0.0005 m |

证据与完整调参记录见 [奶蛙最终版资产说明](../Docs/奶蛙最终版资产说明.md) 第 4.1 节与
[奶蛙首轮资产与动作说明](../Docs/奶蛙首轮资产与动作说明.md) 第 9 节；机器可读记录是
`ArtSource/Characters/Pet01/validation_report_extra_actions.json`（当前 `passed: false`）。

**接入清单（三条都还没做）**：

1. 把三份 FBX 的 Rig 设为 Generic、从主模型复制 Avatar；`DozeLoop` / `WalkLoop` 勾 Loop Time，
   `TouchReact` 不勾。
2. 把三个 `AnimationClip` 加进 `Animations/Pet01Controller.controller`（状态名需与行为名一致，
   `PetPresenter.PlayBehavior` 用状态名哈希 `CrossFade` 直切）。
3. 打开 `Scripts/Pet/PetConfig.cs` 里 `Aspect.HasAnimatorClip` 的三行：
   `Petted` / `Resting` / `Chasing` 目前显式返回 `false`——**这一步不做，动画做完了状态机也不会切过去**。
4. `WalkLoop` 先解决穿地（或明确接受 -0.79 mm 并记录理由）再接入。

**仍未验证**：三段动作在 Unity 里的播放效果、真机表现；`WalkLoop` 的穿地问题。

## 触屏输入链路（2026-09-17）

`ARPet/Assets/_Project/Scripts/App/TouchInputBridge.cs`：把 `Input`（编辑器/PC 走鼠标，
Android 真机走触屏）转换成 `AppRoot.FeedTouchPointerDown/Up` 需要的屏幕坐标与"是否命中角色"。
两条纪律：

- 命中判定是**从摄像机出发的射线**，有碰撞体时用 `Collider.Raycast`；没有碰撞体时退化为
  "射线到某个锚点变换的最近距离是否小于半径"的近似判定（`HitsPet` 方法，公开出来是为了能在
  EditMode 测试里直接构造场景验证，不需要真的触发一次 `Input` 事件）。
- 是否落在 UI 上交给 `EventSystem.IsPointerOverGameObject` 判断，与命中判定完全独立，
  互不依赖，符合"界面输入不得穿透到角色"的项目纪律。

已通过 `ProjectSetup.AttachMaterialsAndAnimatorToSmokeScene`（菜单"3. 接入角色材质、Animator 与
触屏输入"）接入 `01_TrackingSmoke`；`Assets/_Project/Scripts/Tests/EditMode/TouchInputBridgeTests.cs`
覆盖有碰撞体 / 无碰撞体两条判定路径、命中/未命中、摄像机背后等边界情况。

## 02_Main 的 Vuforia 接线（2026-09-17）

菜单 **桌上有龙 / 5. 接入 02_Main 的 Vuforia ARCamera 与 ImageTarget**
（`ProjectSetup.WireUpMainSceneVuforia`，只在 `VUFORIA_ENGINE` 宏点亮时可用）：

- 用 `GameObject/Vuforia Engine/AR Camera` 与 `.../Image Target` 两个官方菜单项生成
  ARCamera 与两张 ImageTarget（`DenCardTarget` / `FoodCardTarget`），三者都挂在场景根，
  互不为父子。
- 把两张 ImageTarget 的 `mDataSetPath` 设为 `ZQXS`（Target Manager 数据库名），
  `mTrackableName` 分别设为 `den_card_placeholder` / `food_card_placeholder`，
  `mWidth`/`mHeight` 设为 0.1 m（与 `Assets/StreamingAssets/Vuforia/ZQXS.xml` 里登记的
  占位图尺寸一致），并把 `mInitializedInEditor` 设为 `true`、
  `mTrackingOptimizationNeedsUpgrade` 设为 `false`。这些字段没有官方文档，是通过反射
  `Vuforia.Unity.Engine.dll` 与 `Vuforia.Unity.Editor.dll`（编辑器画 Database/Target
  下拉框用的 `VuforiaUtilities.DrawDatabaseTargetInspector`）核实的；Vuforia 升级后
  如果接线失效，要用同样的反射方法重新核对字段名是否还存在。
- 把 `VuforiaTrackingSource` 挂到 `AppRoot` 所在物体上，`denTargetObject`/`foodTargetObject`
  分别指向两张卡，`denTargetSize`/`foodTargetSize` 设为 0.1×0.1 m；再把 `AppRoot` 的
  `trackingSourceBehaviour`/`markerRegistryBehaviour` 都指向它。
- 把 `VuforiaConfiguration.MaxSimultaneousImageTargets` 从默认的 1 调到 2——项目需要同时
  跟踪小窝卡与食物卡，默认值只允许 1 个会导致食物卡永远侧不到。
- 幂等：重复运行不会产生第二套 ARCamera / ImageTarget（已用两次连续运行验证，第二次运行后
  场景里仍只有一个 ARCamera、一个 DenCardTarget、一个 FoodCardTarget）。

**排雷记录**：第一次实现时用 `EditorApplication.ExecuteMenuItem` 连续创建两个 ImageTarget，
因为 Unity 的 `GameObject/...` 菜单默认把新物体建成"当前选中物体的子物体"，第二张卡
（`FoodCardTarget`）变成了第一张卡的子物体，进而触发 Vuforia 把它的宽高静默改回默认的
0.2 m（与占位数据库登记的 0.1 m 不一致）。现在每次调用菜单项前都清空
`Selection.activeGameObject`，创建完成后再显式把两张卡的 `transform.SetParent(null)`
并重新写一次尺寸做兜底纠正。

**2026-09-18 真机结果**：首次测试发现 `DenCardTarget.mInitializedInEditor` 被序列化为 `false`，
Vuforia 11 会在 `ObserverBehaviour.OnVuforiaStarted` 直接跳过该目标，导致小窝卡永远不创建观察者。
现已由 `ApplyImageTargetProperties` 写入完整初始化状态，`PrepareMainSceneForPhoneTest` 在每次构建前对
两张卡重新套用属性。iQOO Z9 Turbo / Android 15 实测出现两条 dataset 创建、两个
`ImageTargetObserver...SUCCESS`，两张占位卡均进入 `TRACKED -- NORMAL`，小窝卡上的 Pet01 正常显示。
仍待验证丢失/恢复、长时间双目标交互和正式标记卡；场景接线约束由 `MainSceneWiringTests` 固定。

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
| Core / Pet / Interaction / Persistence | 否 | 真实 Unity EditMode 测试已通过；业务规则与存档恢复已覆盖 |
| UI / Presentation / Platform | 否 | 状态面板、安全区、输入隔离和 Animator 已接入；表情、特效、拍照待实现 |
| AR（`VuforiaTrackingSource`） | 是 | iQOO Z9 Turbo 已验证 Vuforia 初始化、双观察者创建和两张占位卡识别；丢失恢复与完整交互回归待测 |

未导入 Vuforia 时，`StubTrackingSource` 提供手动摆位的模拟目标，让状态机与界面能先跑通。

本地替身检查入口：`pwsh -NoProfile -File ../Tools/Validation/run-rule-checks.ps1`。真实 Unity 结果以
`TestResults-EditMode.xml` 为准；两者都不能替代 Android 真机跟踪验证。

## 约定

- 单位统一为**米**，角色高约 0.2 m；空间关系一律在小窝卡的局部坐标系里结算。
- 提交源码时保留 `Assets` 的 `.meta`、`Packages/`、`ProjectSettings/`；排除 `Library`、`Temp`、`Logs`、`Obj`。
- 不在 `Update` 里改状态数值；一次行为只在确定时刻结算一次（见 `BehaviorStateMachine`）。
