# ARPet 首次初始化步骤

> **2026-10-27 改版入口（P2 多宠竖切 + P3 手势与满值闭环完成）**：四张正式角色卡已定稿，尺寸约66×94mm，四色素材见 [资产映射](../Docs/四色奶蛙资产映射.md)。源码已完成多宠：`PetSession` + `PetRuntime`、`TargetId`/`PetId` 契约、食物链退出、存档 schema 2（v1 只读迁移）；P3 手势与满值闭环也已接线（每宠 0.28s 双击窗口、四色定稿参数、满值直接播正式片段、TouchReact 与开心条）；P4 接近组共同大笑与 P5 二维嘴眼贴片同日接线（贴片资源由菜单 5/6 自动挂到每只宠物 Head 下）。`02_Main` 已由菜单 5/6（批处理入口 `ProjectSetup.RebuildMainSceneForBatch`）重建为四张 `Target_card_*` 各挂一只对应颜色奶蛙的主场景，仍是唯一构建场景；2026-10-27 真实 Unity EditMode **231/231**（`TestResults-P5Face-20261027.xml`），但**四宠场景未构建 APK、未真机测试**。四色材质与四专属骨骼片段已接独立开发预览（`03_ExclusiveAnimationPreview`）。下文第 1～7 节的重装步骤仍有效，涉及 Den/Food 双卡或单宠接线的段落已明确标为历史。
>
> 面向第一次打开本工程的你。**先读 [Docs/环境缺失与下载清单.md](../Docs/环境缺失与下载清单.md)
> 把 Unity 6 LTS、Android 模块、Vuforia、Visual Studio 装好，再回来做这里的步骤。**
>
> 历史基线（2026-09-18，单宠/两占位目标，已被上方 2026-10-27 状态取代）：**工程已在真实 Unity 6 中编译、测试并产出 Android 基础测试 APK。**
> Unity `6000.0.83f1` + Vuforia Engine `11.4.4`；**98 / 98 EditMode 测试通过**；两个场景与配置资产已生成。
> **旧基线Vuforia前置项已完成**：License Key、开发者协议均已配置；当时 `ZQXS` 已包含四张0.066m角色目标，同时保留两张0.1m旧占位目标兼容旧场景。新四目标接线和真机验证当时待完成。
> **2026-09-18 新完成**：`02_Main` 已接入 Pet01、材质、Animator、触屏输入、状态面板、Safe Area 与 EventSystem；
> `autoCreateStubSource` 已明确关闭，场景已加入 Android 构建列表。
> iQOO Z9 Turbo / Android 15 已验证应用启动、相机预览、双目标观察者创建和两张占位卡识别。
> **截至 2026-09-18 仍然缺**：Prefab、音效与二维表情资产，以及丢失恢复、长时间双目标互动等完整真机回归。
> 下面第 1、2、4 节的步骤已在 2026-09-16 执行过一遍，保留作为重装或换机时的步骤清单。

| 已生成 | 仍然没有 |
| --- | --- |
| 各文件的 `.meta`、`Packages/packages-lock.json`、`ProjectSettings/*.asset` | Prefab、音效与二维表情资产 |
| `Scenes/01_TrackingSmoke.unity`、`02_Main.unity`、`03_ExclusiveAnimationPreview.unity`、`04_FourCardTracking.unity` | Prefab、音效与二维表情资产 |
| `Configs/PetBehaviorConfig.asset` | — |
| Vuforia 11.4.4 已解析并编译；License Key / 协议 / 数据库 `ZQXS` 已配置；2026-09-18 单宠真机识别过两张占位卡 | 四色四宠真机验证、逐宠丢失恢复与 v1→v2 迁移实测 |
| `Animations/Pet01Controller.controller`（Idle、Eat、TouchReact、四专属、social_laugh）、四色材质、`02_Main` 四卡四宠接线 | 两段补充动作（DozeLoop/WalkLoop）接入 Unity |
| `Builds/Android/ARPet-phone-test.apk`（Development、Debug 签名、ARM64、minSdk 29、targetSdk 36） | 四宠场景 APK 尚未构建 |

场景与 Prefab 是**编辑器序列化的资产**，无法在仓库里凭空写出来，因此由第 4 步的菜单一键生成。

---

## 1. 打开工程

