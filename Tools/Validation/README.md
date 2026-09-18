# ARPet 本地规则与装配检查

无需下载依赖，使用 PowerShell 7 自带 Roslyn 按 C# 9 编译工程源码，然后用轻量断言/反射运行器执行规则用例。

```powershell
pwsh -NoProfile -File ./Tools/Validation/run-rule-checks.ps1
```

脚本按 `.asmdef` 依赖分别编译，覆盖未安装 SDK 的编辑器路径，以及 Android / Vuforia 条件分支。`versionDefines` 的安装检测条件也会核对。结果写入 [latest-results.json](latest-results.json)，编译文件位于忽略提交的 `Temp/`；每个文件系统用例使用独立目录，清理前检查目录边界。

这些检查包含：

- `Assets` 内的规则/存档测试：去重、连续停留、跟踪丢失、阈值、数值累计、行为时序、备份恢复、保存失败节流。
- `IntegrationChecks.cs`：检查 AppRoot 的组装调用，以及编辑器生成工具、Android 权限、Vuforia 暂停/回调的控制流。
- 分程序集编译：按实际项目引用约束编译，不能通过把所有代码塞入同一程序集掩盖缺失引用。

**限制**：这不是 Unity / NUnit Test Runner，不验证 IL2CPP 或真实设备。`UnityApiStubs.cs`、`EditorApiStubs.cs`、`VuforiaApiStubs.cs` 只提供验证用 API 与调用记录；假场景文件明确写有占位说明，不是 Unity 场景。JSON 使用 `System.Text.Json`，实际 `JsonUtility` 和 SDK 兼容性必须在锁定的 Unity / Vuforia 版本中复核。

所有替身均在 `Tools/Validation`，不会被 Unity 的 `Assets` 编译。测试存档与场景不会写入实际工程场景或玩家存档路径。

逐轮发现与验证边界见根目录 [代码审查问题与修复记录](../../代码审查问题与修复记录.md)。
