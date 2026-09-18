# AGENTS.md

## 项目目标与当前阶段

本目录用于增强现实课程设计“基于图像跟踪与行为状态驱动的移动 AR 萌宠互动系统设计与实现”，产品暂名“桌上有龙”。

项目事实与范围以同目录《开题说明_AR萌宠互动系统.md》、实际工程状态和用户最新指示为依据。本文为 2026-09-18 更新的开发约定与状态快照，后续完成工作时同步更新，不将旧快照当作永久限制。

- 开发者 1 人，约 4 周，有一定 Unity 基础。
- 目标平台为 Android 手机和平板，首要实测设备是 iQOO Z9 Turbo / Android 15。
- 当前已完成开题交付、奶蛙三维资产并**冻结为最终版**：`ArtSource/Characters/Pet01/pet01.blend`、26 根骨骼、Idle / Eat 动画和 3 份 FBX。用户于 2026-09-11 确认该版本为最终版本；解冻须按 [奶蛙最终版资产说明](Docs/奶蛙最终版资产说明.md) 第 7 节先归档并新开 `shape_revision`。Blender 源资产与 FBX 回读的 32 项检查已通过。
- `ARPet/` 的**七层架构骨架已搭建**（11 个程序集、核心业务逻辑、EditMode 测试、编辑器工具与 `SETUP.md`）。2026-09-12 已完成循环审查修复；2026-09-16 **首次进入真实 Unity 环境验证**：Unity `6000.0.83f1` + 真实 Vuforia Engine `11.4.4`，11 个程序集编译通过、**83 / 83 EditMode 测试通过（0 失败）**、两个场景与 `PetBehaviorConfig.asset` 已由编辑器工具生成且序列化引用正确。本轮修复了一个替身验证不可能发现的 P1 缺陷：`versionDefines` 表达式原写为 `[0.0.0,)`，被 Unity 解析器拒绝，导致 `VUFORIA_ENGINE` 宏从未定义、Vuforia 真实实现一直被条件编译排除；已改为裸版本号 `"0.0.0"`。证据见 [代码审查问题与修复记录](代码审查问题与修复记录.md) 第 4.5 节与 `ARPet/TestResults-EditMode.xml`。
- **2026-09-16 下午：Vuforia 全部前置项完成**。License Key 与开发者协议已写入 `VuforiaConfiguration.asset`（`vuforiaLicenseKey`、`eulaAcceptedVersions` 均已有值）；Target Manager 数据库 `ZQXS` 已导入并在 Configuration 的 Databases 列表识别，含两目标 `den_card_placeholder` / `food_card_placeholder`（尺寸 0.1 m，占位图位于 `ArtSource/Markers/Placeholder/`，正式卡未设计）。注意 Vuforia 11 已取消"Load Database"勾选框，ImageTarget 启用时自动加载数据库；导入数据库包时的 "Package does not contain valid import settings" 警告无害（见 SETUP.md 常见坑）。填 Key 的正确菜单入口是 **Window → Vuforia Configuration**，不在 GameObject 菜单下。
- **2026-09-17：Animator Controller 与角色材质已接入 `01_TrackingSmoke` 场景**。菜单“桌上有龙 / 3. 接入角色材质、Animator 与触屏输入”创建了 `Assets/_Project/Animations/Pet01Controller.controller`（Idle 循环 / Eat 单次两个状态，各挂真实 AnimationClip）与三个 Standard 材质（Pet01_Skin/Oral/Eyes，Metallic=0，Smoothness 0.43/0.57/0.64，按实测的子网格顺序赋值），并把 `PetPresenter.animator` 字段接到场景里 Pet01 实例的 Animator 组件上。同一天还完成：（1）`TouchInputBridge` 触屏输入链路（`ARPet/Assets/_Project/Scripts/App/TouchInputBridge.cs`），用射线 + 碰撞体或"锚点球形近似"判定是否命中角色，已接入 `01_TrackingSmoke`；（2）Android 构建设置：菜单"4. 切换到 Android 并设置构建选项"把构建目标切到 Android、脚本后端设为 IL2CPP、架构仅 ARM64、minSdk 设为 API 29（Vuforia 官方支持表要求），`CheckProjectSettings` 复核通过；（3）`02_Main` 场景的 Vuforia 接线：菜单"5. 接入 02_Main 的 Vuforia ARCamera 与 ImageTarget"生成 ARCamera 与两张 ImageTarget（分别绑定 `ZQXS` 数据库的 `den_card_placeholder` / `food_card_placeholder`，尺寸 0.1 m），挂 `VuforiaTrackingSource` 并接好 `AppRoot` 的跟踪源引用，同时把 `VuforiaConfiguration.MaxSimultaneousImageTargets` 从默认的 1 调到 2（同时跟踪两张卡的硬性前提）。ImageTarget 的 `mDataSetPath`/`mTrackableName`/`mWidth`/`mHeight` 字段名没有官方文档，是通过反射 Vuforia 编译产物核实的；过程中还发现并修复一个真实 bug——用 `GameObject/Vuforia Engine/...` 菜单连续创建两个 ImageTarget 时，第二个会成为第一个的子物体，进而导致 Vuforia 把它的宽高静默改回默认的 0.2 m，现在每次创建前都清空 `Selection.activeGameObject` 并显式重新校正父级与尺寸。新增合计 10 项 EditMode 测试（4 项 Animator 驱动 + 6 项 TouchInputBridge 命中判定），全部 93 项测试（含此前 83 项）通过。**仍未完成**：真机验证、真实光照下的材质观感、正式标记卡设计定稿（当前仍是占位图与占位尺寸）、补充动作接入 Unity。
- **2026-09-17 晚：补充动作做到 2 段完成、1 段卡住**。冻结骨架上一共只加了 Action，网格与骨骼未动。`TouchReact`（0.7 s 单次）与 `DozeLoop`（4.0 s 循环）全部检查通过、FBX 回读误差 ≤8.0e-8 m；`WalkLoop`（1.0 s 循环）8 项检查里 7 项通过，唯一未过的是脚部穿地：最低点 -0.00079 m，判据 -0.0005 m（超出 0.29 mm）。已确认**这套骨架的 FBX 烘焙管线承受不住非根骨骼平移动画**（Foot/Pelvis 平移超过 1～2 mm 就让回读偏离到 1.6～1.8e-2 m）；调 Foot 反旋也不是有效杠杆（-15° 比 -11° 还差 0.05 mm，因为穿地点压在踝关节正下方，绕踝旋转几乎不移它），这条方向可以放弃。另有一条旧结论已于同晚**复核并推翻**：此前认为"旋转放大到 22°/38°/-16° 会破坏 FBX 回读（1.9e-2 m）"，隔离重跑后实测回读 5.8e-7 m 完全干净；真实约束是穿地与逐帧平滑度互相拉扯（该变体 `smooth_motion` 0.0109 超判据），且穿地点已定位为摆动腿起步瞬间踝关节正下方的几个脚掌顶点、踝比静止低约 0.6 mm，详见[首轮说明](Docs/奶蛙首轮资产与动作说明.md) 9.3、9.4 节。这段复核用隔离脚本跑在 `D:\Cache_Temp\opencode\walkloop_recheck\`，工程文件未改动。四个候选动作中 Happy 经用户确认不做——代码里没有对应行为。三段动作**尚未接进 Unity**：`PetConfig.Aspect.HasAnimatorClip` 仍把 `Petted`/`Resting`/`Chasing` 返回 `false`，接线清单见 [ARPet/README.md](ARPet/README.md)。同轮清理了两个一次性诊断脚本（`diagnose_foot_bone.py`、`diagnose_pelvis_axis.py`），并把 export/validate 日志落盘到 `ArtSource/Characters/Pet01/Logs/20260917_extra_actions/`。
- **文档与清单同步状态（2026-09-17 晚）**：说明文档（本文、《奶蛙最终版资产说明》、《奶蛙首轮资产与动作说明》、`ARPet/README.md`、`ArtSource/Characters/Pet01/README.md`）已按实物更新；**两个 JSON 清单尚未同步**——`ArtSource/Characters/Pet01/asset_manifest.json` 与 `ARPet/Assets/_Project/Art/Characters/Pet01/final_manifest.json` 的 `clips`、`export_files`/`exported_files`、`not_made_yet` 仍只反映 Idle/Eat；后者的 `frozen.source_sha256` 也仍是 `fad23c5c…`，而 `pet01.blend` 已是 `8dc3dc92…`（冻结前副本在 `Archive/20260917_before_extra_actions/`，哈希可对上）。
- 本机环境（2026-09-17 复核）：Unity 6.0 LTS `6000.0.83f1`（`D:\APP\Unity`，含 Android Build Support、NDK r27c、OpenJDK）、Visual Studio 2022 `17.14.37628.2`（含 C++ 工具链）、Windows SDK `10.0.26100.0`、Vuforia Engine `11.4.4`（本地 UPM 包）。已无 P0 环境缺口，环境细节与版本依据见 [环境缺失与下载清单](Docs/环境缺失与下载清单.md)。工程当前实际为 **Built-in 渲染管线**（无自定义 SRP 资产）、**Gamma 色彩空间**；**Android 平台已切换**（IL2CPP / 仅 ARM64 / minSdk API 29，2026-09-17）。
- **2026-09-18：P1 与手机基础测试链路完成。** `02_Main` 已放入 Pet01（3 材质、Generic Animator、`PetPresenter`），并作为 `DenCardTarget` 子物体使用单位局部旋转；`AppRoot` 的 config / presenter / anchored root 已接线，`autoCreateStubSource` 明确为关闭。新增只读 Core UI 契约、状态/跟踪面板、Safe Area 与 EventSystem。真实 Unity EditMode 测试为 **98 / 98 通过**（证据：`ARPet/TestResults-EditMode.xml`）。已生成 `Builds/Android/ARPet-phone-test.apk`，静态核验为 Debug 签名、`minSdk=29`、`targetSdk=36`、仅 `arm64-v8a`、含 CAMERA 权限；iQOO Z9 Turbo / Android 15 已验证应用启动、相机预览、双目标观察者创建和两张占位卡识别。由于本机访问 Google Maven 不稳定，工程加入 `Assets/Plugins/Android/settingsTemplate.gradle` 的阿里云 Google Maven 镜像作为构建回退。
- **2026-09-18 晚：打包卡死的根因定位与稳定方案。** 症状是 Unity 永远停在 `Detecting Android SDK`、日志反复刷 `Still waiting for package manifests to be fetched remotely.`、进程存活但不推进（Unity 调起的 `java.exe ... SdkManagerCli --list` 一直挂着）。根因：本机到 `dl.google.com` 的 TCP 连得上却没有响应，而 cmdline-tools 用不带超时的 URLConnection 拉远程清单。方案：给 sdkmanager 的 JVM 加连接/读取超时，即用户级环境变量 `SDKMANAGER_OPTS=-Dsun.net.client.defaultConnectTimeout=5000 -Dsun.net.client.defaultReadTimeout=5000`（写入后需重启一次 Unity Hub 才对菜单构建生效）。实测 sdkmanager 约 38 s 后以退出码 0 输出本地已安装包列表，Unity 把两条 warning 记为 warning 后继续构建，SDK 检测整体约 3～4 分钟，不再挂死。已用 `Win32_Process` 实测确认 Unity 的调用链是 `cmd -> sdkmanager.bat -> java ... SdkManagerCli --list` 且会带上该参数。此前"临时 sdkmanager shim"的做法不再需要，新脚本里保留了对 shim 残留的防御检查。新增构建入口 `Tools/Unity/build-android-apk.ps1`（构建前早检 + 带超时构建 + APK 是否被本次刷新 + SHA256）。实测无效、不要再试：`http_proxy`/`https_proxy` 环境变量与 `-Dhttps.proxyHost`/`-Dhttps.proxyPort` 系统属性（sdkmanager 不理会）；阿里云 Maven 镜像只作用于 Gradle 依赖下载阶段，管不到 SDK 检测。
  **端到端实测（同晚）**：不放任何 shim、仅靠该环境变量，Unity 批处理构建在 4.5 分钟内成功产出 `Builds/Android/ARPet-phone-test.apk`；`Detecting Android SDK` 耗时 186.6 s，日志里 6 条 `Still waiting ...` 全是警告级。新 APK 为 85,181,203 B，`aapt2 dump badging` / `apksigner verify` 复核为 Debug 签名、`minSdk=29`、`targetSdk=36`、仅 `arm64-v8a`、含 CAMERA 权限。构建脚本还修复了 PowerShell 参数 `$Unity` 与局部变量 `$unity` 不区分大小写导致进程句柄被强制转成字符串的问题，现可正确等待 Unity、执行硬超时与最终核验。
- **2026-09-18 真机 P0 修复与复测。** 首次运行发现 `DenCardTarget.mInitializedInEditor=0`，Vuforia 11 的 `ObserverBehaviour.OnVuforiaStarted` 因此直接返回，小窝卡永远不创建观察者。`ProjectSetup.ApplyImageTargetProperties()` 现显式写入 `mInitializedInEditor=true`、`mTrackingOptimizationNeedsUpgrade=false`；`PrepareMainSceneForPhoneTest()` 在每次构建前对两张卡重套完整属性；新增 EditMode 回归断言。修复包在 iQOO Z9 Turbo / vivo V2352A、Android 15 / API 35 上实测：Vuforia 11.4.4 初始化成功，`den_card_placeholder` 与 `food_card_placeholder` 各创建一条 dataset、两个 ImageTargetObserver 均成功，两张卡均进入 `TRACKED -- NORMAL`，Pet01 正常锚定显示，0 条 `DATABASE_LOAD_ERROR`、0 条 `FATAL EXCEPTION`。证据：`D:\Cache_Temp\opencode\phone_arpet_p0_fix_20260918.log` 与同名 PNG；丢失恢复和长时间双目标互动仍待回归。
- 用户偏好娱乐化的 AR 萌宠，当前角色方向为奶蛙风格圆润形象。教育场景和桌面布局系统已不作为当前方向。
- `ArtSource/References/` 已有奶蛙参考图片、视频、网页与建模简报；已检查 35 张图片、12 段视频并确认视频均有音轨，尚未形成独立音效。造型与动作参考不代表已有三维模型或骨骼动画，音轨检测不代表已试听或可直接使用。
- AR 角色出现在摄像头观察的真实桌面中，不是安卓悬浮窗桌宠。

## 已确定的产品范围

基线交付包括：

1. 一张宠物小窝卡定位角色，正确处理跟踪丢失与恢复。
2. 小窝卡加一张食物卡的实体投喂。
3. 触屏逗弄和虚拟平面内的简单追球。
4. 饥饿、开心、困倦等状态对应的行为反馈。
5. 少量趣味反应解锁、本地存档和拍照留念。
6. 手机和平板界面适配，以及实际设备支持情况的如实记录。

首版使用 1 个角色，先验证待机与进食，再扩展约 5～6 类基础动作。精确还原参考角色的全部外形、表情与动画不是默认验收条件。

应用内视频导出、AI 对话、语音、多人协同、云端服务、手部追踪、真实环境遮挡、任意物体识别和整屋漫游均为后续可选项。开发任务应优先完成上述基线。

## 技术决策

- 引擎：Unity 6 LTS（用户于 2026-09-08 明确确认）；选择 Vuforia 官方明确支持的具体发行版与补丁版本，通过真机样例后锁定。
- 跟踪：Vuforia Engine Image Targets。先单目标，再验证最多两个同时跟踪的目标。
- 逻辑：C# 行为状态机；界面使用 uGUI。
- 动画：Animator 与 Generic 骨骼类型。非标准萌宠不默认使用 Humanoid 自动重定向。
- 配置：ScriptableObject；持久化：本地 JSON。
- 资产：轻量三维角色与二维表情、特效结合；导出 FBX 和贴图供 Unity 使用。
- 安卓：Android Build Support 及编辑器配套 SDK、NDK、JDK；按 SDK 要求设置 ARM64 构建。
- 渲染管线随通过验证的示例锁定。若采用 URP，核实摄像头背景和材质兼容。

不要依据 Android 15、处理器型号或“能安装 APK”推定 ARCore、双目标跟踪或全部 AR 功能可用。前期在 Google 列表中未查到 iQOO Z9 Turbo 这一名称，当前选择图像跟踪，但其实际可用性仍需测试。

Unity 环境按新建 Unity 6 LTS 环境准备，并安装配套 Android Build Support、SDK、NDK、OpenJDK。用户要求忽略本机已有 Unity 安装，后续不以旧安装的版本、模块或路径作为本项目的选型依据，也不反复检查旧安装。

2026-09-08 复核的其他工具：Blender 5.1.1 位于 D:/blender.exe；Android Studio 2026.1、VS Code 1.136.1 已安装。独立 Android SDK 位于 D:/DevTools/AndroidSdk，含 API 36、Build Tools 与 Platform Tools 37.0.0，但该 SDK 根下未见 NDK。Unity 6 LTS 构建优先使用所选编辑器配套工具链。具体路径、证据与待配置项见 [技术栈与开发环境](Docs/技术栈与开发环境.md)，后续从已知位置核查，不反复全盘扫描。

Android 应用主体在 Unity 工程中开发并打包；Android Studio / adb 用于调试，原生插件按实际需要接入。当前组合已锁定为 Unity `6000.0.83f1`、Vuforia Engine `11.4.4` 与 Built-in 渲染管线；真机验证若发现兼容问题再重新评估。不将历史 Vuforia 9.0 材料作为当前版本依据。

## 用户已明确的工作方式

- 用户要求助手直接调用 D:/blender.exe 与 Python/bpy 制作、检查和导出资产；不使用用户已要求忽略的 blender-asset-package skill。2026-09-10 已完成模型、绑定、两个动作、FBX 导出和 Blender 回读验证；Idle / Eat 已接入 Unity，Idle 已随小窝卡完成基础真机显示验证，Eat 触发仍待真机验证。
- 2026-09-10 用户将同时尝试外部网站或软件生成模型，候选文件放在 `ArtSource/Characters/Pet01/Candidates/网站名_版本/`。优先接收完整 GLB，也接受 BLEND、FBX 或 OBJ + MTL + 贴图；可提供未绑定模型。按候选实际造型和网格质量决定后续修整与绑定，目录说明见 `ArtSource/Characters/Pet01/Candidates/README.md`。
- 搜索文件和文本优先使用 rg，查找应限定在相关目录。
- 在用户当前任务范围内推进文档、代码和验证，不重复询问已经确认的选题与技术方向。
- 遵循当前文件系统和工具权限；必要的权限请求说明具体路径与原因。权限失败不等于模型或功能已生成。
- 保留本目录已有课件、白皮书和合并文档；在独立子目录建立工程与新增资产。
- 不声称完成尚未执行的构建、模型生成、Unity 导入或真机验证。

## 建议目录结构

以下是当前实际结构（2026-09-11 已建立目录与代码骨架）。源资产、Unity 导入资源、可选 Android 插件与打印标记的详细分工见 [技术栈与开发环境](Docs/技术栈与开发环境.md)：

~~~text
项目根目录/
  .gitignore                        排除 Library/Temp/Logs/Obj/Gradle 导出
  AGENTS.md
  开题说明_AR萌宠互动系统.md
  三份文档合并.md
  原始课件与白皮书
  ARPet/
    README.md                       工程结构与三层可运行程度
    SETUP.md                        首次打开、生成场景、接入 Vuforia 的步骤
    Assets/_Project/
      Scenes/                       01_TrackingSmoke（模拟）、02_Main（AR）—— 已生成并接线
      Scripts/
        Core/                       共享契约：接口、枚举、只读结构（不依赖任何程序集）
        AR/                         ① 跟踪层：位姿、有效性、坐标换算、Vuforia 适配
        Interaction/                ② 交互判定层：投喂、逗弄、事件管道
        Pet/                        ③ 行为状态机层：状态数值、优先级、唯一结算点
        Presentation/               ④ 表现层：Animator、表情、音效（不依赖 Pet）
        UI/                         ⑤ 界面层：安全区、输入隔离、跟踪提示
        Persistence/                ⑥ 数据层：版本化 JSON、异常恢复、写盘节流
        Platform/                   ⑦ 平台层：权限、生命周期、相册
        App/                        装配层：AppRoot 唯一组装点
        Editor/                     编辑器工具：一键生成配置与场景、设置检查
        Tests/EditMode/             规则测试：重复结算、优先级、阈值边界、存档恢复
      Art/Characters/Pet01/         角色 FBX、贴图、final_manifest.json
      Art/UI、Art/VFX/              二维表情与特效（待做）
      Animations/                   Pet01Controller（Idle / Eat 已接入）
      Audio/                        音效（待做）
      Prefabs/                      预制体（待做）
      Configs/                      PetBehaviorConfig.asset + JSON 模板
    Assets/StreamingAssets/         Vuforia 设备数据库运行期读取位置
    Assets/Plugins/Android/         按需接入的原生插件
    Packages/                       manifest.json（+ 首次打开时生成 lock）
    ProjectSettings/                ProjectVersion.txt（+ 首次打开时生成其余设置）
  ArtSource/
  Tools/Blender/
  Docs/
  Builds/Android/
~~~

Blender 源文件放在 ArtSource，导出的 FBX 与贴图进入 Unity 工程。避免依赖 Unity 自动调用本机 Blender 导入源文件。提交源码时保留 Assets 的 meta 文件及工程配置，排除 Library、Temp、Logs 等可重建目录。

**分层依赖由 `.asmdef` 强制，不靠约定**：`Core` 不依赖任何东西；`Presentation` 不依赖 `Pet`（通过 `Core.IPetPresenter` 反向驱动）；`AR` 只依赖 `Core`；只有 `App` 知道各层如何拼装。详见 [项目架构与技术架构](Docs/项目架构与技术架构.md)。

## 模块职责与实现约束

### 跟踪与坐标

- 跟踪层只提供目标标识、位姿、有效性和更新时刻，不直接结算游戏数值。
- 使用小窝局部坐标定义宠物、食物判定和追球区域，统一米制单位。
- 图像配置尺寸必须对应实际印刷图像尺寸；不要把纸张外框尺寸误当成有效目标尺寸。
- 丢失跟踪时暂停新空间交互，不根据过期位置继续计时或投喂。
- 标记移出镜头后的持续世界跟踪、真实物体遮挡和环境重建不是默认能力。

### 交互与行为

- 实体投喂要求相关目标均有效，结合距离、高度、停留时间、冷却和离开后再次进入判定。
- 同一次有效进入只结算一次；跟踪恢复不自动生成新的投喂事件。
- 饱腹、精力等状态参数与进食、移动等执行中行为分离，明确打断规则和行为优先级。
- 用动画事件或完成回调衔接结算，避免同一结果由 Update 和动画事件重复执行。
- 界面输入不得穿透到角色；触屏逗弄如实标记为触屏输入，不称为现实手部接触。
- 追球限制在标记定义的虚拟平面与范围内，不默认引入真实环境导航。
- 参数与反应条件可配置，避免在多个脚本重复硬编码相同阈值。

### 资产与表现

- 用户于 2026-09-10 改以 `Docs/AssetReview/20260908_naiwa/expression_frames.jpg` 中的奶蛙形象为标准，并明确要求肚子更胖、更圆润丰满。当前身体比例以该图中的站姿帧 `ArtSource/References/20260908_naiwa_assets/video_frames/mfstream_t1.png`、`mfstream_t6.png` 为主要对照，增加胸腹宽度、侧面鼓度与下腹饱满度；其余帧补充表情和动作。2026-09-08 选定的 `milkyfrog_official/milkyfrog-hero.png` 保留作历史参考。
- 第一轮只验证轮廓、骨骼、待机、进食及 Unity 导入；通过后再扩展动作。
- 2026-09-10 腹部修订后的资产高约 0.2 m、20,528 三角面、26 根骨骼，每顶点最多 3 个骨骼影响。腹部设计截面宽约 0.132 m、厚约 0.110 m，随体型更新腹部色块、双臂位置和肩部过渡，并修正进食时前臂牵拉腹部的问题；修改前版本保存在 `ArtSource/Characters/Pet01/Archive/20260910_before_belly_revision/`。Idle 为 3.0 s 循环，Eat 为 3.2 s 单次；2.4 s 的 Consume 仅为设计标记，当前由行为层按 3.2 s 时长结算一次，Unity 动画事件尚未接入。Idle / Eat 已导入 Unity；TouchReact / DozeLoop 已制作但尚未接入，WalkLoop 仍未通过穿地检查，Happy 已取消。
- **2026-09-11 用户确认当前版本为最终版本**，形体、骨骼与两段动画冻结；后续默认只做 Unity 导入、材质重建与表现接入。任何形体改动按 [奶蛙最终版资产说明](Docs/奶蛙最终版资产说明.md) 第 7 节先归档再改。三个补充行为仍由 `PetConfig.Aspect.HasAnimatorClip` 显式关闭，动画接入并通过各自验收后才允许进入状态机；Happy 已取消且代码中没有对应行为。
- 检查尺寸、朝向、脚底根节点、权重、动画循环与过渡。角色约 20 厘米只是起始设计值。
- 二维资源可作为表情、特效、动作参考或二维角色方案；不能当作已获得的三维骨骼动画。
- 记录可编辑源文件、导出文件和素材来源，渲染用的灯光、相机、地台不应误作为角色一起导入。
- 如果调整角色还原度或采用二维表现，记录原因并同步修订开题文档。二维方案不会修复 AR 跟踪兼容问题。
- 少量短音效处于后续表现增强评估阶段，独立于模型保存并由 Unity 事件触发，不作为首轮建模验收项。缺口、建议清单与检查证据见 [奶蛙建模与音效资源核查](Docs/奶蛙建模与音效资源核查.md)。

### 数据与移动端

- 使用稳定标识和带版本号的 JSON 保存状态、解锁内容，避免保存场景对象引用或依赖临时世界坐标。
- 缺失和异常存档应有可用的恢复路径。
- 处理摄像头权限拒绝、切后台、重新进入、拍照保存失败等真实使用路径。
- 手机和平板布局应考虑屏幕比例和安全区域；未实测的平台标记为待验证。

## 开发顺序

1. 核实 Unity、Vuforia、Android 工具链与开发密钥配置，记录具体版本。
2. 构建并在 iQOO Z9 Turbo 上运行最小单目标样例。
3. 验证第二张食物卡、丢失恢复和一次性投喂判定。
4. 完成两个动作的角色资产导入，串联状态与动画。
5. 补齐逗弄、追球、反应解锁、存档和拍照。
6. 完成性能与交互测试，再整理报告和演示材料。

跟踪未通过时优先排查初始化、摄像头、版本与配置。三维资产耗时过大时评估降低还原度或二维表现；若改变基线交互方式，如由实体投喂改为触屏投喂，必须在文档和结果说明中明确变化。

## 验证与交付

- 对纯业务规则进行必要验证，重点覆盖重复结算、行为优先级、阈值边界和存档恢复；不为简单文案或低影响修改机械添加测试。
- 编辑器测试之外，实际记录手机型号、系统、版本、卡片尺寸、光照和测试距离。
- 参考开题稿验证重复识别、双目标交互、遮挡恢复、权限与后台恢复、拍照及存档。
- 以约 30 FPS 为初步性能目标，通过真机连续互动测量；不将目标当作结果。
- 小规模体验反馈只作探索性记录，不虚构满意度、性能数字或统计结论。
- 报告结果时区分“已实现”“已测试”“目标”“待验证”，说明材料限制和剩余问题。
- 最终交付 APK、Unity 源码、源资产与导出资源、打印标记、使用说明、测试记录和演示材料。

## 参考入口

- 同目录：开题说明_AR萌宠互动系统.md、三份文档合并.md。
- **架构与施工（2026-09-11，最新）**：[Docs/项目架构与技术架构.md](Docs/项目架构与技术架构.md)（七层架构、程序集依赖、关键设计、各层现状）与 [Docs/环境缺失与下载清单.md](Docs/环境缺失与下载清单.md)（要下载什么、什么版本、从哪拿）。工程入口：[ARPet/README.md](ARPet/README.md)、[ARPet/SETUP.md](ARPet/SETUP.md)。
- 开题交付（2026-09-09）：正式版 [Docs/开题报告_AR萌宠互动系统.md](Docs/开题报告_AR萌宠互动系统.md) 与答辩幻灯 [Docs/开题报告_桌上有龙_答辩.pptx](Docs/开题报告_桌上有龙_答辩.pptx)（16 页，已过视觉审查；汇报人信息待按学校模板补充）。逐页演讲稿见 [Docs/开题答辩_演讲稿.md](Docs/开题答辩_演讲稿.md)（约 8.5 分钟版 + 5 分钟精简版 + 问答预案）。
- 市场调研：[Docs/市场调研_AR萌宠产品横向对比.md](Docs/市场调研_AR萌宠产品横向对比.md)（2026-09-09，竞品事实与定位结论，引用前按文中"待核实项"复核）。
- 奶蛙建模参考：[ArtSource/References/20260908_naiwa_assets/建模参考简报.md](ArtSource/References/20260908_naiwa_assets/建模参考简报.md)（比例、色彩、分部位要点，供建模使用）。
- 奶蛙最终版资产（2026-09-11）：[Docs/奶蛙最终版资产说明.md](Docs/奶蛙最终版资产说明.md)（冻结记录、Unity 导入步骤、解冻流程）与 [本地预览](ArtSource/Characters/Pet01/review.html)。
- 奶蛙首轮制作过程：[Docs/奶蛙首轮资产与动作说明.md](Docs/奶蛙首轮资产与动作说明.md)（2026-09-10；首轮模型、骨骼、两个动作与腹部比例修订）。
- Vuforia Supported Versions：https://developer.vuforia.com/library/vuforia-engine/platform-support/supported-versions/
- Vuforia Recommended Devices：https://developer.vuforia.com/library/vuforia-engine/platform-support/recommended-devices/
- Vuforia Image Targets：https://developer.vuforia.com/library/vuforia-engine/images-and-objects/image-targets/image-targets/
- ARCore Supported Devices：https://developers.google.com/ar/devices

课程材料中的 Vuforia 9.0 导入说明属于历史参考，不能直接据此混用新旧编辑器和 SDK。实施时记录并遵循已验证的版本组合。
