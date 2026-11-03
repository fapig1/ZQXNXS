# Audio — 音效与语音口令回应

更新：2026-10-29。四段开发者**自录配音**已接入（来源经开发者确认，可直接用于课程交付）。源文件在 `ArtSource/00_全部音效_按顺序合集/`（不改动），本目录放 Unity 使用的副本，清单见 [audio_manifest.json](audio_manifest.json)（含源 SHA256）。

| sfxId（文件 `Resources/Audio/<id>.wav`） | 原文 / 源文件 | 时长 | 用在哪 |
| --- | --- | --- | --- |
| `andi` | 安迪.wav | 1.15s | 语音口令"安迪"的回应；classic 满值专属（打招呼）开始时 |
| `gaga` | 嘎嘎爹拉筛.wav | 1.74s | 语音口令"嘎嘎爹拉筛"的回应 |
| `leiting` | 雷霆大笑.wav | 2.36s | 语音口令"雷霆大笑"的回应；共同大笑开始时（整组只播一次） |
| `oula` | 欧拉.wav | 0.87s | 语音口令"欧拉"的回应；rage 满值专属（怒吼）开始时 |

处理：转成 16-bit 单声道（源为立体声），采样率保持 44.1kHz，首尾各 10ms 淡入淡出防爆音。

## 接线

- 播放走 `IPetPresenter.PlaySfx(sfxId)`；`PetPresenter` 用 `Resources.Load<AudioClip>("Audio/" + sfxId)` 加载（有缓存），放到本宠物的 2D `AudioSource`（`spatialBlend=0`，菜单 5/6 自动添加）。同一只宠物新一句会打断上一句，不叠音。
- 哪个时机播哪段由 `Configs/PetBehaviorConfig.asset` 决定：`VoiceCommands[].SfxId`、`SocialSfxId`、`ExclusiveSfxIds`（按 Classic/Calm/Rage/Divine 顺序，calm 与 divine 留空）。改配置不用改代码。
- 缺文件时 `PlaySfx` 只警告一次并忽略，不影响主流程。

## 新增音效

1. 把 wav 放进 `Resources/Audio/`，文件名即 sfxId（英文、无空格）。
2. 在 `PetBehaviorConfig.asset` 对应字段填上 sfxId。
3. 若是新的语音口令，还要改 `StreamingAssets/Voice/kws/keywords_raw.txt` 并运行 `python Tools/Voice/make_keywords.py`，见 [项目使用与调试说明](../../../../Docs/项目使用与调试说明.md) 第 9 节。

单击/双击的触屏反馈、divine 与 calm 的专属演出目前没有配音效。
