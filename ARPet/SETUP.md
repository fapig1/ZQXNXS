# ARPet 首次初始化步骤

> 面向第一次打开本工程的你。**先读 [Docs/环境缺失与下载清单.md](../Docs/环境缺失与下载清单.md)
> 把 Unity 6 LTS、Android 模块、Vuforia、Visual Studio 装好，再回来做这里的步骤。**
>
> 当前 `ARPet/` 的状态（2026-09-18）：**工程已在真实 Unity 6 中编译、测试并产出 Android 基础测试 APK。**
> Unity `6000.0.83f1` + Vuforia Engine `11.4.4`；**98 / 98 EditMode 测试通过**；两个场景与配置资产已生成。
> **Vuforia 前置项已全部完成**：License Key、开发者协议、数据库 `ZQXS`（两张占位图目标，0.1 m）均已配置。
> **2026-09-18 新完成**：`02_Main` 已接入 Pet01、材质、Animator、触屏输入、状态面板、Safe Area 与 EventSystem；
> `autoCreateStubSource` 已明确关闭，场景已加入 Android 构建列表。
> iQOO Z9 Turbo / Android 15 已验证应用启动、相机预览、双目标观察者创建和两张占位卡识别。
> **仍然缺**：Prefab、音效与二维表情资产，以及丢失恢复、长时间双目标互动等完整真机回归。
> 下面第 1、2、4 节的步骤已在 2026-09-16 执行过一遍，保留作为重装或换机时的步骤清单。

| 已生成 | 仍然没有 |
| --- | --- |
| 各文件的 `.meta`、`Packages/packages-lock.json`、`ProjectSettings/*.asset` | Prefab、音效与二维表情资产 |
| `Scenes/01_TrackingSmoke.unity`、`Scenes/02_Main.unity` | Prefab、音效与二维表情资产 |
| `Configs/PetBehaviorConfig.asset` | — |
| Vuforia 11.4.4 已解析并编译；License Key / 协议 / 数据库 `ZQXS` 已配置；iQOO Z9 Turbo 已识别两张占位卡 | 丢失恢复与完整真机交互回归 |
| `Animations/Pet01Controller.controller`、三个 Pet01 材质、两个场景的角色接线 | 三段补充动作接入 Unity |
| `Builds/Android/ARPet-phone-test.apk`（Development、Debug 签名、ARM64、minSdk 29、targetSdk 36） | — |

场景与 Prefab 是**编辑器序列化的资产**，无法在仓库里凭空写出来，因此由第 4 步的菜单一键生成。

---

## 1. 打开工程

1. 确认已按清单装好 **Unity 6 LTS（本机已装 `6000.0.83f1`；按官方支持表不要改用 6000.3.x）** 并勾选 Android Build Support / SDK & NDK Tools / OpenJDK。
2. Unity Hub → **Add** → 选择 `D:\ZengQiangXianShi\ARPet`（**选到 `ARPet` 这一层，不是它的上级目录**）。
3. 打开。首次导入会花几分钟，Unity 会补齐 `.meta`、`packages-lock.json` 与 `ProjectSettings`。

**如果 Unity 提示"版本不一致 / 需要升级"**：点确认继续即可。
然后把 `ProjectSettings/ProjectVersion.txt` 改成你实际安装的版本号，保持仓库记录与事实一致。

**如果 Hub 拒绝在非空目录创建/添加工程**（部分 Hub 版本会这样）：
先把 `Assets/_Project` 临时改名（例如加后缀 `_bak`），在 `ARPet` 下用 Hub 新建一个
**3D (Built-in Render Pipeline)** 空工程，再把 `Assets/_Project_bak` 改回 `Assets/_Project`，
最后把你原来的 `Packages/manifest.json` 覆盖回去。这样做不会丢任何已写的代码。

---

## 2. 确认编译通过

