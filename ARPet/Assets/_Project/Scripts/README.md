# Scripts — 分层代码

架构说明见 [Docs/项目架构与技术架构.md](../../../../Docs/项目架构与技术架构.md)。
依赖方向由各目录的 `.asmdef` 强制，编译期即失败，不靠约定。

2026-10-27：跟踪/交互契约已从"小窝卡 / 食物卡"槽位枚举泛化为稳定字符串 `TargetId` / `PetId`，
食物玩法相关链路已从运行代码删除。**同日第二片：App 已从单宠过渡改为 N 宠独立运行**
（`App.PetSession` 逐宠持有一份 `Pet.PetRuntime` + `TrackingStateMachine` + 表现层），
`AppRoot` 用 `pets` 数组装配，单宠的 `petId` / `targetId` 过渡字段已删除。
**同日第三片：存档升为 schema 2**（`zqxnxs_save_v2.json`），按稳定 `PetId` 逐只保存开心值与计数；
以 schema 2 为准；没有 v2 时兼容读取旧 v1 迁移（加载不改写 v1，`SaveService.Delete` 会一并删除 v1 遗留文件），读到更高版本 schema 时只读保护、拒绝写盘。
**同日第四片（P3 A）：开心值满值周期落地在纯逻辑层**——`InteractionEvent.Gesture`（单击 / 双击）、
`Pet.HappyCycle`（周期状态 + 冷却 + 开始 / 看完计数）、`PetConfig.VariantTunings`（四色逐项参数）与
`PetConfig.AnimatorStates`（能力清单，替代原 `Aspect.HasAnimatorClip` 硬编码）；开心值默认不再随时间衰减。
**同日第五片（P3 B）：输入端与表现端接线**——`Interaction.TapGestureRecognizer` 每宠一个 0.28s 双击窗口，
`TouchPokeRule` 改 DPI 自适应移动阈值并删除输入侧冷却，`TouchInputBridge` 按 fingerId 跟踪单指，
满值直接播四段正式专属片段，`TouchReact` 与 `PetStatusPanel` 开心值/开心条已接。EditMode 161/161。
**同日第六片（P4 B）：接近组共同大笑接线**——`Interaction.ProximityRule` 只读位姿判"够近 / 已分开 / 已锁"
（进入 0.18m / 退出 0.23m 迟滞、双向高度差 0.035m、法向夹角 20°、停留 0.6s / 0.3s），
`PetSession.TickSocial` 把它接给 `Pet.SocialCoordinator`：整组同会话开演，任一只丢失或切后台整组取消，
开始时锁住组内全部关系对，真实分开 0.3s 后才重新武装。Unity EditMode 223/223（`ARPet/TestResults-P4Social-20261027.xml`）。
**同日第七片（P5 B）：二维嘴眼表情接线**——`Presentation.FaceExpressionTable` 管"形象 × 表情 → 眼格 / 嘴格"，
`PetPresenter.SetExpression` 用 per-Renderer `MaterialPropertyBlock` 写 `_MainTex_ST` 选图集格子（四宠共用材质不串表情），
贴片显示时把本体 Eyes / Oral 槽换成同色皮肤遮盖材质；`PetExpression.Signature` 让专属演出按形象出招牌表情。
`Editor/FaceOverlaySetup` 把 `Pet01_FaceOverlay.fbx` 的两片贴片挂到每只宠物的 Head 骨骼下（菜单 5/6 与批处理入口都会走）。
Unity EditMode 231/231（`ARPet/TestResults-P5Face-20261027.xml`）；离线规则检查 228 passed / 21 failed（同一份已知基线）。
**2026-10-29：语音口令**——`Platform.VoiceKeywordListener` 采麦克风、后台线程用 `SherpaKeywordSpotter`（sherpa-onnx C API P/Invoke）离线识别四句口令；
`AppRoot.SubmitVoicePhrase` → `Interaction.VoiceCommandRule` 给每只在场宠物发 `InteractionEventKind.Voice`；`BehaviorStateMachine.TryAcceptVoice`
按单击加分、3 秒冷却、播口令配置的动作 / 表情 / 音效（`PetBehaviorId.VoiceReply`）；`PetPresenter.PlaySfx` 真正出声。Unity EditMode 255/255（`ARPet/TestResults-Voice-20261029.xml`）。
详见 [当前范围](../../../../Docs/交互与皮肤扩展设计.md)。

