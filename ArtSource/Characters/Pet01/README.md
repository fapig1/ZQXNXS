# Pet01 奶蛙资产

> 当前有效版本是 **2026-09-11 冻结的最终版**：形体、26 根骨骼与 Idle / Eat 两段动画已由用户确认结束，只做 Unity 导入与表现接入。
> 完整记录见[奶蛙最终版资产说明](../../../Docs/奶蛙最终版资产说明.md)；制作过程见[奶蛙首轮资产与动作说明](../../../Docs/奶蛙首轮资产与动作说明.md)。

打开 [review.html](review.html) 查看视角、骨骼、表情与视频；打开 [pet01.blend](pet01.blend) 编辑资产，保留同级 `Textures/`。
源模型使用相对路径引用 `Textures/`，移动或复制源文件时一并保留该目录。渲染相机、灯光和地台只保存在源文件里。

## 现状

- 高 20 cm，10,434 顶点 / 20,528 三角面，26 根骨骼，每顶点最多 3 个骨骼影响。
- Idle：3.0 s 循环；Eat：3.2 s 单次，唯一建议结算点为 2.4 s。两段都无根位移。
- 2026-09-17 新增三段补充动作（只加 Action，网格与骨骼未改）：TouchReact 0.7 s 单次、
  DozeLoop 4.0 s 循环、WalkLoop 1.0 s 循环。前两段检查全部通过，WalkLoop 未通过脚部穿地检查
  （最低点 -0.00079 m，判据 -0.0005 m）。另有 Happy 决定不做。
- `pet01.blend` 的 SHA-256 随三段动作写入而变化（`fad23c5c…` → `8dc3dc92…`）。**冻结基准仍可复原**：
  `Archive/20260917_before_extra_actions/pet01.blend` 的哈希正好等于 `fad23c5c…`。
- `Previews/Frames/` 是实际动画渲染帧；`validation_report.json` 记录 32 项检查与导出文件哈希，
  `validation_report_extra_actions.json` 记录补充动作的 26 项检查，运行日志在 `Logs/`。

FBX 和贴图副本在 [Unity 导入资源目录](../../../ARPet/Assets/_Project/Art/Characters/Pet01/)。

## 归档

- `Archive/20260910_initial/`：最初版本。
- `Archive/20260910_before_belly_revision/`：腹部比例修订前。
- `Archive/20260917_before_extra_actions/`：三段补充动作写入前，含冻结版哈希。
  该目录里的 `README.md` 是归档时的旧副本，内容已过时，**以本文件为准**。

每个归档目录里的 `backup_manifest.json` 记录文件哈希。

其他网站或软件生成的候选模型放到 [Candidates](Candidates/)，每个版本单独一个子目录。推荐格式、贴图要求与站姿建议见 [候选模型说明](Candidates/README.md)。