1. 确认已按清单装好 **Unity 6 LTS（本机已装 `6000.0.83f1`；按官方支持表不要改用 6000.3.x）** 并勾选 Android Build Support / SDK & NDK Tools / OpenJDK。
2. Unity Hub → **Add** → 选择 `D:\ZengQiangXianShi\ARPet`（**选到 `ARPet` 这一层，不是它的上级目录**）。
3. 打开。首次导入会花几分钟，Unity 会补齐 `.meta`、`packages-lock.json` 与 `ProjectSettings`。

**如果Unity提示版本不一致**：用锁定的 `6000.0.83f1` 打开，先核对Hub选择的编辑器；不要直接升级或把ProjectVersion改成任意本机版本。

**如果 Hub 未能添加工程**：确认使用 Add/Open 而不是 New，并选择含 `Assets`、`Packages`、`ProjectSettings` 的 `ARPet/`。本仓库已经是完整工程，不在原目录新建空工程或临时搬移 Assets。也可用锁定编辑器的 `-projectPath D:\ZengQiangXianShi\ARPet` 打开。

---

## 2. 确认编译通过

1. 打开 **Window → General → Console**，确认没有 `CS` 编译错误。
   应该能看到 `ZQXNXS.ARPet.*` 共 11 个程序集全部编译成功（含测试程序集）。
2. 打开 **Window → General → Test Runner → EditMode → Run All**。
   当前有 17 份测试文件，覆盖行为规则、手势识别与满值周期、存档 v2 与 v1 迁移、逐宠会话与事件路由、触屏输入、接近规则与共同大笑、嘴眼选格与四宠不串表情、四卡/主场景接线（含贴片挂 Head 与对位）、Animator/可见性与专属动作；2026-10-27 批处理报告为 **231/231**（`TestResults-P5Face-20261027.xml`；P4 223/223、P3 161/161、P2 112/112 为阶段证据），新运行以本次 Test Runner 输出为准。
   这些测试覆盖的是逐宠丢失恢复、事件去重、单击/双击互斥、满值周期、优先级、阈值边界与存档迁移，是后续改动的主要护栏。

本地 C# 替身检查入口 `Tools/Validation/run-rule-checks.ps1`：2026-10-27 为 0 编译错误、158 passed / 21 failed；21 条为已知基线（20 条需真实资产/场景，1 条 `IntegrationChecks.Regeneration_PreservesBothExistingSceneFiles` 的 SavePromptCount 1≠3）。
这不能替代本节的真实 Unity 编译和 Test Runner，证据见 [代码审查问题与修复记录](../代码审查问题与修复记录.md)。

> 若新机器尚未恢复 Vuforia 本地 tgz，按第 5 节安装并检查包解析。已配置的本机再报“未检测到 Vuforia Engine”应排查包和宏定义，不能一律当作预期。

---

## 3. 切换构建目标

**File → Build Settings → Android → Switch Platform**。
切换完成后建议顺手设一次：

- **Player Settings → Other Settings → Scripting Backend = IL2CPP**
- **Target Architectures = 只勾 ARM64**（Vuforia 11.2.4 起已移除 armv7 支持）
- **Minimum API Level = 29 / Target API Level = 36**（当前配置与构建准备工具一致；升级时另做兼容验证）

菜单 **桌上有龙 / 帮助 / 检查工程设置** 会把这些逐条检查一遍并列出还没配的项。

---

## 4. 一键生成配置与场景

执行菜单 **桌上有龙 / 1. 生成默认配置与场景**。它会：

1. 首次创建配置时，把 `Pet01.fbx` 设为 **Generic** 骨架、关闭材质导入、关闭动画压缩与 `Optimize Game Objects`；
2. 创建缺失的 `Assets/_Project/Configs/PetBehaviorConfig.asset`，已有配置保留；
3. 只生成缺失的场景，已有场景及其手工引用不会覆盖；需要切换场景时先显示保存当前修改的提示：
   - `Scenes/01_TrackingSmoke.unity` —— 挂 `StubTrackingSource` 的**单宠模拟场景**，
     可以直接按 Play 验证状态机与界面，**不需要 Vuforia、不需要打印机、不需要手机**；
   - `Scenes/02_Main.unity` —— 四色多宠主场景骨架：生成时只有 AppRoot，运行菜单 5/6 后补齐 Vuforia ARCamera、四张平级 `Target_card_*`、每卡下的 `PetRoot_<petId>` + 对应颜色 Pet01 + PetPresenter + PetHitTarget，以及四宠状态面板。