| 目录 | 命名空间 | 层 | 说明 |
| --- | --- | --- | --- |
| `Core/` | `ZQXNXS.ARPet.Core` | 共享契约 | 接口、枚举、只读结构。`ITrackingSource` 按 `TargetId` 出采样，`IMarkerRegistry` 按 `TargetId` 查世界位姿，`InteractionEvent` 用 `(PetId, Kind, Id)` 标识；`IAppUiState` 除选中宠物的单值外再暴露 `Pets` 逐行快照。**不依赖任何其他程序集。** |
| `AR/` | `ZQXNXS.ARPet.AR` | ① 跟踪层 | 位姿、有效性、按目标 ID 的注册与过期判断；所有目标共用世界米制坐标系，不再换算小窝局部坐标。只依赖 `Core`。`TrackingStateMachine` 每只宠物各持一个实例。 |
| `Interaction/` | `ZQXNXS.ARPet.Interaction` | ② 交互判定层 | `TouchPokeRule` 判合格点按（命中、同一只、位移 ≤ max(40px, dpi×0.25in)、时长、可交互），`TapGestureRecognizer` 每宠 0.28s 窗口判单击/双击；`ProximityRule` 只读位姿判两卡的进入 / 退出迟滞、双向高度差、法向夹角与停留（按 `PetPair` 的锁：开演时上锁；同时可见拉开到退出区，或任一卡连续不在画面里 ≥2s（LIMITED 不算）且放回时相对位置变化 ≥3cm 才解锁；2026-10-30 起不再有"启动先上锁"）；`VoiceCommandRule` 把一句识别到的口令变成每只在场宠物一个 `Voice` 事件（各自 Id 流）；事件带归属宠物 `PetId`，每宠 Id 严格递增，每次点按只结算一次。 |
| `Pet/` | `ZQXNXS.ARPet.Pet` | ③ 行为状态机层 | `PetRuntime`（一只宠物 = 身份 + 开心值 + 满值周期 + 行为状态机，纯 C#、不引用场景对象）是**唯一结算点**；被别的 `PetId` 的事件调用会直接拒绝。`HappyCycle` 管满值周期的状态 / 冷却 / 计数，`SocialCoordinator` 只管组划分、整组开始/取消与记账（几何由 `ProximityRule` 判），`PetConfig` 的 `VariantTunings` 给四色逐项参数、`Social` 给接近参数、`AnimatorStates` 决定哪些行为可进入。 |
| `Presentation/` | `ZQXNXS.ARPet.Presentation` | ④ 表现层 | Animator、二维嘴眼表情、音效。表情按 `FaceExpressionTable` 选格，格子坐标读 Blender 生成的 `face_overlay_layout.json`，不在代码里另写一份；`SetTrackingVisibility` 只切 `Renderer.enabled`（不 SetActive、不碰 Vuforia Observer/父级 ImageTarget，Animator 继续走），`SetInteractable` 切子树 `Collider.enabled`。**不依赖 `Pet`**，通过 `Core.IPetPresenter` 被反向驱动。 |
| `UI/` | `ZQXNXS.ARPet.UI` | ⑤ 界面层 | 状态面板显示选中宠物的跟踪/行为/开心值；`petListText` 逐行列出各宠变体、跟踪状态与开心值百分比，选中项前加 "▶"；选中宠的 `happinessBar`（Filled/Horizontal）跟随数值；`VoiceHoldButton` 是"按住说话"按钮，经 Core `IVoiceCommandSource` 开关麦克风。只依赖 `Core`。 |
| `Persistence/` | `ZQXNXS.ARPet.Persistence` | ⑥ 数据层 | 版本化 JSON（schema 2，多宠）、异常恢复、写盘节流。`SaveFile.Pets` 按稳定 `PetId` 逐只保存 `Happiness`、三种计数与 P3 的满值周期字段（周期状态 / 冷却剩余 / 开始次数 / 看完次数，缺字段读成默认值，schema 与文件名不变）；`SaveFileV1` 只用于旧档迁移解析（v1 文件读后原样保留）；更高版本 schema 只读保护。 |
| `Platform/` | `ZQXNXS.ARPet.Platform` | ⑦ 平台层 | 摄像头权限、应用生命周期、相册；帧率记录 `PerformanceRecorder`；语音 `VoiceKeywordListener`（麦克风权限、采样、模型解包、识别线程）与 `SherpaKeywordSpotter`（原生库封装，只识别不结算）。 |
| `App/` | `ZQXNXS.ARPet.App` | 装配层 | `AppRoot` —— **唯一**知道各层如何拼在一起的地方（唯一组装点，全局只一份，不复制）；`PetSession` 是多宠运行容器（纯 C#，可在 EditMode 直接测），`TickSocial` 每帧把逐宠位姿交给 `ProximityRule` 再交给 `SocialCoordinator`，`PauseAll` 先取消社交；`TouchInputBridge` 按 `PetHitTarget` 拾取并带上 `petId`，Android 分支按 Began 的 fingerId 跟踪单指。 |
| `Editor/` | `ZQXNXS.ARPet.EditorTools` | 编辑器工具 | 一键生成配置与场景；菜单 5/6 把 `02_Main` 接成四色多宠（四张 `Target_card_*` 各挂一只），批处理入口 `ProjectSetup.RebuildMainSceneForBatch`。命名/材质查找集中在 `PetSceneAssets`；表情贴片、`ZQXNXS/FaceDecal` 材质与每色遮盖材质由 `FaceOverlaySetup` 幂等补齐。 |
| `Tests/EditMode/` | `ZQXNXS.ARPet.Tests.EditMode` | 测试 | 重复结算、优先级、阈值边界、手势识别、满值周期、存档恢复、逐宠丢失/恢复与事件路由，以及接近规则几何 / 迟滞与"靠近→同帧开演→分开再重演"的装配级链路、表情选格与四宠不串表情、贴片挂 Head 与对位（17 份文件；Unity 2026-10-27 全量 231/231，`ARPet/TestResults-P5Face-20261027.xml`；离线规则检查 228 passed / 21 failed 已知基线）。 |

