# Prefabs — 预制体

**当前目录为空。** 预制体属于编辑器序列化资产，需要 Unity 生成。

## 计划中的预制体

| 预制体 | 内容 | 依赖 |
| --- | --- | --- |
| `PetRoot.prefab` | `Pet01` 模型实例 + `Animator` + `PetPresenter` | 角色 FBX、材质、Animator Controller |
| `Ball.prefab` | 追球用的小球（轻量网格 + 材质） | 追球玩法实现后 |
| `HudCanvas.prefab` | 状态面板、跟踪提示、反应图鉴入口 | 界面设计稿 |

## 挂点约定

```
DenCard (ImageTarget)
└── PetRoot            ← 角色实际跟随的锚点，不要把小窝卡本身当角色父节点
    └── Pet01          ← FBX 实例（约 0.2 m 高，局部 +Z 朝前）
```

场景里**只放一只主模型**。`Pet01_Idle.fbx` / `Pet01_Eat.fbx` 只用来取 `AnimationClip`，
不要把它们的网格也实例化出来，否则会出现两只角色。

跟踪层与交互层通过小窝卡的局部坐标结算，预制体不需要知道世界位置。