模拟场景只有一个 `card_classic` 代理：`TrackingSource (Stub)` 下挂 `ClassicCardProxy`（`targets[0]` 指向它）。AR 主场景关闭自动模拟源回退，缺少 VuforiaTrackingSource 时会报告配置缺失。

**验证模拟链路**：打开 `01_TrackingSmoke` 按 Play，Console 应出现类似
`[AppRoot] 组装完成，共 1 只宠物：pet_classic；选中 pet_classic。`
模型在 `PetRoot_pet_classic` 下；把 `ClassicCardProxy` 移到相机前方可见处即可看到 Idle。
用鼠标点击角色（Game 视图），Console 应打印 `[AppRoot] 交互 #N Pet for pet_classic ... → 接受`。
单击与双击、开心值满值与专属片段已接；接近共同大笑尚未实现。单宠 `01_TrackingSmoke` 场景也可用来看 TouchReact（0.7s）与满值演出。

> **2026-10-29 补注**：仓库里的 `01_TrackingSmoke` 是 2026-09-17 生成的老文件，序列化仍是 P2 之前的
> `denProxy/foodProxy` 与单宠 `presenter`，直接 Play 会出现"没有配置任何宠物绑定"并最终 0 只可交互。
> 已用菜单 **桌上有龙 / 10. 修复 01_TrackingSmoke 的模拟接线**（批处理入口
> `SmokeSceneRepair.RepairForBatch`）把 `targets[]` 与 `pets[]` 写回现役契约并补 `PetHitTarget`；
> 该工具幂等，可重复运行。场景里没有 `PetStatusPanel`，所以开心值只能看 Console 日志，不是画面。
> 若把整个场景删掉再跑菜单 1，会按新契约生成 `PetRoot_pet_classic` 版本。

---

## 5. 接入 Vuforia

> **本节已于 2026-09-16 按实测流程改写。** 实际使用的版本是 **Vuforia Engine 11.4.4**，
> 装法是**本地 UPM 包（`.tgz`）**，不是把文件解压进 `Assets/`。
> 注意：该 `.tgz` 约 132 MB，超过 GitHub 单文件 100 MB 限制，**已加入 `.gitignore` 不入库**；
> 每个 clone 本仓库的组员都要按下面第 1 步自行拿到包并放到 `Packages/` 下。

1. **拿到包**：Developer Portal → 下载 Unity 版 Vuforia SDK，得到 `.unitypackage`；导入后它会在
   `Packages/` 下留下 `com.ptc.vuforia.engine-<版本>.tgz`（本次为 `com.ptc.vuforia.engine-11.4.4.tgz`，约 138 MB），
   并在 `Assets/Editor/Migration/` 放入它自带的迁移脚本。
2. **在 `Packages/manifest.json` 里登记这个包**（`file:` 路径相对于 `Packages/` 目录）：

   ```json
   "com.ptc.vuforia.engine": "file:com.ptc.vuforia.engine-11.4.4.tgz"
   ```

3. **重新打开工程让它解析**。manifest 的改动**必须重启工程**才会生效；在 Unity 已打开的情况下从外部
   编辑 `manifest.json`，Unity 不一定侦测得到，表现为"包文件明明在，但 Vuforia 就是没生效"。
   解析成功的标志有三条：`Packages/packages-lock.json` 里出现 `com.ptc.vuforia.engine`（`source: local-tarball`）、
   `Library/PackageCache/` 下出现该包、`Library/ScriptAssemblies/VuforiaScripts.dll` 被编译出来。
4. **保持联网**。Vuforia 11.4.4 声明了三个依赖，其中两个本工程原本没有，需要从 Unity 注册表下载：
   `com.unity.inputsystem` 与 `com.unity.nuget.newtonsoft-json`（`com.unity.ugui` 我们已有）。
   装 Input System 时 Unity 会弹窗问是否启用新的输入后端并重启编辑器。
   本工程当前是 `Active Input Handling = Both`（`activeInputHandler: 2`），`TouchInputBridge` 使用旧 `Input` API；
   可以保持 Both，但**不要切成仅 Input System (New)**，否则 Android 触屏输入会静默失效。
