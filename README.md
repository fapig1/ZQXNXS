# ZQXNXS · 桌上有龙

增强现实课程设计：基于图像跟踪与行为状态驱动的移动 AR 萌宠互动系统设计与实现。

应用采用 Unity 6 LTS 主工程集成 Vuforia 图像跟踪、C# 互动逻辑与 uGUI 界面，由 Unity 输出 Android APK；Blender 负责角色模型和动画，Android Studio / adb 用于安卓调试。

## 文档入口

| 文档 | 内容 |
| --- | --- |
| [项目架构与技术架构](Docs/项目架构与技术架构.md) | 七层架构、程序集依赖、关键设计决策、各层现状 |
| [环境缺失与下载清单](Docs/环境缺失与下载清单.md) | **要下载什么、什么版本、从哪拿**，以及本机还缺什么 |
| [奶蛙最终版资产说明](Docs/奶蛙最终版资产说明.md) | **最终版冻结记录**、Unity 导入步骤、解冻流程 |
| [技术栈与开发环境](Docs/技术栈与开发环境.md) | 工具分工、本机核查、目录规划、实施顺序 |
| [奶蛙首轮资产与动作说明](Docs/奶蛙首轮资产与动作说明.md) | 首轮制作过程与腹部比例修订 |
| [奶蛙建模与音效资源核查](Docs/奶蛙建模与音效资源核查.md) | 主参考选择、素材缺口、音效清单 |
| [开题说明](开题说明_AR萌宠互动系统.md) / [开题报告](Docs/开题报告_AR萌宠互动系统.md) | 功能范围、技术路线与验收目标 |
| [开发约定](AGENTS.md) | 模块职责与工作方式 |
| [奶蛙模型和动画预览](ArtSource/Characters/Pet01/review.html) | 本地打开，切换视角与播放动作 |
| [ARPet 工程说明](ARPet/README.md) / [首次初始化步骤](ARPet/SETUP.md) | Unity 工程结构与打开步骤 |

## 当前状态

**截至 2026-09-18。**

**已完成：**

- 开题交付：开题说明、正式开题报告、答辩幻灯与演讲稿。
- 参考素材与环境核查：35 张图片、12 段视频已检查；本机工具链已逐项核实。
- **奶蛙角色最终版**：高 0.200 m、10,434 顶点 / 20,528 三角面、26 根骨骼、每顶点最多 3 个骨骼影响，
  Idle 3.0 s 循环与 Eat 3.2 s 单次两段动画。Blender 源资产与 FBX 回读的 32 项检查全部通过。
  **用户于 2026-09-11 确认该版本为最终版本，形体与动画冻结。**
- **ARPet 工程架构骨架**：七层分层 + 装配层 + 编辑器工具 + EditMode 测试，共 11 个程序集，
  依赖方向由 `.asmdef` 强制；核心业务逻辑与规则测试已写入。

**已完成（真实 Unity 与 Android 真机实测）：**

- 环境全部就位：Unity 6.0 LTS `6000.0.83f1`（含 Android Build Support、NDK r27c、OpenJDK）、
  Visual Studio 2022 `17.14.37628.2`（含 C++ 工具链）、Windows SDK `10.0.26100.0`、
  Vuforia Engine `11.4.4`（本地 UPM 包）。**已无 P0 环境缺口。**
- 工程已初始化：11 个程序集编译通过、**98 / 98 EditMode 测试通过（0 失败）**、
  两个场景与 `PetBehaviorConfig.asset` 已由编辑器工具生成且序列化引用正确。
- 版本已锁定：见 [环境缺失与下载清单](Docs/环境缺失与下载清单.md) 第 5 节。
- **Vuforia 前置项全部完成**：License Key 与开发者协议已写入 `VuforiaConfiguration.asset`；
  Target Manager 数据库 `ZQXS`（两张占位图目标，尺寸 0.1 m）已导入并在 Databases 列表识别。
  注意 Vuforia 11 已取消"Load Database"勾选框，ImageTarget 启用时自动加载数据库。
- `02_Main` 已接入 ARCamera、两张 ImageTarget、Pet01、材质、Animator、触屏输入和诊断 UI；
  Android 构建使用 IL2CPP、仅 ARM64、minSdk 29，基础测试 APK 已生成。
- iQOO Z9 Turbo / Android 15 已验证 Vuforia 初始化、两张占位卡各自创建观察者并进入
  `TRACKED -- NORMAL`，Pet01 可正常锚定到小窝卡显示。

**仍然没有（不要当成已完成）：**

- Prefab、二维表情和音效尚未制作；正式标记卡仍未设计。
- TouchReact 与 DozeLoop 已制作但尚未接入 Unity；WalkLoop 仍有 0.29 mm 超判据的穿地问题；Happy 已取消。
- 真机丢失/恢复、长时间双目标互动、触屏完整交互与性能数据尚未完成系统回归；
  标记卡只有占位图（`ArtSource/Markers/Placeholder/`），正式卡未设计。

下一步按 [ARPet/SETUP.md](ARPet/SETUP.md) 做，剩余待办清单见
[项目架构与技术架构](Docs/项目架构与技术架构.md) 第 7 节。

## 目录

规划中，`ArtSource/` 保存可编辑资产，`ARPet/` 保存完整 Unity 应用工程，
`Tools/Blender/` 保存资产脚本，`Docs/` 保存说明、打印标记与测试记录，`Builds/Android/` 保存构建产物。
原有课件与白皮书保留在根目录。