1. 打开 **Window → General → Console**，确认没有 `CS` 编译错误。
   应该能看到 `ZQXNXS.ARPet.*` 共 11 个程序集全部编译成功（含测试程序集）。
2. 打开 **Window → General → Test Runner → EditMode → Run All**。
   四份测试（行为状态机、投喂与数值、跟踪与交互、存档）应当全部通过。
   这些测试覆盖的是重复结算、优先级、阈值边界与存档恢复，是后续改动的主要护栏。

2026-09-12 已运行本地 C# 替身检查：83 项规则/存档测试与 23 项装配/平台检查通过。
这不能替代本节的真实 Unity 编译和 Test Runner，证据见 [代码审查问题与修复记录](../代码审查问题与修复记录.md)。

> 如果 Console 报"未检测到 Vuforia Engine"，**这是预期的** —— 第 5 步装上 SDK 后会自动消失。

---

## 3. 切换构建目标

**File → Build Settings → Android → Switch Platform**。
切换完成后建议顺手设一次：

- **Player Settings → Other Settings → Scripting Backend = IL2CPP**
- **Target Architectures = 只勾 ARM64**（Vuforia 11.2.4 起已移除 armv7 支持）
- **Minimum API Level / Target API Level** 按 Vuforia 官方支持表填写

菜单 **桌上有龙 / 帮助 / 检查工程设置** 会把这些逐条检查一遍并列出还没配的项。

---

## 4. 一键生成配置与场景

执行菜单 **桌上有龙 / 1. 生成默认配置与场景**。它会：

1. 首次创建配置时，把 `Pet01.fbx` 设为 **Generic** 骨架、关闭材质导入、关闭动画压缩与 `Optimize Game Objects`；
2. 创建缺失的 `Assets/_Project/Configs/PetBehaviorConfig.asset`，已有配置保留；
3. 只生成缺失的场景，已有场景及其手工引用不会覆盖；需要切换场景时先显示保存当前修改的提示：
   - `Scenes/01_TrackingSmoke.unity` —— 挂 `StubTrackingSource` 的**模拟场景**，
     可以直接按 Play 验证状态机与界面，**不需要 Vuforia、不需要打印机、不需要手机**；
    - `Scenes/02_Main.unity` —— Vuforia ARCamera、两张 ImageTarget、Pet01、AppRoot、触屏输入与诊断 UI。

模拟场景的 `denProxy` / `foodProxy` 已由生成工具赋值。AR 主场景关闭自动模拟源回退，尚未接入真实跟踪源时会报告配置缺失。

**验证模拟链路**：打开 `01_TrackingSmoke` 按 Play，Console 应出现类似
`[AppRoot] 组装完成。可用行为：Idle, Eating`。
把层级里的 `DenCardProxy` 移到相机前方可见处；把 `FoodCardProxy` 拖到 `DenCardProxy` 附近并停留，
应能看到 Console 打印 `交互 #N Feed from Food ... → 接受`。

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
   装 Input System 时 Unity 会弹窗问是否启用新的输入后端并重启编辑器——**选"否"**。
   本工程当前是 `Active Input Handling = Input Manager (Old)`，`TouchInputBridge` 使用
   `Input.touches`；若切到新后端，Android 触屏输入会静默失效。
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
7. 在 Target Manager 建 **Device** 数据库，上传小窝卡与食物卡图案，下载并导入。
   导入成功的标志是 **Vuforia Configuration → Databases 列表出现库名**（2026-09-16 实测 ZQXS 已出现）。
   **Vuforia 11 已取消"Load Database"勾选框**：配置面板顶部提示写明，场景中启用引用该库的
   ObserverBehaviour（即 ImageTarget）时会自动加载并激活数据库，无需手动勾选；
   "Add Database" 按钮只在手动登记库文件（如放在 StreamingAssets/Vuforia 之外）时才需要。
