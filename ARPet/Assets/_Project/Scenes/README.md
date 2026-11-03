# Scenes — 场景

2026-10-27状态：`02_Main` 已重建为四色多宠主场景（场景内四个 `Target_card_*` 各挂一只；后续改动可跑菜单 5/6 或批处理 `ProjectSetup.RebuildMainSceneForBatch` 幂等复核），P3 手势与满值闭合同日接线（开心条由菜单 6 生成），另有独立四色动画开发预览与四卡实测场景。

| 场景 | 用途与状态 |
|---|---|
| `01_TrackingSmoke.unity` | Stub模拟跟踪（单宠冒烟）；已接Pet01材质、Idle/Eat/TouchReact Animator与输入链 |
| `02_Main.unity` | 唯一启用构建场景。四张平级 `Target_card_*`（尺寸取自ZQXS.xml）各挂 `PetRoot_pet_*` + 对应颜色Pet01 + PetPresenter + PetHitTarget；App 上挂 VuforiaTrackingSource（4 条 targets）与 AppRoot（4 条 pets）；面板含四宠开心值百分比与选中宠开心条。旧 Den/Food 两张占位卡已删除；真机未测 |
| `03_ExclusiveAnimationPreview.unity` | 四色材质和按钮播放四专属骨骼片段；非AR，不经过满值业务判定（业务触发在主场景），完整演出尚未验收 |
| `04_FourCardTracking.unity` | 2026-09-29 由菜单8生成：ARCamera + 四个平级 `card_*` ImageTarget（0.066×0.093923m，取自ZQXS.xml），各挂对应颜色Pet01 Idle；`TargetTrackingProbe` 仅TRACKED时显示并写CSV。不接AppRoot/业务；四卡APK已构建，真机未测 |

02_Main是四色多宠主场景与唯一默认构建场景；满值专属触发与单击/双击已于2026-10-27接线，接近组共同大笑与嘴眼表情仍未接线。ImageTarget宽高一律从ZQXS.xml读取，菜单6不会再把目标写回手写值，也不会触碰04场景。详见 [SETUP](../../../SETUP.md) 与 [四色映射](../../../../Docs/四色奶蛙资产映射.md)。

场景接线应通过Unity工具验证并保存.meta。预览相机、灯光、地台不能误作为角色一起导出。