5. **确认 `versionDefines` 真的点亮了宏**。两个程序集（`ZQXNXS.ARPet.AR`、`ZQXNXS.ARPet.EditorTools`）
   各自声明 `name = com.ptc.vuforia.engine`、`expression = "0.0.0"`、`define = VUFORIA_ENGINE`。
   该条件仅检测包存在，**不表示兼容所有版本**；具体 SDK 与 Unity 版本仍按官方支持表和真机结果锁定。
   若实际包标识不同，需同步调整两个程序集；宏不会沿程序集依赖自动传播。

   > ⚠️ **表达式不要写成 `[0.0.0,)`。** 2026-09-16 实测：这种"上界为空"的方括号范围会被 Unity 的
   > 解析器拒绝，日志里抛 `ExpressionNotValidException: '[0.0.0,)' is not a valid expression`，
   > **宏不会定义，`VuforiaTrackingSource` 的真实实现会一直被条件编译排除**——包装得再对也没用。
   > 用**裸版本号**（`"0.0.0"` 表示"该版本或更新"），这也是 Unity 自带 asmdef 的写法
   > （例如 render-pipelines.core 用 `"expression": "1.5.0"` 定义 `HAS_BURST`）。
   > 本仓库的本地校验脚本会拒绝这种非法写法，但它无法复现 Unity 的解析器，
   > **表达式是否真正生效仍以真实 Unity 编译日志为准**。
6. 在 `Vuforia Configuration` 里填入 **License Key**。11.4.4 的入口是菜单 **Window → Vuforia Configuration**
   （快捷键 Ctrl+Shift+V），**不在 GameObject 菜单下**（GameObject → Vuforia Engine 子菜单里只有
   AR Camera、Image Target 等创建项，没有 Configuration）。首次打开会自动生成
   `Assets/Resources/VuforiaConfiguration.asset`。另外首次使用必须先在 **Help → Vuforia Engine →
   Show Developer Agreement** 接受开发者协议，否则配置无法修改、引擎也不会初始化（Vuforia 10+ 的流程）。
7. `ZQXS` 库含四个 `card_*` 角色目标和两个旧占位目标。`02_Main` 使用四个 `card_*`（菜单 5/6 接线）；`04_FourCardTracking` 是独立的四卡跟踪实测场景（见第 7 节）。ImageTarget 宽高由 `VuforiaTargetDatabase` 从 XML 读取，换库时只需更新 XML/DAT。不再制作新食物卡。
   导入成功的标志是 **Vuforia Configuration → Databases 列表出现库名**（2026-09-16 实测 ZQXS 已出现）。
   **Vuforia 11 已取消"Load Database"勾选框**：配置面板顶部提示写明，场景中启用引用该库的
   ObserverBehaviour（即 ImageTarget）时会自动加载并激活数据库，无需手动勾选；
   "Add Database" 按钮只在手动登记库文件（如放在 StreamingAssets/Vuforia 之外）时才需要。
8. **把 `02_Main` 接成四色多宠场景**：运行菜单 **桌上有龙 / 5. 接入 02_Main 的四色多宠 Vuforia 接线**，
   再运行菜单 **6. 准备 02_Main 真机基础测试**（或一次性批处理 `ProjectSetup.RebuildMainSceneForBatch`）。
   - 菜单 5 复用/创建 ARCamera，为 `card_classic` / `card_calm` / `card_rage` / `card_divine` 各建一张平级 ImageTarget；
   - 每张卡下挂 `PetRoot_<petId>` + 对应颜色 Pet01 + `PetPresenter` + `PetHitTarget`；目标的**印刷有效尺寸由 XML 写入**（0.066×0.093923 m），不再手填；
   - `VuforiaTrackingSource.targets` 写 4 条，`AppRoot.pets` 写 4 条，`trackingSourceBehaviour` 与 `markerRegistryBehaviour` 都指向该组件；
   - 旧单宠的 `DenCardTarget` / `FoodCardTarget`（连同示例观察者）会被删除，属于历史结构，不再手工接线。
