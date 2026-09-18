const pptxgen = require("pptxgenjs");

const p = new pptxgen();
p.layout = "LAYOUT_WIDE"; // 13.33 x 7.5
p.author = "课程设计";
p.title = "基于图像跟踪与行为状态驱动的移动AR萌宠互动系统设计与实现";

const W = 13.33, H = 7.5, M = 0.5;
const BG_DARK = "26201A";      // 封面/结尾深暖棕
const BG = "FFFFFF";           // 内容页
const PRIMARY = "E8A93A";      // 琥珀金（奶蛙体色加深）
const PRIM_DEEP = "B97E1E";    // 深琥珀
const TINT = "F8F1E2";         // 卡片暖色底
const TINT2 = "FBF7EE";        // 更浅底
const ACCENT = "6E9B4E";       // 眼球绿（点缀）
const TEXT = "2B241A";
const MUTED = "8C8172";
const LIGHT = "F5EFE4";        // 深底上的浅字
const F = "微软雅黑";
const IMG = "D:/ZengQiangXianShi/ArtSource/References/20260908_naiwa_assets/milkyfrog_official/";

const sh = () => ({ type: "outer", color: "3A2F1E", blur: 7, offset: 2, angle: 45, opacity: 0.14 });
const bu = () => ({ code: "2022", indent: 12 });

function footer(s, n) {
  s.addText("桌上有龙 · 开题报告", { x: M, y: H - 0.38, w: 3, h: 0.3, fontSize: 10.5, fontFace: F, color: MUTED, margin: 0 });
  s.addText(String(n).padStart(2, "0"), { x: W - 1.1, y: H - 0.38, w: 0.6, h: 0.3, fontSize: 10.5, fontFace: F, color: MUTED, align: "right", margin: 0 });
}
function title(s, t, sub) {
  s.addText(t, { x: M, y: 0.38, w: W - 2 * M, h: 0.62, fontSize: 28, bold: true, fontFace: F, color: TEXT, margin: 0 });
  if (sub) s.addText(sub, { x: M, y: 1.0, w: W - 2 * M, h: 0.34, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
}
function card(s, x, y, w, h, fill) {
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x, y, w, h, fill: { color: fill || TINT }, rectRadius: 0.07, line: { type: "none" } });
}
function arrow(s, x, y, w, h, flipH, flipV) {
  let X = x, Y = y, Wd = w, Ht = h, fx = !!flipH, fy = !!flipV;
  if (w < 0) { X = x + w; Wd = -w; fx = !fx; }
  if (h < 0) { Y = y + h; Ht = -h; fy = !fy; }
  s.addShape(p.shapes.LINE, { x: X, y: Y, w: Wd, h: Ht, line: { color: PRIM_DEEP, width: 2.2, endArrowType: "triangle" }, flipH: fx, flipV: fy });
}

/* ============ S1 封面 ============ */
let s = p.addSlide();
s.background = { color: BG_DARK };
s.addImage({ path: IMG + "mf-pose-wave.png", x: 8.35, y: 1.35, w: 4.5, h: 4.5 });
s.addText("课程设计开题报告 · 2026 年 9 月", { x: 0.9, y: 1.15, w: 6.8, h: 0.4, fontSize: 15, fontFace: F, color: PRIMARY, charSpacing: 2, margin: 0 });
s.addText("基于图像跟踪与行为状态驱动的\n移动 AR 萌宠互动系统设计与实现", { x: 0.9, y: 1.75, w: 7.3, h: 2.3, fontSize: 33, bold: true, fontFace: F, color: "FFFFFF", lineSpacingMultiple: 1.22, margin: 0 });
s.addText([
  { text: "产品暂名 「桌上有龙」", options: { color: PRIMARY, bold: true } },
  { text: " · Unity + Vuforia 图像跟踪", options: { color: LIGHT, breakLine: true } },
  { text: "单人独立开发", options: { color: LIGHT } },
  { text: " · 约 4 周 · Android", options: { color: LIGHT } },
], { x: 0.9, y: 4.2, w: 7.3, h: 0.85, fontSize: 15, fontFace: F, lineSpacingMultiple: 1.35, margin: 0 });
s.addShape(p.shapes.LINE, { x: 0.9, y: 5.05, w: 5.6, h: 0, line: { color: "4A3E2C", width: 1 } });
s.addText("汇报人：＿＿＿＿　学号：＿＿＿＿　班级：＿＿＿＿　指导教师：＿＿＿＿\n（按学校开题模板补充）", { x: 0.9, y: 5.3, w: 7.0, h: 0.9, fontSize: 13, fontFace: F, color: "C9BFA9", lineSpacingMultiple: 1.4, margin: 0 });
s.addText("角色形象为风格参考 · 最终采用原创设计", { x: 8.55, y: 6.15, w: 4.1, h: 0.35, fontSize: 11.5, fontFace: F, color: "9A8D72", align: "center", margin: 0 });

