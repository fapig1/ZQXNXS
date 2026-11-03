# Animations — 动画播放配置

2026-09-22：已生成 `Pet01_classic_proud.fbx`、`Pet01_calm_zen.fbx`、`Pet01_rage_roar.fbx`、`Pet01_divine_awaken.fbx`，每段均为30fps单次片段；Blender全帧检查、逐段FBX回读和旧Idle/Eat/TouchReact/DozeLoop回归通过。Unity接入菜单为“桌上有龙 / 动画 / 接入四色专属动作”，会把四段加入 `Pet01Controller` 和 `PetExclusiveAnimations.asset`。

开发预览场景为 `03_ExclusiveAnimationPreview.unity`，按钮只验证实际 Animator 播放，不经过业务判定；满值业务触发已于 2026-10-27 接通，走的是 `02_Main` 的宠物实例，不是该预览场景。

Apply Root Motion关闭；Idle循环、Eat与TouchReact不循环（TouchReact 0.7s）。行为层传入 `PetConfig.Behaviors[].AnimationName`，`Exclusive` 取该变体的 `VariantTunings.ExclusiveStateName`，不能把枚举名当成所有状态名。

TouchReact/DozeLoop 已有 FBX 并通过历史检查；**TouchReact 已于 2026-10-27 接入 Animator 并由 `Petted` 使用**（第 0 层、非循环、FBX 按 Custom/8 影响导入），DozeLoop/WalkLoop 仍未接、`Resting`/`Chasing` 因不在 `PetConfig.AnimatorStates` 能力清单而关闭（原 `Aspect.HasAnimatorClip` 硬编码已删除）。WalkLoop 穿地未过。旧Happy已取消。四段专属骨骼片段已接入预览；2026-09-28共享 `social_laugh`（2.4s单次）已加为Animator状态，由同一安装菜单写入。四色开心值满值与单击/双击已接，专属表情、二维嘴眼、粒子音效与接近组协调仍待实现。

四色映射与下一阶段见 [资产交接](../../../../Docs/四色奶蛙资产映射.md)。食物玩法退出新版，但原Eat冻结资产仍保留；旧业务3.2s结算，源Consume 2.4s为设计标记。时钟与动画事件只允许一个结算来源。