9. **核对 `VuforiaTrackingSource` 的运行时行为**。该文件的真实实现位于 `#if VUFORIA_ENGINE` 内，
   **2026-09-16 已对着真实的 Vuforia 11.4.4 SDK 编译通过**（编译器接受了
   `ObserverBehaviour.TargetStatus.Status` 与 `Status` 枚举成员名，AR.dll 中可见
   `VuforiaApplication` / `VuforiaBehaviour` / `ObserverBehaviour` 等类型），
   所以**成员名这一层已经不是待验证项**。2026-09-18 记录的是单宠初始化、两观察者创建与角色锚定；2026-10-27 的四卡四宠场景**尚未真机实测**。
   当前只有直接 `TRACKED` 才允许该宠交互；延伸跟踪和 Limited 不用于结算。

---

## 6. 角色导入核对

| 检查 | 期望值 | 怎么查 |
| --- | --- | --- |
| 高度 | 约 0.200 m | 放进场景与 Cube（1 m）比 |
| 朝向 | 局部 **+Z** 朝前 | 旋转 0 时应面向摄像机 |
| 上方向 | 局部 **+Y** | — |
| 骨骼数 | 26 根 | 展开 FBX 层级或用 `Root` 子节点计数 |
| 动画 | `Idle` 3.0 s 循环、`Eat` 3.2 s 单次 | 选中 AnimationClip 看时长，Idle 勾 Loop Time |
| 材质 | 3 个：Skin / Eyes / Oral | Base Color 用同一张 sRGB 贴图、Metallic = 0 |

详细步骤与颜色的 Smoothness 初值见
[奶蛙最终版资产说明](../Docs/奶蛙最终版资产说明.md) 第 5 节。

**2026-09-18：以上材质与动画设置已由代码完成，不需要再手工勾。** 菜单
**桌上有龙 / 3. 接入角色材质、Animator 与触屏输入** 会：把两份动作 FBX 的默认片段
显式命名为 `Idle`（勾 Loop Time）/ `Eat`（不勾）；创建三个基础材质并按 Pet01.fbx
的实际子网格顺序赋给 `SkinnedMeshRenderer`；创建
`Animations/Pet01Controller.controller` 并把它接到 `01_TrackingSmoke`
场景里 Pet01 实例的 `Animator` 组件与 `PetPresenter.animator` 字段上。
运行后可在 Console 看到接线完成日志；`02_Main` 使用菜单 **桌上有龙 / 5. 接入 02_Main 的四色多宠 Vuforia 接线** 与 **6. 准备 02_Main 真机基础测试**，菜单 6 会按四个身份补齐对应颜色的 Pet01、材质、Animator、触屏输入、四宠状态面板（含逐宠开心值百分比与选中宠 `HappinessBar`）与 EventSystem，并强制关闭 `autoCreateStubSource`。

## 7. 生成并安装基础测试 APK

运行菜单 **桌上有龙 / 7. 构建 Android 基础测试 APK**。APK 输出到
`D:\ZengQiangXianShi\Builds\Android\ARPet-phone-test.apk`。
**注意：2026-10-27 的四色四宠 `02_Main` 尚未构建 APK，菜单 7 需要先完成菜单 5/6 并用真机验证。**

旧基础测试 APK 已通过 `aapt2 dump badging` 和 `apksigner verify` 静态核验：包名
`com.DefaultCompany.ARPet`、Android Debug 签名、`minSdk=29`、`targetSdk=36`、仅
`arm64-v8a`，并声明摄像头权限。它是 Development 测试包，便于 `adb logcat` 采集日志。
2026-09-18 已在 iQOO Z9 Turbo / Android 15 安装并验证应用启动、相机预览、两占位目标识别和 Pet01 锚定显示。完整丢失恢复、长期互动、性能与新版四卡仍待验证；本次文档清查未重新安装 APK。

### 四卡跟踪实测（开发顺序第2步，2026-09-29）

- 菜单 **桌上有龙 / 8. 生成四卡跟踪实测场景** 生成 `04_FourCardTracking`，并把 `maxSimultaneousImageTargets` 上调到 4（只上调）。
- 菜单 **9. 构建四卡跟踪实测 APK**（或 `pwsh -File Tools/Unity/build-android-apk.ps1 -Method ZQXNXS.ARPet.EditorTools.FourCardTrackingSetup.BuildFourCardTestApk -OutputApk D:\ZengQiangXianShi\Builds\Android\ARPet-four-card-test.apk`）只构建 04 场景，不改默认构建列表。2026-09-29 已构建，未上机。
- 运行时每张卡只在 `TRACKED` 时显示对应颜色的奶蛙；左上角显示每卡状态、获取次数、跟踪秒数和 FPS。日志写入 `/sdcard/Android/data/com.DefaultCompany.ARPet/files/TrackingProbe/four_card_*.csv`（切后台时写盘），用 `adb pull` 取回。
- 安装：`adb install -r Builds\Android\ARPet-four-card-test.apk`（与基础测试包同包名，会覆盖）。
- 记录项：打印实测尺寸与纸张、光照、距离、每卡单独识别（星级≥3 的卡应稳定 TRACKED）、六组两两同框与错认（屏上显示颜色与卡面不符即错认）、四卡同框时的同时跟踪数与最低 FPS、遮挡后恢复、保护套反光。

