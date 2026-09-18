# CLAUDE.md — Claude Code 工作区指引

## 首要规则：以 AGENTS.md 为准

完整的项目约定、模块约束与**当前状态快照**在 [AGENTS.md](AGENTS.md)，每次会话先读它，
再按需读下文列出的专项文档。本文只是速查卡，与 AGENTS.md 或用户最新指示冲突时，以后者为准。
文中"当前阶段"一节容易过时，状态以 AGENTS.md 为准；路径与红线相对稳定。

交流使用中文；不重复询问用户已确认的选题与技术方向。

## 项目一页速览

- 增强现实课程设计"基于图像跟踪与行为状态驱动的移动 AR 萌宠互动系统"，产品暂名**桌上有龙**。
- AR 角色出现在摄像头观察的真实桌面中，**不是安卓悬浮窗桌宠**；不使用 ARCore / AR Foundation。
- 主工程是 Unity 项目 `ARPet/`（七层架构，11 个程序集，依赖由 `.asmdef` 强制），
  输出 Android APK；首要实测设备 iQOO Z9 Turbo / Android 15。
- 角色"奶蛙"三维资产**已冻结为最终版**（`ArtSource/Characters/Pet01/pet01.blend`，26 骨骼，Idle/Eat 两段动画）；
  形体改动必须先按 [Docs/奶蛙最终版资产说明.md](Docs/奶蛙最终版资产说明.md) 第 7 节归档再改。

## 关键路径

| 路径 | 内容 |
| --- | --- |
| `AGENTS.md` | 开发约定与状态快照（单一事实来源） |
| `ARPet/` | Unity 主工程（`README.md` 结构说明、`SETUP.md` 初始化与环境排坑） |
| `ARPet/Assets/_Project/Scripts/` | 七层脚本：Core / AR / Interaction / Pet / Presentation / UI / Persistence / Platform / App |
| `ARPet/SETUP.md` 第 7 节 | 常见坑速查表（Vuforia / 输入 / 构建） |
| `Docs/技术栈与开发环境.md`、`Docs/环境缺失与下载清单.md` | 环境版本依据、版本锁定记录 |
| `Docs/项目架构与技术架构.md` | 七层架构与关键设计 |
| `ArtSource/Markers/Placeholder/` | 两张占位标记卡（1200×1200 PNG，对应 Target Manager 数据库 `ZQXS`） |
| `D:\APP\Unity\6000.0.83f1\` | Unity 编辑器（含 Android Build Support / NDK r27c / OpenJDK） |
| `D:/blender.exe` | Blender 5.1.1（配 `Tools/Blender/` 下的 bpy 脚本） |

## 常用操作

- **搜索**：用 `rg`，并把范围限定在相关目录；不要全盘扫描。
- **Unity 编译与测试**：在 Unity 6000.0.83f1 中打开 `ARPet/`，用 Window → General → Test Runner →
  EditMode → Run All（2026-09-16 实测 83/83 通过）。也可用命令行（本项目未实测，失败就退回编辑器）：
  `"D:\APP\Unity\6000.0.83f1\Editor\Unity.exe" -batchmode -projectPath D:\ZengQiangXianShi\ARPet -runTests -testPlatform EditMode -testResults D:\ZengQiangXianShi\ARPet\TestResults-EditMode.xml -logFile -`
- **编辑器内手工步骤**（Switch Platform、菜单工具、场景接线）需要用户在 Unity 里操作；
  助手可写编辑器脚本（`Assets/_Project/Scripts/Editor/` 已有"桌上有龙"菜单工具）代为执行。
  **不要声称编辑器内的结果已发生**——没跑过就是没跑过。
- **git 提交**：保留 `Assets/**`（含 `.meta`）、`Packages/`、`ProjectSettings/`；
  不提交 `Library/`、`Temp/`、`Logs/`、`Obj/`、`.gradle/`、Gradle 导出与 `*.apk`（`.gitignore` 已配好）。

## 红线（违反会直接浪费返工）

1. **版本纪律**：Unity 固定 `6000.0.83f1`，**不要**建议或改装 `6000.3.x`；Vuforia 固定 `11.4.4`（本地 tgz UPM 包）。
2. `versionDefines` 表达式必须用**裸版本号** `"0.0.0"`；`[0.0.0,)` 会被 Unity 解析器拒绝，宏静默失效。
3. Unity 弹窗问是否启用新 Input System 时选**"否"**——工程用旧 Input Manager，切走会让将来的 `Input.touches` 静默失效。
4. Android 构建设置：IL2CPP、**仅 ARM64**、Minimum API **≥ 29**（Vuforia 硬性要求）。
5. Vuforia 11 的事实：填 Key 的菜单是 **Window → Vuforia Configuration**（不在 GameObject 菜单下）；
   **没有**"Load Database"勾选框，ImageTarget 启用时自动加载；数据库包导入时
   "Package does not contain valid import settings" 警告无害。
6. 图像目标尺寸填**印刷图案的有效尺寸（米）**，不是纸张外框尺寸；配置与实际打印必须一致。
7. **不声称完成未执行的构建、模型生成、Unity 导入或真机验证**；区分"已实现 / 已测试 / 目标 / 待验证"。
8. 不依据 Android 15 或处理器型号推定 ARCore、双目标跟踪或全部 AR 功能可用——一切以真机实测记录为准。
9. 业务规则红线（详见 AGENTS.md"模块职责与实现约束"）：同一次有效进入只结算一次；
   跟踪丢失时暂停新空间交互；表现层不依赖 Pet 层；阈值参数进 ScriptableObject，不散落硬编码。

## 当前阶段（以 AGENTS.md 为准，此节易过时）

- 已完成：开题交付、奶蛙资产冻结、ARPet 七层骨架真实 Unity 验证（编译 + 83/83 测试）、
  Vuforia 前置项全部配置（License Key / 协议 / 数据库 `ZQXS` 占位图目标 0.1 m）。
- 下一步按序：① `02_Main` 场景接线（SETUP 第 5.8 步：ARCamera、两张 ImageTarget、`VuforiaTrackingSource` 引用装配）
  → ② Animator Controller + 角色材质 + Prefab → ③ Android 构建（IL2CPP/ARM64/minSdk 29）与真机单目标 → 双目标投喂
  → ④ 逗弄、追球、解锁、存档、拍照，收尾测试记录与演示材料。
- 完成任何阶段性工作后，**同步更新 AGENTS.md 的状态快照**，不把旧快照当永久限制。
