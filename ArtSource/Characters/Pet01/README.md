# Pet01 奶蛙资产

> 当前制作采用2026-09-22用户授权的权重修订 `20260922_arm_belly_weight_transition`，形体/骨架沿用2026-09-11基线。四段专属骨骼片段已制作，完整演出缺口与验证证据见[接入记录](../../../Docs/四色专属动作接入记录_20260922.md)。
> 完整记录见[奶蛙最终版资产说明](../../../Docs/奶蛙最终版资产说明.md)；制作过程见[奶蛙首轮资产与动作说明](../../../Docs/奶蛙首轮资产与动作说明.md)。

打开 [review.html](review.html) 查看视角、骨骼、表情与视频；打开 [pet01.blend](pet01.blend) 编辑资产，保留同级 `Textures/`。
源模型使用相对路径引用 `Textures/`，移动或复制源文件时一并保留该目录。渲染相机、灯光和地台只保存在源文件里。

## 现状

四色贴图/立绘/定稿卡、源哈希和待接线状态见 [四色奶蛙资产映射](../../../Docs/四色奶蛙资产映射.md)。卡面 `work/pet01_work.blend` / `pet01_surgery.blend` 是制作副本，不取代本目录源文件。源清单、Unity副本和final_manifest已于2026-09-21同步当前哈希与补充动作状态；不是重新验证模型。

- 高 20 cm，26 根骨骼。2026-09-29 起为用户授权的 `20260928_arm_separation`：双臂改为独立闭合的娃娃关节外壳，12,452 顶点 / 24,536 三角面（原 10,434 / 20,528），最多8个骨骼影响，静止骨架和 8 段旧Action曲线不变；classic_proud / divine_awaken 重做。工作文件与各轮尝试在 `ArmSeparation/`，见[修订记录](../../../Docs/手臂分离修订记录_20260929.md)。此前 2026-09-22 的 `20260922_arm_belly_weight_transition` 修复了抬臂破面过渡。
- Idle：3.0s循环；Eat：3.2s单次，两段无根位移。Consume 2.4s仅源设计标记，旧代码实际3.2s时钟结算；Eat作为冻结资产保留，食物玩法已退出新版范围。
- 2026-09-17 新增三段补充动作（只加 Action，网格与骨骼未改）：TouchReact 0.7 s 单次、
  DozeLoop 4.0 s 循环、WalkLoop 1.0 s 循环。前两段检查全部通过，WalkLoop 未通过脚部穿地检查
  （最低点 -0.00079 m，判据 -0.0005 m）。另有 Happy 决定不做。
- `pet01.blend` 的历史 SHA-256 随三段动作写入而变化（`fad23c5c…` → `8dc3dc92…`）；2026-09-22权重修订后当前哈希以同目录manifest为准。**冻结基准仍可复原**：
  `Archive/20260917_before_extra_actions/pet01.blend` 的哈希正好等于 `fad23c5c…`。
- `Previews/Frames/` 是实际动画渲染帧；`validation_report.json` 记录 32 项检查与导出文件哈希，
  `validation_report_extra_actions.json` 记录补充动作的 26 项检查，运行日志在 `Logs/`。

FBX 和贴图副本在 [Unity 导入资源目录](../../../ARPet/Assets/_Project/Art/Characters/Pet01/)。

## 归档

- `Archive/20260910_initial/`：最初版本。
- `Archive/20260910_before_belly_revision/`：腹部比例修订前。
- `Archive/20260917_before_extra_actions/`：三段补充动作写入前，含冻结版哈希。
- `Archive/20260922_before_exclusive_actions/`：四段专属动作与权重修复前的主源。
- `Archive/20260922_after_first_weight_fix/`：第一轮腹部过渡权重修复后的主源。
  该目录里的 `README.md` 是归档时的旧副本，内容已过时，**以本文件为准**。

每个归档目录里的 `backup_manifest.json` 记录文件哈希。

其他网站或软件生成的候选模型放到 [Candidates](Candidates/)，每个版本单独一个子目录。推荐格式、贴图要求与站姿建议见 [候选模型说明](Candidates/README.md)。