本机访问 Google Maven 不稳定，工程通过 `Assets/Plugins/Android/settingsTemplate.gradle`
优先使用阿里云 Google Maven 镜像，同时保留 `google()` / `mavenCentral()` 回退。
该镜像只作用于构建后期的 Gradle 依赖下载，**管不到 SDK 检测阶段**。

### 构建不再卡死：sdkmanager 远程清单超时（2026-09-18）

现象：构建永远停在 `Detecting Android SDK`，Editor.log（或 `-logFile` 指定的日志）反复刷
`Still waiting for package manifests to be fetched remotely.`，Unity 进程活着但不推进；
任务管理器里能看到 Unity 调起的 `java.exe ... SdkManagerCli --list` 一直挂着。

原因：本机到 `dl.google.com` 的 TCP 能连上、但请求没有响应。Unity 的 `CheckAndroidSDK`
会对每个 SDK 组件（cmdline-tools / platform-tools / platform / build-tools）各跑一次
`sdkmanager --list`，而 cmdline-tools 用不带超时的 URLConnection 拉远程清单，于是永久等待。

处理：给 sdkmanager 的 JVM 加连接/读取超时。远程清单在几秒内失败后，sdkmanager 会打印
warning，仍然输出本地已安装包列表并以退出码 0 结束，Unity 把它们记为 warning 后继续构建。

```powershell
[Environment]::SetEnvironmentVariable('SDKMANAGER_OPTS',
  '-Dsun.net.client.defaultConnectTimeout=5000 -Dsun.net.client.defaultReadTimeout=5000', 'User')
```

写进**用户级**环境变量后需要**重启一次 Unity Hub**（Hub 只在启动时读环境变量）才对菜单
构建生效；本仓库的 `Tools/Unity/build-android-apk.ps1` 会自己带上这个变量，并做构建前早检
与构建后 APK 核验，推荐直接用它：

```powershell
pwsh -File D:\ZengQiangXianShi\Tools\Unity\build-android-apk.ps1
```

不要再用"临时替换 `sdkmanager.bat` 做 shim"的老办法：Unity 的调用链是
`cmd -> sdkmanager.bat -> java ... SdkManagerCli`，shim 确实能挡住这一步，但它改的是 Unity
安装目录、Unity 升级即失效；上面的环境变量方案不动安装目录。实测**无效**的替代做法还有
`http_proxy` / `https_proxy` 环境变量与 `-Dhttps.proxyHost` / `-Dhttps.proxyPort`（sdkmanager
不理会，仍然永久等待）。

代价：SDK 检测阶段每次 `--list` 约 38 秒，整个检测约 3～4 分钟，日志里会出现若干条
`Warning: Failed to download any source lists!` 和 `Still waiting for package manifests ...`。
它们是预期噪音，只要后面还有 `Android PostProcess task "Detecting Android SDK" took ...`
就说明这一步已经走完。

### 四色多宠手机测试步骤（2026-10-27 起，pending）

> 以下针对菜单 5/6 接线后的 `02_Main` 四色多宠场景。**该场景尚未构建 APK、未真机执行**，全部是待验证步骤，不得写成已通过。
> 旧 Den/Food 双卡 APK 的复现步骤（识别小窝卡、投喂、饥饿下降、4 s 冷却）属于 2026-09-18 历史，已不适用。

