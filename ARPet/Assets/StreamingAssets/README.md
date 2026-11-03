# StreamingAssets — Vuforia设备数据库

2026-09-22核对：`Vuforia/ZQXS.xml` 和 `Vuforia/ZQXS.dat` 已替换为新 Vuforia 数据库导出。XML 含 `card_classic`、`card_calm`、`card_rage`、`card_divine` 四个角色目标，尺寸均为 `0.066×0.093923m`，并保留旧 `den_card_placeholder` / `food_card_placeholder` 条目供历史场景回溯。当前场景仍引用旧目标，最大同时跟踪数仍为2；四目标接线与真机能力尚未验证。

用户已删除食物卡玩法；旧条目暂用于尚未迁移的单宠场景，新业务不会继续使用食物目标。

下一步按 [四色资产映射](../../../Docs/四色奶蛙资产映射.md) 补录四卡评级、接入新目标场景并做实测；classic有历史4星，其余pending。实际目标宽0.066m、高0.093923m，以完整上传图对应的印刷范围为准，不能填70×99mm保护套尺寸。

导入新库必须同时更新TargetBinding、场景Observer及ProjectSetup/构建准备；当前工具仍生成旧占位目标。先两宠后四宠验证，六配对与四卡同屏实测不由评级替代。Vuforia11没有Load Database勾选，目标启用时加载。

旧数据库退出运行包随迁移实施；设计源、打印母版、目标ID与尺寸记录统一见资产映射，不再另造并行目录说明。
