# Scripts — 分层代码

架构说明见 [Docs/项目架构与技术架构.md](../../../../Docs/项目架构与技术架构.md)。
依赖方向由各目录的 `.asmdef` 强制，编译期即失败，不靠约定。

| 目录 | 命名空间 | 层 | 说明 |
| --- | --- | --- | --- |
| `Core/` | `ZQXNXS.ARPet.Core` | 共享契约 | 接口、枚举、只读结构。**不依赖任何其他程序集。** |
| `AR/` | `ZQXNXS.ARPet.AR` | ① 跟踪层 | 位姿、有效性、小窝局部坐标换算。只依赖 `Core`。 |
| `Interaction/` | `ZQXNXS.ARPet.Interaction` | ② 交互判定层 | 投喂 / 逗弄 / 追球事件的一次性判定。 |
| `Pet/` | `ZQXNXS.ARPet.Pet` | ③ 行为状态机层 | 状态数值、行为优先级、**唯一结算点**。 |
| `Presentation/` | `ZQXNXS.ARPet.Presentation` | ④ 表现层 | Animator、表情、音效。**不依赖 `Pet`**，通过 `Core.IPetPresenter` 被反向驱动。 |
| `UI/` | `ZQXNXS.ARPet.UI` | ⑤ 界面层 | 状态面板、跟踪提示、安全区适配、输入隔离。 |
| `Persistence/` | `ZQXNXS.ARPet.Persistence` | ⑥ 数据层 | 版本化 JSON、异常恢复、写盘节流。 |
| `Platform/` | `ZQXNXS.ARPet.Platform` | ⑦ 平台层 | 摄像头权限、应用生命周期、相册。 |
| `App/` | `ZQXNXS.ARPet.App` | 装配层 | `AppRoot` —— **唯一**知道各层如何拼在一起的地方。 |
| `Editor/` | `ZQXNXS.ARPet.EditorTools` | 编辑器工具 | 一键生成配置与场景、工程设置检查。 |
| `Tests/EditMode/` | `ZQXNXS.ARPet.Tests.EditMode` | 测试 | 重复结算、优先级、阈值边界、存档恢复。 |

## 依赖规则（改代码前先看）

- 上层可以依赖下层，**下层绝不能反向依赖上层**。
- 想让表现层做点什么，扩展 `Core.IPetPresenter`，而不是让 `Presentation` 引用 `Pet`。
- 想让交互层拿到新的空间信息，扩展 `Core.IMarkerRegistry` / `ITrackingSource`，
  而不是让 `Interaction` 引用 Vuforia。
- 业务规则不要写进 `MonoBehaviour.Update`，也不要在多个脚本里重复硬编码同一阈值 ——
  阈值属于 `PetConfig`。

## 尚未实现的接口（占位，不假装已实现）

- `IPetPresenter.SetExpression` / `PlayFx` / `SetInteractable`：只记录请求，二维表情与特效未制作。
- `Platform.PhotoCapture`：明确返回 `false` 并给出原因，因为合成截图需要跟踪 SDK 与真机验证。
- `PetConfig.Aspect.HasAnimatorClip`：`Petted` / `Resting` / `Chasing` 返回 `false`，
  这些动画未制作，状态机不会切到空状态。
