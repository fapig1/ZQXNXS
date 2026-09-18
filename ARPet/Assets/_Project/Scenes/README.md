# Scenes — 场景

由菜单 **桌上有龙 / 1. 生成默认配置与场景** 生成。

| 场景 | 用途 | 现状 |
| --- | --- | --- |
| `01_TrackingSmoke.unity` | 用 `StubTrackingSource` 验证脚本编译、状态机与界面提示。**不需要 Vuforia、打印机或手机。** | 未生成 |
| `02_Main.unity` | 正式 AR 场景。预留 ARCamera 与两张 ImageTarget 的挂点。 | 未生成 |

场景属于编辑器序列化资产，必须由 Unity 生成，不能在仓库里手写。
生成步骤见 [../../SETUP.md](../../SETUP.md) 第 4 节。

**不要**把预览用的灯光 / 相机 / 地台当成角色资产一起提交。
