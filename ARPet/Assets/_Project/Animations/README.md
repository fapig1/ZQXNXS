# Animations — 动画播放配置

Animator Controller、Avatar Mask、Animator Override 等**播放配置**放这里。
角色本体的 FBX 动画在 `../Art/Characters/Pet01/Models/`，不要混放。

## 计划中的 Animator Controller

| Controller | 状态 | 说明 |
| --- | --- | --- |
| `Pet01.controller` | 待创建 | 第 0 层含 `Idle`（默认，Loop）与 `Eat`（单次）两个状态 |

接入要求：

- `Apply Root Motion` **关闭**（两段动画都不含整体位移，Root 固定）。
- `Idle` 勾选 **Loop Time**；`Eat` **不勾**。
- Idle→Eat 过渡初值 **0.12～0.20 s**，Eat 播完回 Idle。
- 首次检查阶段关闭动画压缩与 `Optimize Game Objects`，便于定位问题；通过后再评估优化。

## 状态名必须与代码一致

`PetPresenter.PlayBehavior` 用 `PetBehaviorId` 的名字去 `Animator.HasState` 查状态，
因此状态名必须是 `Idle` / `Eat`（大小写敏感）。
`PetConfig.Behaviors[].AnimationName` 可以覆盖这个默认映射。

**未来扩展时**：`WalkLoop` / `TouchReact` / `Happy` / `DozeLoop` 做好后，
除了在这里加状态，还要把 `PetConfig.Aspect.HasAnimatorClip` 里对应项改为 `true`，
否则状态机仍然不会选择它们（这是有意设计的保护）。

## 结算只能有一处

进食的数值结算目前在行为层按时长（3.2 s）触发。
**如果改用 Animation Event，必须同时关掉行为层的时长结算**，两者只能留一个。
源资产的 `Consume`（2.4 s）标记只是设计元数据，不会自动变成 Unity 事件。