8. 把 ARCamera 与两张 ImageTarget 放进 `02_Main`
   （菜单 **GameObject → Vuforia Engine → AR Camera / Image Target**）：
   - 挂一个 `VuforiaTrackingSource`（在 AR 场景根物体上）；
   - `denTargetObject` ← 小窝卡 ImageTarget；`foodTargetObject` ← 食物卡 ImageTarget；
   - **两张卡的 Size 必须填"印刷图案的有效尺寸（米）"，不是纸张外框尺寸**；
   - 把 `AppRoot` 的 `trackingSourceBehaviour` 与 `markerRegistryBehaviour` 都指向这个组件。
9. **核对 `VuforiaTrackingSource` 的运行时行为**。该文件的真实实现位于 `#if VUFORIA_ENGINE` 内，
   **2026-09-16 已对着真实的 Vuforia 11.4.4 SDK 编译通过**（编译器接受了
   `ObserverBehaviour.TargetStatus.Status` 与 `Status` 枚举成员名，AR.dll 中可见
   `VuforiaApplication` / `VuforiaBehaviour` / `ObserverBehaviour` 等类型），
   所以**成员名这一层已经不是待验证项**。仍未验证的是运行期行为：
   初始化与恢复时序、Observer 状态实际取值、以及真机跟踪表现。
   直接 `TRACKED` 才允许实体投喂；延伸跟踪和 Limited 不得用于停留结算。

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
**桌上有龙 / 3. 接入角色材质与 Animator** 会：把两份动作 FBX 的默认片段
显式命名为 `Idle`（勾 Loop Time）/ `Eat`（不勾）；创建三个材质并按 Pet01.fbx
的实际子网格顺序赋给 `SkinnedMeshRenderer`；创建
`Animations/Pet01Controller.controller` 并把它接到 `01_TrackingSmoke`
场景里 Pet01 实例的 `Animator` 组件与 `PetPresenter.animator` 字段上。
运行后可在 Console 看到接线完成日志；`02_Main` 使用菜单 **桌上有龙 / 6. 准备
02_Main 真机基础测试**，该步骤会接入 Pet01、三材质、Animator、触屏输入、诊断 UI，
并强制关闭 `autoCreateStubSource`。

## 7. 生成并安装基础测试 APK

运行菜单 **桌上有龙 / 7. 构建 Android 基础测试 APK**。APK 输出到
`D:\ZengQiangXianShi\Builds\Android\ARPet-phone-test.apk`。

当前 APK 已通过 `aapt2 dump badging` 和 `apksigner verify` 静态核验：包名
`com.DefaultCompany.ARPet`、Android Debug 签名、`minSdk=29`、`targetSdk=36`、仅
`arm64-v8a`，并声明摄像头权限。它是 Development 测试包，便于 `adb logcat` 采集日志。
2026-09-18 晚已通过 `adb devices` 识别到设备 `10AE5Y1ZY40017Y`，但安装、摄像头权限、占位卡识别、角色显示和丢失恢复仍需在手机上执行。

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

### 手机基础测试步骤

1. 打印 `ArtSource/Markers/Placeholder/print_test_cards_A4.pdf`，打印选项使用**实际大小 / 100%**，
   不要使用“适合页面”；打印后用尺确认两张有效图案均为 100 mm × 100 mm。