/* ============ S2 目录 ============ */
s = p.addSlide();
s.background = { color: BG };
const toc = [
  ["01", "选题背景与意义", "两条成熟需求的交叉点"],
  ["02", "国内外现状与机会", "品类标杆离场后的空档"],
  ["03", "研究目标与特色", "一个闭环 · 五个问题 · 四点特色"],
  ["04", "系统设计与功能", "五层架构 · FR-01~09 · 投喂结算"],
  ["05", "技术路线与资产", "Unity + Vuforia · 奶蛙风格原创角色"],
  ["06", "计划 · 验证 · 成果", "四周排期 · 验收矩阵 · 交付清单"],
];
toc.forEach((it, i) => {
  const col = Math.floor(i / 3), row = i % 3;
  const x = M + col * 6.5, y = 1.5 + row * 1.85;
  s.addText(it[0], { x, y: y - 0.12, w: 1.3, h: 1.1, fontSize: 44, bold: true, fontFace: F, color: PRIMARY, margin: 0 });
  s.addText(it[1], { x: x + 1.35, y: y + 0.02, w: 4.9, h: 0.5, fontSize: 20, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2], { x: x + 1.35, y: y + 0.56, w: 4.9, h: 0.4, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
  if (row < 2) s.addShape(p.shapes.LINE, { x: x + 1.35, y: y + 1.38, w: 4.7, h: 0, line: { color: "E8E1D2", width: 1 } });
});

/* ============ S3 选题背景 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "选题背景：两条被长期验证的需求，在桌面相遇", "公开资料检索日期 2026-09-09，数字均注明来源");
const stats = [
  ["9,810 万台", "拓麻歌子系列硬件累计销量", "电子宠物陪伴需求跨越五十年代际（截至 2025，维基百科）"],
  ["390 万+", "《旅行青蛙》中国大陆两周下载", "2018-01 登顶免费榜：轻度陪伴 + 照片分享的爆发力（维基百科）"],
  ["0 依赖", "Vuforia 图像跟踪对 GMS 的依赖", "纯视觉方案可在国产安卓（iQOO Z9 Turbo 等）落地，与 ARCore 路线解耦"],
];
stats.forEach((it, i) => {
  const x = M + i * 4.31, w = 4.0;
  card(s, x, 1.75, w, 3.3, i === 2 ? TINT : TINT2);
  s.addText(it[0], { x: x + 0.3, y: 2.1, w: w - 0.6, h: 0.95, fontSize: 40, bold: true, fontFace: F, color: i === 2 ? ACCENT : PRIM_DEEP, margin: 0 });
  s.addText(it[1], { x: x + 0.3, y: 3.15, w: w - 0.6, h: 0.75, fontSize: 15.5, bold: true, fontFace: F, color: TEXT, lineSpacingMultiple: 1.2, margin: 0 });
  s.addText(it[2], { x: x + 0.3, y: 3.95, w: w - 0.6, h: 0.95, fontSize: 12, fontFace: F, color: MUTED, lineSpacingMultiple: 1.25, margin: 0 });
});
s.addText([
  { text: "结论　", options: { bold: true, color: PRIM_DEEP } },
  { text: "“照料虚拟生物”与“把角色放进现实”各自成熟，但两者的轻量结合——桌面 AR 萌宠——仍缺少能长期留在用户手机里的单机形态。", options: { color: TEXT } },
], { x: M, y: 5.45, w: W - 2 * M, h: 0.8, fontSize: 15.5, fontFace: F, lineSpacingMultiple: 1.3, margin: 0, valign: "middle" });
footer(s, 3);

/* ============ S4 国内外现状 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "国内外 AR 萌宠产品现状", "据公开资料整理（维基百科 / 官方文档 / App Store），检索日期 2026-09-09");
const th = { fill: { color: PRIM_DEEP }, color: "FFFFFF", bold: true, fontFace: F, fontSize: 12.5, valign: "middle" };
const td = (t, o) => ({ text: t, options: Object.assign({ fontFace: F, fontSize: 11.5, color: TEXT, valign: "middle" }, o || {}) });
const rows = [
  [td("产品", th), td("现状（截至 2026-09）", th), td("技术 / 玩法路线", th), td("对本课题的启示", th)],
  [td("Peridot（Niantic）", { bold: true }), td("2023-05 上线，2026-08-31 移动版停运", { color: "A33B2E" }), td("CV 识别真实环境 + AI 对话；扩展头显与眼镜"), td("技术最强者离场：重运营 + 强内购 + 云依赖难以为继")],
  [td("Pokémon GO", { bold: true }), td("2016 上线，运营中（2025 归 Scopely）"), td("LBS 捕捉；相机 AR 模式；Snapshot 拍照"), td("验证大众吸引力与拍照留念需求")],
  [td("《一起来捉妖》（腾讯）", { bold: true }), td("2019 公测；2023-2024 更新停滞", { color: "A33B2E" }), td("国内首款 LBS+AR 探索手游"), td("强制出门 + 交易竞争放大负担，加速流失")],
  [td("AR Dragon（PlaySide）", { bold: true }), td("2017 上线，2023-02 后基本停更", { color: "A33B2E" }), td("ARKit 单机养龙：孵化/喂养/装扮/拍照"), td("小团队可完成轻量闭环；生命周期短")],
  [td("拓麻歌子系列（万代）", { bold: true }), td("2021/2021/2023 持续迭代"), td("实体掌机：摄像头合影、实体卡扩展内容"), td("实体道具与合影玩法有成熟先例")],
  [td("Google 搜索 3D 动物", { bold: true }), td("功能可用（国内设备不可用）"), td("Scene Viewer 系统级放置 3D 模型"), td("“单点放置”不构成产品；国内存在空位")],
];
s.addTable(rows, { x: M, y: 1.55, w: W - 2 * M, colW: [2.5, 3.1, 3.3, 3.43], border: { pt: 0.75, color: "E3DCCB" }, rowH: 0.62, margin: 0.07, fill: { color: "FFFFFF" } });
s.addText("说明：产品均未实测，互动体验描述来自官方与公开资料；停运/停更以公开记载为准。", { x: M, y: 6.85, w: 9.5, h: 0.3, fontSize: 10.5, fontFace: F, color: MUTED, margin: 0 });
footer(s, 4);

/* ============ S5 机会空档 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "现状信号：品类标杆离场，空档清晰");
card(s, M, 1.6, 5.6, 3.6, TINT);
s.addText("2026-08-31", { x: 0.9, y: 2.0, w: 4.9, h: 1.0, fontSize: 47, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
s.addText("Peridot 移动版停运", { x: 0.9, y: 3.1, w: 4.9, h: 0.5, fontSize: 20, bold: true, fontFace: F, color: TEXT, margin: 0 });
s.addText("上线仅三年余的品类标杆（Niantic → Niantic Spatial）。视觉与 AR 技术受好评，付费模式遭批评（维基百科）。", { x: 0.9, y: 3.65, w: 4.9, h: 1.2, fontSize: 13, fontFace: F, color: MUTED, lineSpacingMultiple: 1.3, margin: 0 });
const sig = [
  ["AR Dragon 停更", "2023-02 后无更新：新鲜感产品的生命周期短板"],
  ["《一起来捉妖》边缘化", "LBS 强制出门 + 交易竞争，2022 年起被评“已凉”"],
  ["谷歌 3D 动物国内不可用", "依赖 GMS 的系统级方案覆盖不了国产主流安卓"],
];
sig.forEach((it, i) => {
  const y = 1.6 + i * 1.25;
  card(s, 6.5, y, 6.33, 1.05, TINT2);
  s.addText(it[0], { x: 6.8, y: y + 0.14, w: 5.8, h: 0.38, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: 6.8, y: y + 0.54, w: 5.8, h: 0.38, fontSize: 12, fontFace: F, color: MUTED, margin: 0 });
});
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y: 5.55, w: W - 2 * M, h: 1.15, fill: { color: BG_DARK }, rectRadius: 0.07, line: { type: "none" } });
s.addText([
  { text: "机会空档　", options: { bold: true, color: PRIMARY } },
  { text: "无服务器、可长期离线拥有、以实体道具交互的单机 AR 萌宠，在国产安卓设备上基本无人占据——本课题正好落在这个空档。", options: { color: LIGHT } },
], { x: 0.9, y: 5.7, w: 11.5, h: 0.85, fontSize: 15.5, fontFace: F, lineSpacingMultiple: 1.25, margin: 0, valign: "middle" });
footer(s, 5);

/* ============ S6 研究目标与拟解决问题 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "研究目标与拟解决的问题");
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y: 1.5, w: W - 2 * M, h: 1.0, fill: { color: TINT }, rectRadius: 0.07, line: { type: "none" } });
s.addText([
  { text: "总体目标　", options: { bold: true, color: PRIM_DEEP } },
  { text: "在普通安卓设备上实现「定位召唤—实体投喂—行为反馈—趣味互动—反应解锁—存档与拍照」完整闭环；", options: { color: TEXT, breakLine: true } },
  { text: "验证图像跟踪与行为状态驱动机制在移动 AR 萌宠中的应用可行性。", options: { color: TEXT } },
], { x: 0.85, y: 1.6, w: 11.7, h: 0.82, fontSize: 13.5, fontFace: F, lineSpacingMultiple: 1.2, margin: 0, valign: "middle" });
const probs = [
  ["空间稳定性", "手持设备上维持角色与实体标记的可用空间关系；跟踪丢失与恢复期间不产生错误交互"],
  ["交互确定性", "“靠近—停留—移开”的连续动作转化为恰好一次的有效投喂，排除误触发与重复结算"],
  ["行为可理解性", "同一操作在不同状态下反馈不同（饿则进食、饱则拒绝、困则迟钝），玩家能“读懂”宠物"],
  ["资产可行性", "单人、数周、无外包条件下产出有表现力的角色，并保住移动端帧率"],
  ["工程完整性", "识别、交互、动画、存档、拍照、真机测试全链路闭环，成果可安装、可演示、可复现"],
];
probs.forEach((it, i) => {
  const col = Math.floor(i / 3), row = i % 3;
  const x = M + col * 6.5, y = 2.85 + row * 1.32;
  s.addText("0" + (i + 1), { x, y: y + 0.02, w: 0.85, h: 0.7, fontSize: 26, bold: true, fontFace: F, color: PRIMARY, margin: 0 });
  s.addText(it[0], { x: x + 0.9, y, w: 5.4, h: 0.4, fontSize: 15.5, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: x + 0.9, y: y + 0.42, w: 5.4, h: 0.8, fontSize: 11.5, fontFace: F, color: MUTED, lineSpacingMultiple: 1.22, margin: 0 });
});
footer(s, 6);

/* ============ S7 预期特色 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "预期特色与创新点");
const feats = [
  ["实体道具输入", "食物卡靠近小窝卡即完成投喂，图像标记同时是产品道具——手机 AR 萌宠中无直接竞品"],
  ["可解释的行为状态驱动", "拒绝生成式 AI 黑箱：状态机规则可配置、可测试、可复现，“宠物有性格”来自透明规则而非算力"],
  ["单机轻量 · 数据归用户", "无服务器、无内购、本地版本化存档——对照 Peridot 停运，“宠物真正属于用户”是结构性优势"],
  ["面向国产中端设备", "跟踪不依赖 GMS；以 iQOO Z9 Turbo（Android 15）为首要实测平台，如实记录能力边界"],
];
feats.forEach((it, i) => {
  const y = 1.55 + i * 1.24;
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y, w: 8.6, h: 1.05, fill: { color: i % 2 ? TINT2 : TINT }, rectRadius: 0.07, line: { type: "none" } });
  s.addText(String(i + 1), { x: 0.78, y: y + 0.16, w: 0.6, h: 0.7, fontSize: 26, bold: true, fontFace: F, color: PRIMARY, margin: 0 });
  s.addText(it[0], { x: 1.5, y: y + 0.12, w: 7.4, h: 0.4, fontSize: 15.5, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: 1.5, y: y + 0.53, w: 7.5, h: 0.44, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
});
s.addImage({ path: IMG + "mf-pose-belly.png", x: 9.45, y: 2.0, w: 3.4, h: 3.4 });
s.addText("角色：奶蛙风格 · 原创设计", { x: 9.45, y: 5.45, w: 3.4, h: 0.35, fontSize: 11.5, fontFace: F, color: MUTED, align: "center", margin: 0 });
footer(s, 7);

/* ============ S8 系统架构 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "系统总体设计：五层架构", "跟踪层不结算数值 · 行为层统一裁决 · 表现层只执行");
function box(x, y, w, h, label, sub, dark) {
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x, y, w, h, fill: { color: dark ? BG_DARK : TINT }, rectRadius: 0.06, line: { type: "none" }, shadow: sh() });
  s.addText(label, { x, y: y + (sub ? 0.07 : 0), w, h: sub ? 0.42 : h, fontSize: 13, bold: true, fontFace: F, color: dark ? PRIMARY : TEXT, align: "center", valign: "middle", margin: 0 });
  if (sub) s.addText(sub, { x: x + 0.06, y: y + 0.46, w: w - 0.12, h: h - 0.55, fontSize: 9.5, fontFace: F, color: dark ? "C9BFA9" : MUTED, align: "center", lineSpacingMultiple: 1.1, margin: 0 });
}
box(M, 1.7, 3.7, 0.95, "摄像头与图像跟踪", "Vuforia Image Targets", true);
box(4.9, 1.7, 3.7, 0.95, "目标坐标与跟踪状态", "小窝局部坐标系 · 米制 · 四态提示");
box(9.3, 1.7, 3.53, 0.95, "触屏输入", "逗弄 / 追球 · 不穿透到角色");
box(4.9, 3.15, 3.7, 1.1, "交互判定", "距离+高度+停留+冷却 · 一次性事件");
box(4.9, 4.75, 3.7, 1.25, "行为状态机", "饥饿·开心·困倦 × 行为优先级 · 一次性结算", true);
box(M, 4.75, 3.7, 1.25, "配置", "ScriptableObject：食物 / 阈值 / 反应条件");
box(9.3, 4.75, 3.53, 1.25, "本地存档", "版本化 JSON · 异常回退");
box(M, 6.32, 3.7, 0.8, "表现层", "Animator 动画 · 表情 · 音效");
box(4.9, 6.32, 3.7, 0.8, "界面层", "uGUI 状态面板 · 反应图鉴");
box(9.3, 6.32, 3.53, 0.8, "拍照合成", "摄像头背景 + 角色");
arrow(s, 4.2, 2.17, 0.7, 0);
arrow(s, 8.6, 2.17, 0.7, 0, true);
arrow(s, 6.75, 2.65, 0, 0.5);
arrow(s, 6.75, 4.25, 0, 0.5);
arrow(s, 4.2, 5.37, 0.7, 0);
s.addShape(p.shapes.LINE, { x: 8.6, y: 5.37, w: 0.7, h: 0, line: { color: PRIM_DEEP, width: 2.2, beginArrowType: "triangle", endArrowType: "triangle" } });
arrow(s, 6.75, 6.0, 0, 0.32);
arrow(s, 6.75, 6.0, -4.4, 0.32, false, false);
arrow(s, 6.75, 6.0, 4.32, 0.32);
footer(s, 8);

/* ============ S9 基线功能 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "基线功能设计（FR-01 ~ FR-09）", "首版 1 个角色；核心互动为投喂、逗弄、追球；约 5~6 类基础动作");
const fr = [
  ["FR-01", "小窝定位与召唤", "单张已知尺寸卡片建立角色局部坐标系"],
  ["FR-02", "跟踪状态处理", "识别/有效/丢失/恢复四态提示；丢失即暂停新交互"],
  ["FR-03", "实体投喂 ★", "双卡跟踪 + 距离/高度/停留/冷却；一次进入仅结算一次"],
  ["FR-04", "触屏逗弄", "点击/滑动触发反馈；界面输入不穿透"],
  ["FR-05", "简单追球", "标记定义的虚拟平面内投放小球，角色限范围追逐"],
  ["FR-06", "状态行为反馈", "饥饿/开心/困倦状态 × 进食/移动/休息行为"],
  ["FR-07", "反应解锁", "状态组合解锁少量趣味反应，图鉴展示"],
  ["FR-08", "本地存档", "版本化 JSON；重启恢复；异常回退默认"],
  ["FR-09", "拍照留念", "合成截图保存；失败路径有明确反馈"],
];
fr.forEach((it, i) => {
  const col = i % 3, row = Math.floor(i / 3);
  const x = M + col * 4.31, y = 1.75 + row * 1.62;
  card(s, x, y, 4.0, 1.42, it[1].includes("★") ? TINT : TINT2);
  s.addText(it[0], { x: x + 0.22, y: y + 0.14, w: 1.0, h: 0.35, fontSize: 12, bold: true, fontFace: F, color: it[1].includes("★") ? PRIM_DEEP : MUTED, margin: 0 });
  s.addText(it[1], { x: x + 0.22, y: y + 0.46, w: 3.6, h: 0.38, fontSize: 14.5, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2], { x: x + 0.22, y: y + 0.86, w: 3.6, h: 0.5, fontSize: 10.5, fontFace: F, color: MUTED, lineSpacingMultiple: 1.15, margin: 0 });
});
s.addText("后续可选扩展（不计入验收）：视频导出 · 多角色多食物 · 手部追踪 · 真实遮挡 · 语音对话 · 联网多人", { x: M, y: 6.75, w: W - 2 * M, h: 0.35, fontSize: 11, fontFace: F, color: MUTED, margin: 0 });
footer(s, 9);

/* ============ S10 一次性投喂结算 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "关键机制：把“卡片靠近”变成恰好一次投喂", "对应拟解决问题 2：交互确定性");
const steps = [
  ["双目标有效", "小窝卡与食物卡同时处于有效跟踪"],
  ["坐标转换", "食物卡位置换算到小窝局部坐标系"],
  ["区域判定", "平面距离 + 高度范围构成投喂区"],
  ["停留计时", "进入投喂区并保持可配置时长"],
  ["行为裁决", "状态机接受进食或给出拒绝反应"],
  ["结算一次", "动画事件确定时刻结算 + 进入冷却"],
];
steps.forEach((it, i) => {
  const col = i % 3, row = Math.floor(i / 3);
  const x = M + col * 4.31, y = 1.75 + row * 1.9;
  card(s, x, y, 3.7, 1.5, TINT2);
  s.addShape(p.shapes.OVAL, { x: x + 0.22, y: y + 0.22, w: 0.62, h: 0.62, fill: { color: PRIMARY }, line: { type: "none" } });
  s.addText(String(i + 1), { x: x + 0.22, y: y + 0.22, w: 0.62, h: 0.62, fontSize: 18, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
  s.addText(it[0], { x: x + 1.0, y: y + 0.3, w: 2.6, h: 0.42, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: x + 0.24, y: y + 0.92, w: 3.25, h: 0.5, fontSize: 10.5, fontFace: F, color: MUTED, lineSpacingMultiple: 1.15, margin: 0 });
  if (col < 2) arrow(s, x + 3.72, y + 0.75, 0.57, 0);
});
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y: 5.85, w: W - 2 * M, h: 0.95, fill: { color: BG_DARK }, rectRadius: 0.07, line: { type: "none" } });
s.addText([
  { text: "边界规则　", options: { bold: true, color: PRIMARY } },
  { text: "跟踪丢失立即暂停计时与交互；恢复跟踪不自动补一次投喂；移开后须再次进入并满足冷却，才允许下一次结算。", options: { color: LIGHT } },
], { x: 0.9, y: 5.97, w: 11.5, h: 0.72, fontSize: 13.5, fontFace: F, lineSpacingMultiple: 1.2, margin: 0, valign: "middle" });
footer(s, 10);

/* ============ S11 角色资产方案 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "角色资产方案：奶蛙风格的原创设计", "已建立量化建模简报：比例表 · 实测色值 · 分部位要点");
const poses = ["mf-pose-wave.png", "mf-pose-belly.png", "mf-pose-lying.png", "mf-pose-rolling.png"];
poses.forEach((f2, i) => {
  const x = M + i * 3.12;
  card(s, x, 1.7, 2.9, 2.9, TINT2);
  s.addImage({ path: IMG + f2, x: x + 0.2, y: 1.9, w: 2.5, h: 2.5 });
});
s.addText("姿态参考（站姿 / 捧腹 / 侧躺 / 打滚）——仅作造型与动作参考，最终形象为原创设计，不精确复刻现有 IP", { x: M, y: 4.72, w: 12.33, h: 0.4, fontSize: 11, fontFace: F, color: MUTED, margin: 0 });
const asset = [
  ["比例基准", "总高 20cm：头约占 0.30H，下腹最宽 0.72H，无脖子、连续背部弧线"],
  ["骨骼与表情", "约 7 根骨骼（头根/脊柱/双肩/双髋/尾根）+ 形状键做张口闭眼"],
  ["首轮验收", "仅待机 + 进食两个动作 → FBX 入 Unity 校验后再扩展"],
  ["配色实测", "体色 #F5C24B · 腹部 #F6ECDB · 手脚 #5B3E19 · 眼圈 #7BA459"],
  ["风险备选", "三维受阻则降级二维序列帧——不解决跟踪问题，须同步修订边界"],
];
asset.forEach((it, i) => {
  const y = 5.08 + i * 0.4;
  s.addText(it[0], { x: M, y, w: 1.5, h: 0.36, fontSize: 12, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0, valign: "middle" });
  s.addText(it[1], { x: 2.1, y, w: 10.7, h: 0.36, fontSize: 11.5, fontFace: F, color: TEXT, margin: 0, valign: "middle" });
  if (i < 4) s.addShape(p.shapes.LINE, { x: M, y: y + 0.38, w: 12.33, h: 0, line: { color: "EDE6D6", width: 0.75 } });
});
footer(s, 11);

/* ============ S12 关键选型：Vuforia ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "关键选型：为什么是 Vuforia，而不是 ARKit / ARCore", "课件介绍了 ARKit / ARCore / AR Foundation / Vuforia 四条路线——本课题按设备条件取捨");
const sel = [
  ["ARKit", "Apple · 仅 iOS", "目标设备 iQOO Z9 Turbo 为 Android 15，ARKit 平台根本不可用，无讨论空间。", "排除 · 平台不容", "A33B2E", TINT2],
  ["ARCore / AR Foundation", "Google · Android", "该机型未列入 ARCore 官方支持列表；国行系统通常无 GMS。AR Foundation 只是封装层，底层仍依赖 ARCore，绕不开认证门槛。", "排除 · 设备不容", "A33B2E", TINT2],
  ["Vuforia Image Targets", "本课题选用", "纯视觉跟踪，不依赖系统 AR 服务与 GMS；Image Targets 恰好匹配“卡片定位 + 双卡投喂”；无需 SLAM、平面检测与环境理解。", "选用 · 确定性最高", ACCENT, TINT],
];
sel.forEach((it, i) => {
  const x = M + i * 4.31, w = 4.0, y = 1.62, h = 3.85;
  card(s, x, y, w, h, it[5]);
  s.addText(it[0], { x: x + 0.28, y: y + 0.24, w: w - 0.56, h: 0.72, fontSize: 16.5, bold: true, fontFace: F, color: TEXT, lineSpacingMultiple: 1.1, margin: 0 });
  s.addText(it[1], { x: x + 0.28, y: y + 0.98, w: w - 0.56, h: 0.35, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
  s.addText(it[2], { x: x + 0.28, y: y + 1.42, w: w - 0.56, h: 1.72, fontSize: 11.5, fontFace: F, color: TEXT, lineSpacingMultiple: 1.28, margin: 0 });
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: x + 0.28, y: y + h - 0.6, w: 2.0, h: 0.4, fill: { color: it[4] }, rectRadius: 0.06, line: { type: "none" } });
  s.addText(it[3], { x: x + 0.28, y: y + h - 0.6, w: 2.0, h: 0.4, fontSize: 11.5, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
});
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y: 5.72, w: W - 2 * M, h: 1.18, fill: { color: BG_DARK }, rectRadius: 0.07, line: { type: "none" } });
s.addText([
  { text: "答辩口径　", options: { bold: true, color: PRIMARY } },
  { text: "课件本身同样讲授 Vuforia（另有专门的导入方法文档）——本课题是在课件给出的路线中，选择与目标设备匹配的一种。已知代价：需打印实体卡、对光照敏感；真实遮挡与环境理解不在基线内。", options: { color: LIGHT, breakLine: true } },
  { text: "一切以第 1 周真机样例实测为准；若实测设备支持 ARCore，两者可兼容共存。", options: { color: "C9BFA9" } },
], { x: 0.9, y: 5.84, w: 11.5, h: 0.95, fontSize: 12.5, fontFace: F, lineSpacingMultiple: 1.22, paraSpaceAfter: 4, margin: 0 });
footer(s, 12);

/* ============ S12 技术路线与环境 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "技术路线与开发环境");
const tech = [
  ["引擎", "Unity 6 LTS（补丁版本待真机样例锁定）"],
  ["跟踪", "Vuforia Image Targets：先单目标，后双目标"],
  ["逻辑", "C# 行为状态机（优先级 / 冷却 / 一次性结算）"],
  ["动画", "Animator + Generic 骨骼（非人形，不用重定向）"],
  ["美术", "Blender 5.1.1 + bpy 脚本化 → FBX / 贴图"],
  ["界面", "uGUI，适配手机与平板安全区域"],
  ["数据", "ScriptableObject 配置 / 版本化 JSON 存档"],
  ["构建", "Android Build Support · ARM64 · 配套 SDK/NDK/JDK"],
];
tech.forEach((it, i) => {
  const y = 1.6 + i * 0.63;
  s.addText(it[0], { x: M, y, w: 1.0, h: 0.5, fontSize: 13, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0, valign: "middle" });
  s.addText(it[1], { x: 1.65, y, w: 6.0, h: 0.5, fontSize: 13, fontFace: F, color: TEXT, margin: 0, valign: "middle" });
  if (i < 7) s.addShape(p.shapes.LINE, { x: M, y: y + 0.56, w: 7.15, h: 0, line: { color: "EDE6D6", width: 0.75 } });
});
card(s, 8.5, 1.6, 4.33, 4.9, TINT);
s.addText("环境现状（2026-09 核查）", { x: 8.8, y: 1.85, w: 3.8, h: 0.4, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
s.addText([
  { text: "已具备", options: { bold: true, color: ACCENT, breakLine: true } },
  { text: "Unity Hub + 2022.3.62f1c1（缺 Android Build Support）", options: { bullet: bu(), breakLine: true } },
  { text: "Blender 5.1.1（bpy 后台调用已跑通）", options: { bullet: bu(), breakLine: true } },
  { text: "独立 Android SDK（API 36）与 adb", options: { bullet: bu(), breakLine: true } },
  { text: "待完成", options: { bold: true, color: "A33B2E" } },
  { text: "按 Vuforia 支持表锁定并安装 Unity 版本 + Android Build Support", options: { bullet: bu(), breakLine: true } },
  { text: "注册 Vuforia 开发密钥", options: { bullet: bu(), breakLine: true } },
  { text: "真机跑通官方样例，锁定渲染管线", options: { bullet: bu() } },
], { x: 8.8, y: 2.35, w: 3.8, h: 3.9, fontSize: 12, fontFace: F, color: TEXT, paraSpaceAfter: 7, lineSpacingMultiple: 1.15, margin: 0 });
footer(s, 13);

/* ============ S14 进度安排 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "实施计划：约 4 周（2026-09-08 起算）");
const plan = [
  ["第 1 周", "09-08 ~ 09-14", "环境锁定、密钥配置、单/双目标真机验证与资产样例", "最小 AR APK 与版本记录"],
  ["第 2 周", "09-15 ~ 09-21", "实体投喂一次性结算、触屏逗弄、状态机衔接动画", "核心互动闭环可运行"],
  ["第 3 周", "09-22 ~ 09-28", "追球、反应解锁、本地存档、拍照与界面适配", "功能完整的候选版本"],
  ["第 4 周", "09-29 ~ 10-05", "真机测试、缺陷修复、性能测量与答辩材料", "APK、源码、资产、测试记录"],
];
plan.forEach((it, i) => {
  const x = M + i * 3.24, w = 3.0;
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x, y: 1.75, w, h: 3.3, fill: { color: i === 0 ? TINT : TINT2 }, rectRadius: 0.07, line: { type: "none" }, shadow: sh() });
  s.addText(it[0], { x: x + 0.22, y: 1.95, w: w - 0.44, h: 0.45, fontSize: 18, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
  s.addText(it[1], { x: x + 0.22, y: 2.42, w: w - 0.44, h: 0.35, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
  s.addText(it[2], { x: x + 0.22, y: 2.88, w: w - 0.44, h: 1.15, fontSize: 11.5, fontFace: F, color: TEXT, lineSpacingMultiple: 1.25, margin: 0 });
  s.addShape(p.shapes.LINE, { x: x + 0.22, y: 4.1, w: w - 0.44, h: 0, line: { color: "E5DCC6", width: 1 } });
  s.addText(it[3], { x: x + 0.22, y: 4.2, w: w - 0.44, h: 0.75, fontSize: 11, bold: true, fontFace: F, color: ACCENT, lineSpacingMultiple: 1.2, margin: 0 });
  if (i < 3) arrow(s, x + w + 0.02, 3.3, 0.2, 0);
});
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: M, y: 5.45, w: W - 2 * M, h: 1.25, fill: { color: BG_DARK }, rectRadius: 0.07, line: { type: "none" } });
s.addText([
  { text: "第一周优先门槛　", options: { bold: true, color: PRIMARY } },
  { text: "真机运行 APK · 角色稳定附着小窝卡 · 食物卡触发一次进食且移开可再触发 · 跟踪丢失不错误结算。未达成则不进入功能开发，优先排查 SDK / 配置 / 设备。", options: { color: LIGHT } },
], { x: 0.9, y: 5.6, w: 11.5, h: 0.95, fontSize: 13.5, fontFace: F, lineSpacingMultiple: 1.25, margin: 0, valign: "middle" });
footer(s, 14);

/* ============ S15 验证与风险 ============ */
s = p.addSlide();
s.background = { color: BG };
title(s, "验证方法与风险应对", "阈值在第一周样例后确认；结果必须来自真实测试记录，区分“目标/已测/待验证”");
s.addText("验证矩阵（初步目标）", { x: M, y: 1.55, w: 6, h: 0.4, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
const vm = [
  ["识别", "固定卡面重复 20 次", "常规条件 ≥18 次成功"],
  ["交互", "重复投喂 / 短暂经过", "一次进入仅结算一次"],
  ["丢失", "遮挡/移出后重新对准", "丢失即停，恢复规则一致"],
  ["性能", "真机连续互动 ≥5 分钟", "约 30 FPS 起步目标"],
  ["存档", "重启 + 异常存档注入", "可恢复，异常不致崩溃"],
  ["体验", "3~5 人同一组任务", "探索性记录，不作统计结论"],
];
vm.forEach((it, i) => {
  const y = 2.05 + i * 0.78;
  s.addText(it[0], { x: M, y, w: 0.95, h: 0.62, fontSize: 13, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0, valign: "middle" });
  s.addText(it[1], { x: 1.55, y, w: 2.6, h: 0.62, fontSize: 11.5, fontFace: F, color: TEXT, margin: 0, valign: "middle" });
  s.addText(it[2], { x: 4.25, y, w: 2.85, h: 0.62, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0, valign: "middle" });
  if (i < 5) s.addShape(p.shapes.LINE, { x: M, y: y + 0.7, w: 6.6, h: 0, line: { color: "EDE6D6", width: 0.75 } });
});
s.addText("主要风险与应对", { x: 7.6, y: 1.55, w: 5, h: 0.4, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
const risks = [
  ["环境/真机兼容受阻", "第一周全时攻坚；评估 2022.3 LTS 支持；备机预案"],
  ["双目标跟踪不稳", "特征清晰卡面；先单目标；实测恢复表现再引入"],
  ["三维资产超期", "先做两动作；二维序列帧备选并修订边界"],
  ["交互误触发", "距离+高度+停留+冷却+再进入多重判定"],
];
risks.forEach((it, i) => {
  const y = 2.05 + i * 1.18;
  card(s, 7.6, y, 5.23, 1.0, TINT2);
  s.addText(it[0], { x: 7.85, y: y + 0.1, w: 4.8, h: 0.36, fontSize: 13, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: 7.85, y: y + 0.5, w: 4.8, h: 0.42, fontSize: 10.5, fontFace: F, color: MUTED, margin: 0 });
});
footer(s, 15);

/* ============ S16 预期成果 + 结束 ============ */
s = p.addSlide();
s.background = { color: BG_DARK };
s.addImage({ path: IMG + "mf-pose-lying.png", x: 9.0, y: 3.1, w: 3.9, h: 3.9 });
s.addText("预期成果", { x: 0.9, y: 0.9, w: 6, h: 0.6, fontSize: 28, bold: true, fontFace: F, color: PRIMARY, margin: 0 });
s.addText([
  { text: "可安装的 Android APK（ARM64）与 Unity 工程源码", options: { bullet: bu(), breakLine: true } },
  { text: "角色资产包：Blender 源文件 + FBX / 贴图 + 来源记录", options: { bullet: bu(), breakLine: true } },
  { text: "可打印小窝卡 / 食物卡与实际尺寸说明", options: { bullet: bu(), breakLine: true } },
  { text: "使用说明、真机测试记录与已知限制清单", options: { bullet: bu(), breakLine: true } },
  { text: "课程设计报告、演示录像与答辩材料", options: { bullet: bu() } },
], { x: 0.9, y: 1.7, w: 7.4, h: 2.6, fontSize: 15, fontFace: F, color: LIGHT, paraSpaceAfter: 12, margin: 0 });
s.addShape(p.shapes.LINE, { x: 0.9, y: 4.55, w: 7.0, h: 0, line: { color: "4A3E2C", width: 1 } });
s.addText("谢谢聆听 · 恳请各位老师指正", { x: 0.9, y: 4.85, w: 7.4, h: 0.7, fontSize: 26, bold: true, fontFace: F, color: "FFFFFF", margin: 0 });
s.addText("参考文献 14 项见《开题报告》正文 · 竞品事实检索日期 2026-09-09\n角色形象仅作风格参考 · 最终为原创设计", { x: 0.9, y: 5.7, w: 7.4, h: 0.8, fontSize: 11.5, fontFace: F, color: "9A8D72", lineSpacingMultiple: 1.4, margin: 0 });

p.writeFile({ fileName: "D:/ZengQiangXianShi/Docs/开题报告_桌上有龙_答辩.pptx" }).then(() => console.log("PPTX written"));