## 依赖规则（改代码前先看）

- 上层可以依赖下层，**下层绝不能反向依赖上层**。
- 想让表现层做点什么，扩展 `Core.IPetPresenter`，而不是让 `Presentation` 引用 `Pet`。
- 想让交互层拿到新的空间信息，扩展 `Core.IMarkerRegistry` / `ITrackingSource`，
  而不是让 `Interaction` 引用 Vuforia。目标用稳定字符串 ID（`card_classic` 等）索引，
  新增一张卡 = 在跟踪源的 `targets` 数组里加一条，不要新增槽位枚举。
- 业务事件归属用 `InteractionEvent.PetId`，去重键是 `(PetId, Kind, Id)`：
  不同宠物的同名事件流互不吞并，同一只宠物的重复 Id 仍然只结算一次。
- 多宠**不复制 AppRoot**：相机、输入采集、存档写入全局各一份，每只宠物的状态收进
  `PetRuntime`（`PetSession` 里的一个槽）。新增一只宠物 = 在 `AppRoot.pets` 加一条绑定，
  并给它挂 `PetHitTarget`（带同一个 `petId`）；不要把业务判断塞回 `MonoBehaviour.Update`。
- 跟踪层只提供目标 ID、位姿、有效性与时间，**不结算开心值**；跟踪失效立即阻止该宠的新交互，
  恢复不伪造"离开/重新进入"，也不重建 `PetRuntime`。
- 业务规则不要写进 `MonoBehaviour.Update`，也不要在多个脚本里重复硬编码同一阈值 ——
  阈值属于 `PetConfig`。

## 尚未实现的接口（占位，不假装已实现）

- `IPetPresenter.PlayFx` / `PlaySfx`：只记录请求，没有粒子与音频资源（后续清单 L11）。
  `SetExpression` 已接二维嘴眼贴片，但贴片不随眼睑 / 嘴骨动作，只在行为切换时换格（L13）。
  `SetInteractable` / `SetTrackingVisibility` 已接成"切 Collider.enabled / Renderer.enabled"，可交互**高亮**仍未做。
- 存档已带上满值周期字段（周期状态 / 冷却剩余 / 开始次数 / 看完次数），单击/双击、满值触发与 Animator 接线已完成；
  **接近组共同大笑与二维嘴眼已接线（Unity EditMode 覆盖），但四宠真机、特效与音效仍未做**；
  `Pets` 条目没有位姿或场景引用（这是有意为之）。
- `Platform.PhotoCapture`：明确返回 `false` 并给出原因，因为合成截图需要跟踪 SDK 与真机验证。
- 能力清单 `PetConfig.AnimatorStates` 已声明 `TouchReact` 与四段专属状态名，Controller 里也已建出这些状态；
  `Resting` / `Chasing` / `Eating` 因为 `DozeLoop` / `WalkLoop` / `Eat` 不在清单里而依然不可进入。