2. 手机开启 USB 调试并连接电脑，确认 `adb devices` 显示设备后执行：

   ```powershell
   & 'D:\APP\Unity\6000.0.83f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' `
     install -r 'D:\ZengQiangXianShi\Builds\Android\ARPet-phone-test.apk'
   ```

3. 打开 ARPet 并允许摄像头权限。初始顶部提示应为 `Point camera at the den card`。
4. 对准小窝卡：提示应变成 `Den card tracked`，Pet01 出现在卡片坐标原点并播放 Idle。
5. 把食物卡与小窝卡放在同一桌面，图案中心距离不超过 100 mm，稳定保持至少 0.6 s：
   Pet01 应播放一次 3.2 s 的 Eat，结束后 Hunger 数值下降。再次投喂前需把食物卡移到
   中心距离大于 160 mm 处，并等待 4 s 冷却。
6. 遮住或移开小窝卡：提示应变为 `Tracking lost - find the den card`；重新对准后应显示
   `Tracking recovered`，且丢失期间不得产生新的投喂。
7. 记录手机型号、Android 版本、光照、观察距离、是否稳定达到约 30 FPS，以及上述每项结果。

当前触屏命中与 UI 隔离链路已接入，但 `TouchReact` 尚未启用，点击角色后没有补充动画不属于本轮失败。

---

## 8. 常见坑

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 一按 Play 就自动投喂一次 | `FoodCardProxy` 初始位置落在判定区内 | 把它移到 0.3 m 以外（生成脚本已经这样放） |
| 角色比例明显不对 | ImageTarget 的 Size 填成了纸张外框尺寸 | 改成**印刷图案**的实际尺寸并重新量 |
| 提示"未检测到 Vuforia Engine" | 三种可能：SDK 未导入、包名不匹配、**`versionDefines` 表达式非法** | 依次查：`packages-lock.json` 里有没有该包；`versionDefines` 的 `name` 是否为 `com.ptc.vuforia.engine`；表达式是否误用了 `[0.0.0,)` 这种空上界写法（改用裸版本号 `"0.0.0"`），并在 Editor.log 里搜 `ExpressionNotValidException` |
| 包文件明明在 `Packages/` 里，Vuforia 却完全没生效 | 在 Unity 已打开时从外部改了 `manifest.json`，Unity 没侦测到 | 关掉工程重新打开；确认 `packages-lock.json` 和 `Library/ScriptAssemblies/VuforiaScripts.dll` 都出现了 |
| 导入 Target Manager 数据库包时报 "Package does not contain valid import settings" | `.dat` 是 Vuforia 专有格式，Unity 没有对应导入器；Unity 6 在包导入阶段对"没有标准导入设置的文件"一律报警（2026-09-16 实测） | **可忽略**。确认 `Assets/StreamingAssets/Vuforia/<库名>.dat/.xml` 与 `Assets/Editor/Vuforia/ImageTargetTextures/<库名>/` 均已落地（`.dat.meta` 只有 guid 属正常），Vuforia Configuration → Databases 列表出现库名即为成功。**Vuforia 11 没有"Load"勾选框**：引用该库的 ImageTarget 在场景中启用时自动加载 |
| 装 Vuforia 后 Unity 弹窗问是否启用新的输入后端 | Vuforia 依赖 `com.unity.inputsystem` | 选"否"。本工程的 `TouchInputBridge` 使用旧版 `Input.touches`，切新后端会让 Android 触屏输入静默失效 |
| 找不到 Editor.log | `%LOCALAPPDATA%\Unity` 是指向 `D:\Cache_Unity` 的符号链接，而目标目录不存在 | 创建 `D:\Cache_Unity` 后**重启 Unity**（日志句柄在启动时打开）。2026-09-16 已修复本机 |
| `Petted` / `Resting` / `Chasing` 永远不触发 | **这是设计如此** | 三段 FBX 尚未接入 Unity（WalkLoop 另有穿地未过），`PetConfig.Aspect.HasAnimatorClip` 显式返回 `false`。接入并验收后再改这个方法解锁 |
| Animator Controller 里 Idle→Eat 之间没有过渡边 | **这是设计如此，不是漏连** | `PetPresenter.PlayBehavior` 用 `Animator.CrossFadeInFixedTime(状态名哈希, ...)` 直接切状态，完全绕开 Controller 自身的过渡图；连一条没有 Exit Time / 条件的边只会让 Unity 打印 "transition will be ignored" 的噪音警告，2026-09-17 已确认并移除 |
| 进食后饥饿度不掉 | 检查 `PetBehaviorConfig.Eating.OnExitSettled` | 结算只发生一次，在 3.2 s 时长处 |
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