1. 打印四张定稿角色卡，规格与文件见 [资产映射](../Docs/四色奶蛙资产映射.md)；按**实际大小 / 100%** 打印，量取有效图案约 66×94 mm。
2. 手机开启 USB 调试并连接电脑，构建并安装四宠场景 APK（菜单 7；包名 `com.DefaultCompany.ARPet`）：

   ```powershell
   & 'D:\APP\Unity\6000.0.83f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' `
     install -r 'D:\ZengQiangXianShi\Builds\Android\ARPet-phone-test.apk'
   ```

3. 首次启动（若设备上存在旧版 v1 存档）：日志应显示已迁移为 v2，`zqxnxs_save_v1.json` 保留未删；面板数值来自 `pet_classic`，其余三只使用初始值（pending）。
4. 对准任一角色卡：应出现对应颜色奶蛙并播放 Idle，`PetListText` 中该宠显示 TRACKED（pending）。
5. 同时放入两张卡，移开其中一张：被移开的宠物立即隐藏，另一只继续显示与播放，不得影响另一只的跟踪状态（pending）。
6. 放回被移开的卡：该宠恢复显示，开心值不重置（pending）。
7. 触屏点击任一宠物：日志应出现 `交互 #N Pet ... → 接受`，且只影响被点中的那只（pending）。单击与双击已接：连续两次合格点按（间隔 <0.28s）应只结算一次双击加分；满值时应只开始一次专属演出并按定稿数值回落；`TouchReact`（0.7s）播放期间新点按被拒绝、不排队（pending）。
8. 观察面板：多宠列表每行显示对应宠物开心值百分比（向下取整），选中宠的开心条跟随数值填充（pending）。
9. 嘴眼表情：被点时只换成 o 嘴；满值专属时 classic 眯眼微笑、calm 闭眼舒展、rage 怒眼怒吼（嘴角不上扬）、divine 纯白发光眼；回到 Idle 时贴片消失、露出原眼嘴；四只同时在场时互不串表情（pending）。
10. 共同大笑：两张卡平放、间距约 15cm、朝向大致一致，停 0.6s 以上：两只应同时播 social_laugh（眯眼大笑），面板显示 "Laughing together!"，开心值不变；移开其中一张，整组立刻停止；分开到 23cm 外再靠近才能再次触发（pending）。
11. 帧率：面板右下角显示最近 5 秒的 FPS。测完取回 CSV：`adb pull /sdcard/Android/data/com.DefaultCompany.ARPet/files/perf`（每行：起始时间、平均 FPS、最慢帧、慢于 30FPS 帧占比、电量、机型、系统）。温升按 `adb shell dumpsys battery` 的 temperature（单位 0.1°C）在开始和结束时各记一次（pending）。
12. 记录手机型号、Android 版本、APK 版本、卡片实际尺寸、光照、观察距离、测试时长与是否稳定约 30 FPS；未实测项保持 pending。记录模板见 [使用说明与演示脚本](../Docs/使用说明与演示脚本.md) 第 3 节。

手势手感、0.28s 双击窗口、DPI 移动阈值（约 450dpi 时约 112px）、接近阈值与嘴眼观感均未在真机上验证，见 [后续修改问题清单](../Docs/后续修改问题清单.md) L8/L12/L14。

---

