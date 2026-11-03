# Pet01 Unity导入资源

2026-09-22接入复核。通用源为 `ArtSource/Characters/Pet01/pet01.blend`，已按用户授权修订权重，形体/骨架不变；范围与四色对应关系见 [资产映射](../../../../../../Docs/四色奶蛙资产映射.md)，解冻见 [最终版说明](../../../../../../Docs/奶蛙最终版资产说明.md) 第7节。

- `Models/Pet01.fbx`：通用网格和26骨骼；已设Generic、关闭Root Motion。
- `Models/Pet01_Idle.fbx` / `Pet01_Eat.fbx`：3s循环与3.2s单次，已接Pet01Controller及两个场景。Eat属旧玩法资源。
- `Models/Pet01_TouchReact.fbx` / `Pet01_DozeLoop.fbx`：通过历史制作检查；TouchReact 已于 2026-10-27 接入 Animator（`Petted`，0.7s 非循环），DozeLoop 仍未接。
- `Models/Pet01_WalkLoop.fbx`：文件存在、穿地检查未通过，Chasing仍禁用。
- `Models/Pet01_{classic_proud,calm_zen,rage_roar,divine_awaken}.fbx`：2.0/2.5秒单次骨骼片段，已接Animator、独立开发预览与满值业务触发（2026-10-27），全帧/FBX检查41/41通过；完整演出观感尚未验收。授权权重修订最多8影响，旧Action回归44/45通过，仅原WalkLoop穿地失败。
- `Textures/Pet01_BaseColor.png`：旧场景色彩图集；四色 `Pet01_BaseColor_{classic,calm,rage,divine}.png` 已导入。
- `Materials/Pet01_{Skin,Oral,Eyes}.mat`：旧场景材质；四色对应的12份Standard材质已生成并用于开发预览。
- `asset_manifest.json`：源清单的Unity说明副本；`final_manifest.json`：冻结与额外动作、验证范围记录。当前主源哈希与原冻结基线分别保存。

2026-09-18真机仅验证黄蛙锚定显示。2026-09-22 Windows编辑器EditMode 109/109通过，含四段真实骨骼/蒙皮播放与PlayMode自动回Idle测试；当时 rage/divine张臂与classic收势语义仍待完善，嘴眼/特效、开心值触发未接。详见[接入记录](../../../../../../Docs/四色专属动作接入记录_20260922.md)。2026-09-28新增共享大笑 `Models/Pet01_social_laugh.fbx` 并接Animator状态，EditMode 110/110通过；组协调触发与二维嘴眼未接，见[共享大笑接入记录](../../../../../../Docs/共享大笑动作接入记录_20260928.md)。2026-09-29 全部 11 个 FBX 换成 `20260928_arm_separation`（娃娃关节手臂，12,452 顶点，最多8影响），classic_proud 重做为打招呼、divine_awaken 重做为水平展翅；.meta 与 Animator 状态不变，EditMode 110/110 通过，见[手臂分离修订记录](../../../../../../Docs/手臂分离修订记录_20260929.md)。2026-10-27：TouchReact 接入 Animator，满值触发改播四段正式片段，EditMode 161/161（`TestResults-P3Gesture-20261027.xml`）；嘴眼/特效/音效与接近组共同大笑仍未接，真机未测。

旧进食实际按行为时长3.2s结算；Consume 2.4s只是设计标记，未接Unity动画事件。后续动画完成仅允许一个业务结算来源。

导入和场景变更通过编辑器工具完成并保留.meta；四宠主场景由菜单 5/6 生成，批处理入口 `ProjectSetup.RebuildMainSceneForBatch`，四色重复运行保持幂等。
