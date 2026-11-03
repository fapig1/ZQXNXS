# Configs — 可调参数

> 2026-10-27：运行期已退出食物链（食物卡投喂、饥饿、精力）。`PetConfig` 现在只有开心值一个数值。
> 同日定稿 P3 规则：单击 / 双击分别加分、开心值**不随时间自然衰减**（在线与离线都为 0，衰减代码路径保留）、
> 满值直接播四色正式专属片段。四色逐项数值见下方调参表；范围说明见
> [范围v0.3](../../../../Docs/交互与皮肤扩展设计.md)。

| 文件 | 类型 | 说明 |
| --- | --- | --- |
| `PetBehaviorConfig.asset` | ScriptableObject | **运行时真正读取的配置**。由菜单 **桌上有龙 / 2. 只生成默认配置资产** 创建。 |
| `pet_config.template.json` | 模板 / 说明 | 字段说明与默认值清单，供人工核对与评审。**不是运行时数据源。** |
| `PetExclusiveAnimations.asset` | ScriptableObject | 四形象到Animator状态和真实FBX Clip的映射；供 `03_ExclusiveAnimationPreview` 开发预览读取。满值业务触发走 `VariantTunings` 的 `ExclusiveStateName`，不读该资产。 |

两份文件职责不同：现有 `PetBehaviorConfig.asset` 由 Unity 编辑器生成并被运行时读取；JSON 是供人工核对的模板。修改默认值时检查 `PetConfig` 代码默认值、序列化资产与模板三处一致，不能只改说明就宣称运行值已变化。

## 归属规则

**可调阈值集中配置，避免业务脚本重复硬编码。** 当前默认值由 `PetConfig` 定义，实际资产可能覆盖默认值。需要检查的问题包括：

- 同一个数值（例如开心值增量）在多个脚本里各写一遍；
- 改一个手感参数需要动 `.cs` 文件；
- 界面上显示的数值与结算用的数值不是同一个来源。

## 参数清单与状态

| 参数 | 默认值 | 状态 |
| --- | --- | --- |
| 开心值初始值 | 0.60 | 设计初值 |
| 开心值每秒变化 `DecayPerSecond` | 0 | **2026-10-27 定稿：不衰减**（在线 / 离线都是 0；`ApplyDecay`、`ApplyOfflineDecay` 代码路径保留） |
| 离线补算上限 `MaxOfflineSeconds` | 12 小时 | 保留；衰减为 0 时不产生效果 |
| 动画过渡时间 | 0.15 s | 设计初值 |
| 位姿过期阈值 `staleSeconds` | 0.35 s | **待真机调整**（由跟踪源提供，不在 PetConfig 中） |

### 四色调参表 `VariantTunings`（按 `PetVariantId` 索引）

| 参数 | classic | calm | rage | divine |
| --- | --- | --- | --- | --- |
| 单击加分 `SingleTapGain` | 0.06 | 0.04 | 0.08 | 0.06 |
| 双击加分 `DoubleTapGain` | 0.10 | 0.07 | 0.12 | 0.10 |
| 满值演出后回落 `FallbackHappiness` | 0.70 | 0.75 | 0.65 | 0.65 |
| 重新武装阈值 `RearmThreshold`（≤） | 0.80 | 0.80 | 0.85 | 0.75 |
| 满值冷却（秒，从专属**开始**算） `ExclusiveCooldownSeconds` | 8 | 8 | 6 | 12 |
| 专属动画状态名 `ExclusiveStateName` | classic_proud | calm_zen | rage_roar | divine_awaken |
| 专属时长（秒） `ExclusiveDurationSeconds` | 2.0 | 2.5 | 2.0 | 2.5 |

纪律：

- 单击 / 双击加分只在行为状态机**接受事件的那一处**结算一次；双击只结算双击值，不叠加单击。
  `Petted` 行为配置里不要再写 `OnEnter` 加分（旧版 +0.06 已删除）。
- `FallbackHappiness > RearmThreshold` 是非法组合：运行时会记一条警告并按阈值处理，避免永远无法重新武装。
- 冷却只随行为时钟（`BehaviorStateMachine.Tick`）递减；跟踪丢失时行为时钟冻结，冷却随之冻结。
- 读档不因 100% 自动开始：`Happiness ≥ 1` 且周期为 Armed / PendingAuto 时改为 PendingConfirm，
  玩家点一下确认才播；已消费的资格不返还，被打断不计"看完"。
- 调参表缺某个变体时回退 classic 的数值并只警告一次；连专属片段也不可用时满值只停在 100%，不消费周期。

### Animator 能力清单 `AnimatorStates`

`AnimatorStates` 是**行为可用性的唯一判据**（Animator Controller 里真实存在的状态名）。
默认包含 `Idle`、`TouchReact`、`classic_proud`、`calm_zen`、`rage_roar`、`divine_awaken`、`social_laugh`；
**不含** `DozeLoop`、`WalkLoop`、`Eat`，因此 `Resting` / `Chasing` / `Eating` 依然不可进入。
缺片段只禁用对应行为，不影响其他行为。`Exclusive` 的动画名与时长取本变体调参表，
行为表里同名字段不参与可用性判定。

## 存档字段（P3 新增，schema 仍为 2）

`PetSaveEntry` 新增四个字段：`HappyCycle`（周期状态，int）、`ExclusiveCooldownRemaining`、
`ExclusiveStartCount`、`ExclusiveWatchedCount`。旧档缺这些字段时读成默认值（Armed / 0 / 0 / 0），
文件名与 schema 版本不变；`SaveService` 负责范围校验（未知枚举 → Armed，负数 → 0），
冷却上限由变体调参表在运行层收紧。

**这些数值一个都还没有经过真机体验验证。** 调整后请同步更新 `pet_config.template.json`，保持两份记录一致。

## 已删除（2026-10-27）

- 食物卡投喂判定参数：最大水平距离、最大高度差、所需停留、冷却、再进入距离（原 `PetConfig.EatConfig`）；
- 饥饿阈值 `HungryThreshold`、困倦阈值 `SleepyThreshold`；
- 饥饿 / 精力初值与每秒衰减；`Eating`（Eat）行为定义；
- `Petted` 的进入加分（+0.06）：改由四色调参表的单击 / 双击加分结算；
- `PetConfig.Aspect.HasAnimatorClip` 硬编码白名单：改由 `AnimatorStates` 能力清单判定。

旧存档字段（`PetState.Hunger` / `Energy` / `FeedCount`）仍在结构体中保留，只为 v1 → v2 迁移，不参与运行。
单次行为仍必须非循环且具有有限正时长；默认行为必须为已具备动画的循环行为，不能设为自动重复的单次行为。