## 8. 常见坑

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 四宠列表里只有一只 TRACKED，其余一直 SEARCHING | `VuforiaTrackingSource.targets` 缺目标，或目标名/数据库与 XML 不一致 | 跑菜单 5/6；核对四条 `targets` 的 `targetId` 与 `ZQXS.xml` 的 `card_*` 完全一致 |
| 角色比例明显不对 | ImageTarget 的 Size 填成了纸张外框尺寸 | 改成**印刷图案**的实际尺寸并重新量 |
| 提示"未检测到 Vuforia Engine" | 三种可能：SDK 未导入、包名不匹配、**`versionDefines` 表达式非法** | 依次查：`packages-lock.json` 里有没有该包；`versionDefines` 的 `name` 是否为 `com.ptc.vuforia.engine`；表达式是否误用了 `[0.0.0,)` 这种空上界写法（改用裸版本号 `"0.0.0"`），并在 Editor.log 里搜 `ExpressionNotValidException` |
| 包文件明明在 `Packages/` 里，Vuforia 却完全没生效 | 在 Unity 已打开时从外部改了 `manifest.json`，Unity 没侦测到 | 关掉工程重新打开；确认 `packages-lock.json` 和 `Library/ScriptAssemblies/VuforiaScripts.dll` 都出现了 |
| 导入 Target Manager 数据库包时报 "Package does not contain valid import settings" | `.dat` 是 Vuforia 专有格式，Unity 没有对应导入器；Unity 6 在包导入阶段对"没有标准导入设置的文件"一律报警（2026-09-16 实测） | **可忽略**。确认 `Assets/StreamingAssets/Vuforia/<库名>.dat/.xml` 与 `Assets/Editor/Vuforia/ImageTargetTextures/<库名>/` 均已落地（`.dat.meta` 只有 guid 属正常），Vuforia Configuration → Databases 列表出现库名即为成功。**Vuforia 11 没有"Load"勾选框**：引用该库的 ImageTarget 在场景中启用时自动加载 |
| 装 Vuforia 后 Unity 弹窗问是否启用新的输入后端 | Vuforia 依赖 `com.unity.inputsystem` | 保持 `Both`，不要选仅新后端。本工程的 `TouchInputBridge` 使用旧版 `Input` API，仅新后端会让 Android 触屏输入静默失效 |
| 找不到 Editor.log | `%LOCALAPPDATA%\Unity` 是指向 `D:\Cache_Unity` 的符号链接，而目标目录不存在 | 创建 `D:\Cache_Unity` 后**重启 Unity**（日志句柄在启动时打开）。2026-09-16 已修复本机 |
| `Resting` / `Chasing` 永远不触发 | **这是设计如此** | DozeLoop / WalkLoop 尚未接入 Animator，`PetConfig.AnimatorStates` 能力清单不含这两个状态名。接入并验收后会自然解锁；`Aspect.HasAnimatorClip` 硬编码已于 2026-10-27 删除 |
| 点击后没有反应或没有动画 | 该宠不在 TRACKED、落在 UI 上、位移超阈值，或正处于 TouchReact/专属行为中 | 先看 `PetStatusPanel` 与日志原因；`Petted`（TouchReact 0.7s）与专属演出执行期间新点按被拒绝且不排队，属设计 |
| Animator Controller 里 Idle 与其它状态之间没有过渡边 | **这是设计如此，不是漏连** | `PetPresenter.PlayBehavior` 用 `Animator.CrossFadeInFixedTime(状态名哈希, ...)` 直接切状态，完全绕开 Controller 自身的过渡图；连一条没有 Exit Time / 条件的边只会让 Unity 打印 "transition will be ignored" 的噪音警告，2026-09-17 已确认并移除。Eat 现为旧资产 |
| 面板不显示某只宠物 | `AppRoot.pets` 没绑该宠，或它的目标卡未在跟踪源注册 | 查 Console 中 `PetSession` 的“未在跟踪源中注册，已跳过该宠物”报错；重跑菜单 5/6 |
| 每帧都在写存档 | 不应该发生 | `SaveCoordinator` 有 5 s 节流；若真发生请查 `MinIntervalSeconds` |
| 打包在 C++ 阶段失败 | 缺 Visual Studio C++ 工作负载或 NDK | 见[环境缺失与下载清单](../Docs/环境缺失与下载清单.md) 2.3 / 2.4 |
| 构建日志说 APK 有 712.2 MiB，实际文件只有 81.2 MiB | `report.summary.totalSize` 统计的体量与落盘 APK 不是一回事 | 正常，以文件大小为准（2026-09-18 复核，两次构建都这样） |
| 打包永远停在 `Detecting Android SDK`，日志反复刷 `Still waiting for package manifests to be fetched remotely.` | 本机到 `dl.google.com` 连得上但不返回，`sdkmanager --list` 无超时地等远程清单；Unity 的 `CheckAndroidSDK` 每个组件各调一次，于是永久停在第一次 | 设用户级 `SDKMANAGER_OPTS="-Dsun.net.client.defaultConnectTimeout=5000 -Dsun.net.client.defaultReadTimeout=5000"` 并重启 Hub，或直接用 `Tools/Unity/build-android-apk.ps1`；详见第 7 节"构建不再卡死" |

---

## 9. 提交约定

- **提交**：`Assets/**`（含 `.meta`）、`Packages/`、`ProjectSettings/`、代码与文档。
- **不提交**：`Library/`、`Temp/`、`Logs/`、`Obj/`、`.gradle/`、Unity 导出的 Gradle 工程、`*.apk`。
  根目录 `.gitignore` 已配好，正常情况下 `git status` 不会出现这些。
- APK 作为交付产物单独放到 `Builds/Android/`，需要入库时用 `git add -f`。
