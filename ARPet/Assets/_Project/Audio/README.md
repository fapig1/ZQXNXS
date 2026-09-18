# Audio — 音效

**当前目录为空。** 少量短音效属于后续表现增强阶段，独立于角色模型保存，
**不是首轮建模验收项**。

## 现状

- `ArtSource/References/20260908_naiwa_assets/` 下 12 段参考视频均检测到可解码音轨，
  但**尚未试听、筛选或剪辑**，因此不能算已有音效。
- 缺口与建议清单见 [Docs/奶蛙建模与音效资源核查.md](../../../../Docs/奶蛙建模与音效资源核查.md)。

## 计划接入方式

- 原始音源与来源/许可记录放 `ArtSource/Audio/`；应用实际使用的短音效放本目录。
- 由 Unity 事件触发，走 `IPetPresenter.PlaySfx(string sfxId)`。
  `PetPresenter` 用 `Resources.Load<AudioClip>(audioResourcesPrefix + sfxId)` 查找，
  默认前缀 `Audio/`，因此放在 `Assets/_Project/Audio/Resources/Audio/` 下，
  或把 `audioResourcesPrefix` 改成实际路径。
- 缺资源时 `PlaySfx` 安全忽略，不影响主流程。

## 候选音效位点

| sfxId | 触发时机 | 优先级 |
| --- | --- | --- |
| `eat_bite` | Eat 的三次咀嚼（约 0.95 / 1.45 / 1.95 s） | 中 |
| `eat_swallow` | Eat 的吞咽结算点（2.4 s） | 中 |
| `ui_tap` | 界面按钮 | 低 |
| `pet_happy` | 逗弄反应（动作尚未制作） | 低 |

**注意**：三次咀嚼只做表演，**不逐口增加饱腹值**；音效不得被当成结算信号。
