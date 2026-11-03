// 结题答辩 PPT 生成脚本。配图先由 Tools/pptx_prep_final_images.py 生成到 Tools/_render/final_img/。
// 运行：node Tools/pptx_build_final.js（pptxgenjs 为全局安装，见下方 require）
const path = require("path");
const pptxgen = require(path.join(process.env.APPDATA, "npm/node_modules/pptxgenjs"));

const p = new pptxgen();
p.layout = "LAYOUT_WIDE"; // 13.33 x 7.5
p.author = "桌上有龙项目组";
p.title = "桌上有龙：从桌面 AR 萌宠到 AR 精灵对战体验馆（结题答辩）";

const W = 13.33, H = 7.5, M = 0.5;
const BG_DARK = "26201A";
const BG = "FFFFFF";
const PRIMARY = "E8A93A";
const PRIM_DEEP = "B97E1E";
const TINT = "F8F1E2";
const TINT2 = "FBF7EE";
const ACCENT = "6E9B4E";
const TEXT = "2B241A";
const MUTED = "8C8172";
const LIGHT = "F5EFE4";
const LINE = "E8E1D2";
const F = "微软雅黑";
const IMG = path.join(__dirname, "_render/final_img") + "/";
const OUT = path.join(__dirname, "../Docs/结题答辩_桌上有龙.pptx");

// 四色
const C = { classic: "E8A93A", calm: "3E8FB0", rage: "D2553A", divine: "8A63B8" };

const bu = () => ({ code: "2022", indent: 12 });

function footer(s, n) {
  s.addText("桌上有龙 · 结题答辩", { x: M, y: H - 0.38, w: 3, h: 0.3, fontSize: 10.5, fontFace: F, color: MUTED, margin: 0 });
  s.addText(String(n).padStart(2, "0"), { x: W - 1.1, y: H - 0.38, w: 0.6, h: 0.3, fontSize: 10.5, fontFace: F, color: MUTED, align: "right", margin: 0 });
}
function title(s, t, sub) {
  s.addText(t, { x: M, y: 0.36, w: W - 2 * M, h: 0.62, fontSize: 27, bold: true, fontFace: F, color: TEXT, margin: 0 });
  if (sub) s.addText(sub, { x: M, y: 0.98, w: W - 2 * M, h: 0.34, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
}
function card(s, x, y, w, h, fill) {
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x, y, w, h, fill: { color: fill || TINT }, rectRadius: 0.07, line: { type: "none" } });
}
function tag(s, x, y, text, color, w) {
  const tw = w || 0.2 + text.length * 0.16;
  s.addShape(p.shapes.ROUNDED_RECTANGLE, { x, y, w: tw, h: 0.3, fill: { color }, rectRadius: 0.15, line: { type: "none" } });
  s.addText(text, { x, y, w: tw, h: 0.3, fontSize: 10.5, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
  return tw;
}
function hArrow(s, x, y, w, color) {
  s.addShape(p.shapes.LINE, { x, y, w, h: 0, line: { color: color || PRIM_DEEP, width: 2.2, endArrowType: "triangle" } });
}
function vArrow(s, x, y, h, color) {
  s.addShape(p.shapes.LINE, { x, y, w: 0, h, line: { color: color || PRIM_DEEP, width: 2.2, endArrowType: "triangle" } });
}
function img(s, name, x, y, w, h, extra) {
  s.addImage(Object.assign({ path: IMG + name, x, y, w, h }, extra || {}));
}
// 按高度等比放图，返回宽度
const SIZES = require(IMG + "sizes.json");
function imgH(s, name, x, y, h, extra) {
  const [pw, ph] = SIZES[name];
  const w = h * pw / ph;
  img(s, name, x, y, w, h, extra);
  return w;
}
function sectionBar(s, label) {
  s.addText(label, { x: W - 3.6, y: 0.42, w: 3.1, h: 0.3, fontSize: 11, fontFace: F, color: PRIM_DEEP, align: "right", bold: true, margin: 0 });
}

let n = 0;
let s;

/* ============ 封面 ============ */
s = p.addSlide(); n++;
s.background = { color: BG_DARK };
s.addText("课程设计结题答辩", { x: 0.8, y: 0.95, w: 6.5, h: 0.4, fontSize: 15, fontFace: F, color: PRIMARY, charSpacing: 2, margin: 0 });
s.addText("基于图像跟踪与行为状态驱动的\n移动 AR 萌宠互动系统设计与实现", { x: 0.8, y: 1.5, w: 6.6, h: 1.9, fontSize: 30, bold: true, fontFace: F, color: "FFFFFF", lineSpacingMultiple: 1.2, margin: 0 });
s.addText([
  { text: "「桌上有龙」", options: { color: PRIMARY, bold: true } },
  { text: "：从桌面 AR 萌宠，到 AR 精灵对战体验馆", options: { color: LIGHT } },
], { x: 0.8, y: 3.55, w: 6.6, h: 0.5, fontSize: 16, fontFace: F, margin: 0 });
s.addText("Unity 6 · Vuforia 11.4 图像跟踪 · sherpa-onnx 离线语音 · Android", { x: 0.8, y: 4.1, w: 6.6, h: 0.4, fontSize: 13, fontFace: F, color: "C9BFA9", margin: 0 });
s.addShape(p.shapes.LINE, { x: 0.8, y: 4.8, w: 5.8, h: 0, line: { color: "4A3E2C", width: 1 } });
s.addText("汇报人：张俊城、钱超、杜宗熠　班级：数字2402　指导教师：谢青", { x: 0.8, y: 5.0, w: 6.8, h: 0.4, fontSize: 13, fontFace: F, color: "C9BFA9", margin: 0 });
s.addShape(p.shapes.ROUNDED_RECTANGLE, { x: 7.55, y: 1.25, w: 5.3, h: 3.6, fill: { color: "3A3026" }, rectRadius: 0.08, line: { type: "none" } });
img(s, "real_four_cards.jpg", 7.7, 1.4, 5.0, 5.0 * 892 / 1400);
s.addText("真机截图：四张角色卡、四只奶蛙同屏（iQOO Z9 Turbo / Android 15）", { x: 7.55, y: 5.0, w: 5.3, h: 0.35, fontSize: 11, fontFace: F, color: "9A8D72", align: "center", margin: 0 });

/* ============ 目录 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
const toc = [
  ["01", "灵感来源", "塔罗牌 → 童年精灵卡 → 奶蛙多形态"],
  ["02", "反思：宝可梦 GO", "爆红之后为什么留不住人"],
  ["03", "已实现的功能", "卡片识别 · 语音口令 · 行为演出"],
  ["04", "功能如何对应体验馆", "每项已实现能力在未来里的位置"],
  ["05", "未来展望：AR 精灵馆", "形态 · 对战规则 · 场馆动线 · 路线图"],
  ["06", "测试结果与不足", "已验证 · 待复测 · 未完成"],
];
toc.forEach((it, i) => {
  const col = Math.floor(i / 3), row = i % 3;
  const x = M + col * 6.5, y = 1.2 + row * 1.9;
  s.addText(it[0], { x, y: y - 0.12, w: 1.3, h: 1.1, fontSize: 44, bold: true, fontFace: F, color: PRIMARY, margin: 0 });
  s.addText(it[1], { x: x + 1.35, y: y + 0.02, w: 4.9, h: 0.5, fontSize: 20, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2], { x: x + 1.35, y: y + 0.56, w: 4.9, h: 0.4, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
  if (row < 2) s.addShape(p.shapes.LINE, { x: x + 1.35, y: y + 1.4, w: 4.7, h: 0, line: { color: LINE, width: 1 } });
});
footer(s, n);

/* ============ 灵感①：塔罗牌与童年精灵卡 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "01 灵感来源");
title(s, "灵感①：从塔罗牌，想到小时候的精灵卡", "一张卡上有角色、有性格，却始终只是一张纸");
const insp = [
  ["塔罗牌", "起点", [
    "每张牌有固定的人物形象和寓意",
    "一整套牌义体系，大家按同一套规则解读",
    "卡面本身就在讲故事",
  ], TINT2],
  ["童年的精灵卡", "遗憾", [
    "零食附赠、文具店买，人手一叠",
    "受技术限制，只能交换、收藏、拍卡翻面",
    "卡上印着数值，却没人真按它打",
    "各地玩法不同，没有统一规则",
  ], TINT],
  ["我们想做的", "目标", [
    "卡片 = 精灵的身份",
    "摄像头一扫，精灵站上桌面", "精灵会动，也会回应你",
    "胜负和数值由程序统一裁决",
  ], "F3E6C8"],
];
insp.forEach((it, i) => {
  const x = M + i * 4.2, w = 3.85, y = 1.6, h = 4.4;
  card(s, x, y, w, h, it[3]);
  tag(s, x + 0.3, y + 0.3, it[1], i === 2 ? PRIM_DEEP : (i === 1 ? "B5A48A" : MUTED), 0.8);
  s.addText(it[0], { x: x + 0.3, y: y + 0.75, w: w - 0.6, h: 0.55, fontSize: 21, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2].map((t) => ({ text: t, options: { bullet: bu(), breakLine: true } })),
    { x: x + 0.3, y: y + 1.5, w: w - 0.5, h: h - 1.7, fontSize: 16, fontFace: F, color: TEXT, valign: "top", paraSpaceAfter: 12, lineSpacingMultiple: 1.2, margin: 0 });
  if (i < 2) hArrow(s, x + w + 0.03, y + h / 2, 0.3);
});
s.addText("AR 让“卡上的精灵活过来”第一次在普通手机上可行：识别一张卡，只需要摄像头和图像跟踪。", { x: M, y: 6.4, w: W - 2 * M, h: 0.4, fontSize: 14, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
footer(s, n);

/* ============ 灵感②：塔罗 × 奶蛙 → 四色角色卡 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "01 灵感来源");
title(s, "灵感②：塔罗牌构图 × 奶蛙形象 → 四色角色卡", "左：韦特塔罗原牌（1909，公有领域）　右：本项目定稿卡面（66×94 mm，零文字）");
const pairs = [
  ["classic", "星币侍从", "暖黄 · 得意"],
  ["calm", "倒吊人 XII", "湖蓝 · 佛系"],
  ["rage", "权杖骑士", "朱红 · 愤怒"],
  ["divine", "教皇 V", "紫 · 神格"],
];
// 每组宽 2.98in：原牌 0.95in 宽 + 箭头 0.28in + 定稿卡 1.75in 宽
const gW = 2.98, gGap = (W - 2 * M - 4 * gW) / 3;
pairs.forEach((it, i) => {
  const x0 = M + i * (gW + gGap), y = 1.55, ch = 2.5;
  const [tpw, tph] = SIZES[`tarot_${it[0]}.jpg`];
  const tw = 0.95, th = tw * tph / tpw;
  img(s, `tarot_${it[0]}.jpg`, x0, y + ch - th, tw, th);
  hArrow(s, x0 + tw + 0.04, y + ch - th / 2, 0.2, C[it[0]]);
  const cw = imgH(s, `card_${it[0]}.jpg`, x0 + tw + 0.28, y, ch);
  void cw;
  s.addShape(p.shapes.RECTANGLE, { x: x0, y: y + ch + 0.2, w: gW, h: 0.06, fill: { color: C[it[0]] }, line: { type: "none" } });
  s.addText(it[1], { x: x0, y: y + ch + 0.32, w: gW, h: 0.35, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
  s.addText(it[2], { x: x0, y: y + ch + 0.65, w: gW, h: 0.4, fontSize: 16, bold: true, fontFace: F, color: C[it[0]], margin: 0 });
});
s.addText("每张卡面画的是该色奶蛙的招牌动作，同时决定了它的性格与满值专属演出。", { x: M, y: 5.35, w: W - 2 * M, h: 0.4, fontSize: 13, fontFace: F, color: TEXT, margin: 0 });
card(s, M, 6.25, W - 2 * M, 0.7, TINT);
s.addText([
  { text: "塔罗给了结构：", options: { bold: true, color: PRIM_DEEP } },
  { text: "一张卡 = 一个角色 + 一种性格 + 一个招牌姿态。" },
  { text: "　奶蛙给了形象：", options: { bold: true, color: PRIM_DEEP } },
  { text: "同一副身体换颜色、换表情，就是不同的精灵。" },
], { x: M + 0.25, y: 6.25, w: W - 2 * M - 0.5, h: 0.7, fontSize: 13.5, fontFace: F, color: TEXT, valign: "middle", margin: 0 });
footer(s, n);

/* ============ 灵感③：思路演进 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "01 灵感来源");
title(s, "灵感③：一条思路，逐步长成 AR 精灵馆", "奶蛙本身就有蛋、天使、冥想、警察等多种形态，天然适合做“收集与成长”");
const steps = [
  ["塔罗牌", "一卡一角色\n统一牌义", null],
  ["童年精灵卡", "能换、能拍\n不能真对战", null],
  ["奶蛙多形态", "同一角色\n多种形态", "form"],
  ["桌上有龙", "本项目：桌面\nAR 萌宠（已实现）", "real_single_card.jpg"],
  ["AR 精灵馆", "收集 · 对战 · 收服\n（未来展望）", null],
];
const sw = 2.2, sg = 0.33, sy = 1.55, sh = 4.1;
steps.forEach((it, i) => {
  const x = M + i * (sw + sg);
  const hi = i === 3, fut = i === 4;
  card(s, x, sy, sw, sh, hi ? "F3E6C8" : (fut ? "EDE6F5" : TINT2));
  s.addText(String(i + 1), { x: x + 0.2, y: sy + 0.15, w: 0.5, h: 0.5, fontSize: 22, bold: true, fontFace: F, color: fut ? C.divine : PRIMARY, margin: 0 });
  s.addText(it[0], { x: x + 0.2, y: sy + 0.65, w: sw - 0.3, h: 0.45, fontSize: 17, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: x + 0.2, y: sy + 1.1, w: sw - 0.3, h: 0.75, fontSize: 12.5, fontFace: F, color: MUTED, lineSpacingMultiple: 1.15, margin: 0, valign: "top" });
  const iy = sy + 1.95, ih = 2.0;
  if (i === 0) imgH(s, "tarot_calm.jpg", x + (sw - ih * 514 / 900) / 2, iy, ih);
  if (i === 1) {
    // 用三张错落的卡片示意“一叠卡”
    [0, 1, 2].forEach((k) => s.addShape(p.shapes.ROUNDED_RECTANGLE, {
      x: x + 0.45 + k * 0.22, y: iy + 0.2 + k * 0.15, w: 0.95, h: 1.4,
      fill: { color: ["D9CBB0", "E6D8BC", "F2E5C9"][k] }, line: { color: "B5A48A", width: 1 }, rectRadius: 0.06, rotate: -8 + k * 8,
    }));
  }
  if (i === 2) {
    const fh = 1.7, fw = fh * 408 / 720;
    imgH(s, "form_egg.jpg", x + (sw - 2 * fw - 0.08) / 2, iy + 0.15, fh);
    imgH(s, "form_angel.jpg", x + (sw - 2 * fw - 0.08) / 2 + fw + 0.08, iy + 0.15, fh);
  }
  if (i === 3) imgH(s, "real_single_card.jpg", x + (sw - ih * 638 / 1400) / 2, iy, ih);
  if (i === 4) {
    ["classic", "calm", "rage", "divine"].forEach((k, j) => s.addShape(p.shapes.OVAL, {
      x: x + 0.3 + (j % 2) * 0.85, y: iy + 0.15 + Math.floor(j / 2) * 0.85, w: 0.7, h: 0.7, fill: { color: C[k] }, line: { type: "none" },
    }));
    s.addShape(p.shapes.OVAL, { x: x + 0.78, y: iy + 0.58, w: 0.6, h: 0.6, fill: { color: "FFFFFF" }, line: { color: TEXT, width: 1 } });
    s.addText("VS", { x: x + 0.78, y: iy + 0.58, w: 0.6, h: 0.6, fontSize: 16, bold: true, fontFace: F, color: TEXT, align: "center", valign: "middle", margin: 0 });
  }
  if (i < 4) hArrow(s, x + sw + 0.03, sy + sh / 2, sg - 0.06);
});
s.addText("第 3 步图片为奶蛙网络参考素材（xinyewebsite.com）：只用来说明灵感，不是本项目资产；第 4 步为本项目真机截图。", { x: M, y: 5.85, w: W - 2 * M, h: 0.35, fontSize: 12, fontFace: F, color: MUTED, margin: 0 });
s.addText("先把“一只精灵站在一张卡上”做扎实，再往对战和体验馆扩展。", { x: M, y: 6.3, w: W - 2 * M, h: 0.4, fontSize: 14, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
footer(s, n);

/* ============ 宝可梦 GO：事实 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "02 反思：宝可梦 GO");
title(s, "宝可梦 GO：现象级爆红，随后大规模流失", "以下为公开报道中的事实，检索日期 2026-10-30");
const facts = [
  ["1.3 亿", "首月下载量", "2016-07 上线，首月即创\n吉尼斯纪录"],
  ["-79%", "美国玩家流失", "美国使用量 7 月 15 日见顶，\n到 9 月中旬已流失 79%"],
  ["2016-08", "开始永久封号", "针对虚拟定位、模拟器、\n改版客户端与刷怪脚本"],
  ["2016-09", "封禁 root / 越狱设备", "为遏制作弊，直接禁止这类设备登录游戏"],
];
facts.forEach((it, i) => {
  const x = M + i * 3.13, w = 2.9, y = 1.6, h = 3.4;
  card(s, x, y, w, h, i < 1 ? TINT2 : TINT);
  s.addText(it[0], { x: x + 0.25, y: y + 0.35, w: w - 0.4, h: 0.9, fontSize: 34, bold: true, fontFace: F, color: i === 0 ? ACCENT : C.rage, margin: 0 });
  s.addText(it[1], { x: x + 0.25, y: y + 1.35, w: w - 0.4, h: 0.45, fontSize: 16, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2], { x: x + 0.25, y: y + 1.9, w: w - 0.45, h: 1.3, fontSize: 12.5, fontFace: F, color: MUTED, valign: "top", lineSpacingMultiple: 1.2, margin: 0 });
});
card(s, M, 5.3, W - 2 * M, 0.95, "F4F1EC");
s.addText([
  { text: "说明　", options: { bold: true, color: TEXT } },
  { text: "游戏至今仍在运营，2025 年随 Niantic 游戏业务售予 Scopely。本页讨论的“失败”指：大多数早期玩家很快离开，官方从第二个月起就陷入与作弊者的长期对抗。" },
], { x: M + 0.25, y: 5.3, w: W - 2 * M - 0.5, h: 0.95, fontSize: 12.5, fontFace: F, color: MUTED, valign: "middle", lineSpacingMultiple: 1.2, margin: 0 });
s.addText("来源：Wikipedia「Pokémon Go」（含 Forbes 引述）；The Verge，2016-08-12，“Niantic is now permanently banning Pokémon Go cheaters”", { x: M, y: 6.45, w: W - 2 * M, h: 0.35, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
footer(s, n);

/* ============ 宝可梦 GO：我们的分析 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "02 反思：宝可梦 GO");
title(s, "我们的看法：问题出在两处", "以下是项目组的分析观点");
const causes = [
  ["原因一", "放给用户的权限太多", [
    "位置、走路距离都由玩家自己的手机上报，服务器只能相信它",
    "虚拟定位、改版客户端、机器人自动刷，不出门就能抓稀有精灵、占道馆",
    "官方只能事后封号，猫鼠游戏一直持续",
    "认真玩的人觉得不公平，最先离开",
  ], C.rage],
  ["原因二", "体验停留在手机屏幕里", [
    "AR 模式只是把精灵叠在相机画面上，精灵和真实环境没有互动",
    "玩家一路低头看屏幕：在手机里玩，而不是在现实里玩",
    "分心还带来安全问题：日本到 2016-08 底已记录 79 起相关交通事故",
  ], C.calm],
];
causes.forEach((it, i) => {
  const x = M + i * 6.25, w = 6.0, y = 1.55, h = 3.6;
  card(s, x, y, w, h, TINT2);
  s.addShape(p.shapes.RECTANGLE, { x, y, w: 0.09, h, fill: { color: it[3] }, line: { type: "none" } });
  s.addText(it[0], { x: x + 0.35, y: y + 0.25, w: 1.5, h: 0.35, fontSize: 13, bold: true, fontFace: F, color: it[3], margin: 0 });
  s.addText(it[1], { x: x + 0.35, y: y + 0.6, w: w - 0.6, h: 0.5, fontSize: 20, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2].map((t) => ({ text: t, options: { bullet: bu(), breakLine: true } })),
    { x: x + 0.35, y: y + 1.2, w: w - 0.55, h: h - 1.35, fontSize: 15, fontFace: F, color: TEXT, valign: "top", paraSpaceAfter: 9, lineSpacingMultiple: 1.15, margin: 0 });
});
card(s, M, 5.4, W - 2 * M, 1.4, "F3E6C8");
s.addText("对我们的启示", { x: M + 0.3, y: 5.5, w: 3, h: 0.35, fontSize: 14, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
s.addText([
  { text: "①  关键判定不依赖用户能伪造的数据：", options: { bold: true } },
  { text: "精灵从实体卡上召唤，卡必须真的在摄像头前，规则在本地统一结算。", options: { breakLine: true } },
  { text: "②  把体验搬出手机屏幕：", options: { bold: true } },
  { text: "受控的线下场地 + AR 眼镜 + 实体道具，人和精灵在同一个空间里互动。" },
], { x: M + 0.3, y: 5.85, w: W - 2 * M - 0.6, h: 0.9, fontSize: 13.5, fontFace: F, color: TEXT, lineSpacingMultiple: 1.25, margin: 0 });
footer(s, n);

/* ============ 已实现功能总览 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "03 已实现的功能");
title(s, "已实现：四张卡、四只奶蛙，在真实桌面上互动", "主线：摆卡召唤 → 触屏或语音逗它 → 开心值满触发专属演出 → 两卡靠近一起大笑 → 本地存档");
card(s, M, 1.5, 6.3, 4.65, "3A3026");
img(s, "real_four_cards.jpg", M + 0.15, 1.65, 6.0, 6.0 * 892 / 1400);
s.addText("真机截图：四卡同屏（同场测试实测 60.4 FPS）", { x: M, y: 5.6, w: 6.3, h: 0.4, fontSize: 11, fontFace: F, color: "C9BFA9", align: "center", margin: 0 });
const feats = [
  ["四卡识别与同屏", "Vuforia 同时跟踪 4 张卡，四卡同屏实测 60.4 FPS", "真机已测", ACCENT],
  ["每卡一只独立奶蛙", "独立开心值与行为状态机；丢失即隐藏，恢复后数值不变", "真机已测", ACCENT],
  ["单击 / 双击逗弄", "双击只算一次奖励；四色加分各不相同", "真机已测", ACCENT],
  ["满值专属演出 ×4", "开心值到 100% 播该色招牌动作与表情，再回落、冷却", "真机已测", ACCENT],
  ["离线语音口令 ×4", "按住说话，所有在场奶蛙同时回应；无需联网", "部分已测", PRIMARY],
  ["两卡靠近共同大笑", "距离、高差、夹角、停留联合判定，整组同时开始", "修复后待复测", MUTED],
  ["二维嘴眼表情 · 本地存档", "不改模型的表情贴片；按宠物 ID 版本化 JSON 存档", "已接入", ACCENT],
];
feats.forEach((it, i) => {
  const y = 1.5 + i * 0.68, x = 7.1;
  s.addText(it[0], { x, y, w: 3.8, h: 0.32, fontSize: 14, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x, y: y + 0.31, w: 5.7, h: 0.3, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
  tag(s, W - M - 1.35, y + 0.01, it[2], it[3], 1.35);
  if (i < feats.length - 1) s.addShape(p.shapes.LINE, { x, y: y + 0.64, w: 5.73, h: 0, line: { color: LINE, width: 0.75 } });
});
s.addText("测试设备：iQOO Z9 Turbo / Android 15；Unity EditMode 自动化测试 273 / 273 通过（2026-10-30）", { x: M, y: 6.4, w: W - 2 * M, h: 0.35, fontSize: 12, fontFace: F, color: PRIM_DEEP, bold: true, margin: 0 });
footer(s, n);

/* ============ 卡片识别设计 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "03 已实现的功能");
title(s, "重点①：卡片识别设计", "卡片既是印刷品，也是精灵的身份和输入设备");
["classic", "calm", "rage", "divine"].forEach((k, i) => imgH(s, `card_${k}.jpg`, M + i * 1.38, 1.5, 1.85));
const cardDesign = [
  ["卡面即识别图", "66×94 mm、零文字、哑光打印；每张卡画的是该色奶蛙的招牌动作，\n四张构图不同，减少互相认错（Vuforia 按灰度特征识别）"],
  ["一卡一身份", "每张卡绑定稳定的 PetId，运行状态和存档都按 ID 走；同一张卡拿走再放回，还是那只奶蛙"],
  ["卡与卡的位置就是输入", "两卡水平距离 ≤ 0.18 m、高差 ≤ 0.035 m、夹角 ≤ 20°、停留 0.6 s → 触发共同大笑；拉开到 0.23 m 以外才算分开"],
  ["丢失即停", "卡被挡住或移出画面，该宠立即隐藏、不能互动，不使用过期位置"],
];
cardDesign.forEach((it, i) => {
  const y = 3.55 + i * 0.78;
  s.addShape(p.shapes.OVAL, { x: M, y: y + 0.05, w: 0.22, h: 0.22, fill: { color: PRIMARY }, line: { type: "none" } });
  s.addText(it[0], { x: M + 0.35, y, w: 2.3, h: 0.32, fontSize: 13.5, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: M + 0.35, y: y + 0.3, w: 6.9, h: 0.45, fontSize: 12, fontFace: F, color: MUTED, lineSpacingMultiple: 1.05, margin: 0, valign: "top" });
});
// 右：对应到体验馆
const rx = 8.0, rw = W - M - rx;
card(s, rx, 1.5, rw, 5.25, "EDE6F5");
s.addText("到了 AR 精灵馆", { x: rx + 0.3, y: 1.65, w: rw - 0.6, h: 0.45, fontSize: 18, bold: true, fontFace: F, color: C.divine, margin: 0 });
const cardFuture = [
  ["出牌 = 召唤", "把精灵卡放上对战台，精灵出场；卡就是登录凭证"],
  ["相向放置 = 开战", "沿用现有接近规则：两卡正对、距离合适即开战；\n三卡靠近即组队"],
  ["收服 = 拿到新卡", "击败 NPC 后获得数字卡，可在纪念区打印成实体卡带走"],
  ["卡不在场就不能出招", "必须人在、卡在摄像头前，远程虚拟定位那一套行不通"],
];
cardFuture.forEach((it, i) => {
  const y = 2.25 + i * 1.1;
  s.addText(it[0], { x: rx + 0.3, y, w: rw - 0.6, h: 0.35, fontSize: 14, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: rx + 0.3, y: y + 0.36, w: rw - 0.6, h: 0.65, fontSize: 12, fontFace: F, color: MUTED, lineSpacingMultiple: 1.12, margin: 0, valign: "top" });
});
s.addText("识别评级：classic 卡\nTarget Manager 4 星\n其余三张待记录", { x: M + 5.65, y: 2.45, w: 1.8, h: 0.9, fontSize: 12, fontFace: F, color: MUTED, margin: 0, valign: "bottom" });
footer(s, n);

/* ============ 语音交互 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "03 已实现的功能");
title(s, "重点②：离线语音口令", "按住“按住说话”，喊一句口令，画面里所有在场的奶蛙一起回应");
const pipe = ["按住说话\n按钮", "手机麦克风\n录音", "sherpa-onnx\n离线关键词", "口令规则\n每只在场奶蛙\n各发一个事件", "行为状态机\n唯一结算点", "动作、表情\n与配音"];
const pw = 1.08, pg = 0.17, py = 1.55;
pipe.forEach((t, i) => {
  const x = M + i * (pw + pg);
  card(s, x, py, pw, 1.15, i === 2 ? "F3E6C8" : TINT2);
  s.addText(t, { x: x + 0.04, y: py, w: pw - 0.08, h: 1.15, fontSize: 10.5, fontFace: F, color: TEXT, align: "center", valign: "middle", bold: i === 2, margin: 0 });
  if (i < pipe.length - 1) hArrow(s, x + pw + 0.01, py + 0.575, pg - 0.02);
});
const hdr = (t) => ({ text: t, options: { bold: true, color: "FFFFFF", fill: { color: PRIM_DEEP } } });
s.addTable([
  [hdr("口令"), hdr("奶蛙的回应"), hdr("表情")],
  ["安迪", "挥手打招呼（classic_proud）", "眯眼微笑"],
  ["嘎嘎爹拉筛", "小跳一下（TouchReact）", "O 形嘴"],
  ["雷霆大笑", "后仰大笑（social_laugh）", "眯眼大笑"],
  ["欧拉", "叉腰怒吼（rage_roar）", "怒眼怒吼"],
], { x: M, y: 2.95, w: 7.3, colW: [1.6, 3.9, 1.8], fontFace: F, fontSize: 12.5, color: TEXT, rowH: 0.38, border: { type: "solid", pt: 0.75, color: LINE }, fill: { color: "FFFFFF" }, valign: "middle", margin: [0.03, 0.08, 0.03, 0.08] });
s.addText([
  { text: "规则：", options: { bold: true } },
  { text: "每次回应等于一次单击加分，可推动满值演出；同一只 3 秒冷却", options: { breakLine: true } },
  { text: "技术：", options: { bold: true } },
  { text: "zipformer 中文模型（3.3M 参数，int8）在手机本地运行，断网可用，录音不上传", options: { breakLine: true } },
  { text: "真机：", options: { bold: true } },
  { text: "“安迪”两次命中，开心值 70% → 82%；其余三句待复测" },
], { x: M, y: 5.05, w: 7.3, h: 1.3, fontSize: 12.5, fontFace: F, color: TEXT, lineSpacingMultiple: 1.25, margin: 0, valign: "top" });
imgH(s, "real_ui_voice.jpg", 8.0, 1.5, 3.6 * 1.25 > 4.9 ? 4.9 : 3.6 * 1.25);
const vx = 10.15, vw = W - M - vx;
card(s, vx, 1.5, vw, 5.25, "EDE6F5");
s.addText("到了 AR 精灵馆", { x: vx + 0.2, y: 1.62, w: vw - 0.3, h: 0.4, fontSize: 15, bold: true, fontFace: F, color: C.divine, margin: 0 });
const voiceFuture = [
  ["喊技能名出招", "对战回合里直接喊出技能"],
  ["眼镜里的主要输入", "戴 AR 眼镜、手里拿卡时不方便点屏幕"],
  ["对奶蛋说“你好”", "孵化花园里靠陪伴积累孵化值"],
  ["离线识别", "场馆断网也能用，语音不出设备"],
];
voiceFuture.forEach((it, i) => {
  const y = 2.12 + i * 1.12;
  s.addText(it[0], { x: vx + 0.2, y, w: vw - 0.3, h: 0.33, fontSize: 12.5, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[1], { x: vx + 0.2, y: y + 0.34, w: vw - 0.3, h: 0.7, fontSize: 11, fontFace: F, color: MUTED, lineSpacingMultiple: 1.1, margin: 0, valign: "top" });
});
s.addText("真机界面截图", { x: 8.0, y: 6.05, w: 2.1, h: 0.3, fontSize: 10, fontFace: F, color: MUTED, align: "center", margin: 0 });
footer(s, n);

/* ============ 行为与演出 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "03 已实现的功能");
title(s, "行为状态驱动：四种性格，四段专属演出", "同一套骨架和动画管线，换颜色、换动作、换表情，就是一只新精灵（下图为 Blender 动作预览，统一用暖黄材质渲染）");
const acts = [
  ["act_classic.jpg", "暖黄 · 打招呼", "2.0 s", C.classic],
  ["act_calm.jpg", "湖蓝 · 合十点头", "2.5 s", C.calm],
  ["act_rage.jpg", "朱红 · 叉腰怒吼", "2.0 s", C.rage],
  ["act_divine.jpg", "紫 · 水平展翅", "2.5 s", C.divine],
  ["act_laugh.jpg", "四色共用 · 共同大笑", "2.4 s", PRIM_DEEP],
];
acts.forEach((it, i) => {
  const x = M + i * 2.48, h = 2.55;
  imgH(s, it[0], x, 1.5, h);
  s.addShape(p.shapes.RECTANGLE, { x, y: 1.5 + h, w: h * 480 / 528, h: 0.06, fill: { color: it[3] }, line: { type: "none" } });
  s.addText(it[1], { x, y: 1.5 + h + 0.12, w: 2.4, h: 0.35, fontSize: 13, bold: true, fontFace: F, color: it[3], margin: 0 });
  s.addText(it[2], { x, y: 1.5 + h + 0.45, w: 2.4, h: 0.3, fontSize: 11, fontFace: F, color: MUTED, margin: 0 });
});

const cyc = ["蓄力\n点按 / 语音加分", "满值\n播专属演出", "回落\n开心值下降", "冷却\n不可重复触发", "重新武装\n再次点满才播"];
cyc.forEach((t, i) => {
  const x = M + i * 2.48, w = 2.32, y = 5.0;
  card(s, x, y, w, 0.95, i === 1 ? "F3E6C8" : TINT2);
  s.addText(t, { x, y, w, h: 0.95, fontSize: 12, fontFace: F, color: TEXT, align: "center", valign: "middle", margin: 0 });
  if (i < cyc.length - 1) hArrow(s, x + w + 0.01, y + 0.475, 0.14);
});
s.addText("所有数值只在行为层的一个结算点改变；动画和计时器不会重复加分。→ 到体验馆：专属演出 = 大招动画，共同大笑 = 合体技。", { x: M, y: 6.2, w: W - 2 * M, h: 0.5, fontSize: 12.5, fontFace: F, color: PRIM_DEEP, bold: true, margin: 0 });
footer(s, n);

/* ============ 映射总表 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "04 功能如何对应体验馆");
title(s, "已实现的每项能力，在 AR 精灵馆里都有位置", "扩展是在现有系统上加层，不需要推倒重做");
const hdr2 = (t) => ({ text: t, options: { bold: true, color: "FFFFFF", fill: { color: PRIM_DEEP } } });
const hl = (t) => ({ text: t, options: { bold: true, color: PRIM_DEEP, fill: { color: "F3E6C8" } } });
const hlp = (t) => ({ text: t, options: { fill: { color: "F3E6C8" } } });
s.addTable([
  [hdr2("已实现（课程项目）"), hdr2("关键机制"), hdr2("在 AR 精灵馆中的作用")],
  [hl("实体卡识别"), hlp("Vuforia 四卡同屏，卡 ↔ 稳定 PetId"), hlp("精灵卡 = 精灵身份；出牌召唤；收服后打印实体卡")],
  [hl("离线语音口令"), hlp("本地关键词识别 → 每只在场精灵一个事件"), hlp("喊技能名出招；AR 眼镜下免手操作；对奶蛋说话孵化")],
  ["两卡接近判定", "距离 / 高差 / 夹角 / 停留 + 迟滞", "两卡相向放置即开战；三卡靠近即组队"],
  ["开心值与满值周期", "加分、满值、回落、冷却、重新武装", "亲密度：影响暴击与“听不听话”；奶蛋孵化值"],
  ["四色专属演出", "每色一段招牌动作 + 招牌表情", "大招动画；四色对应属性克制"],
  ["多宠组协调（共同大笑）", "整组同帧开始，任一成员离开整组取消", "合体技；多人组队 Boss 战的同步演出"],
  ["丢失即停、不用过期位置", "跟踪状态机，逐宠隐藏与恢复", "对战公平：卡不在场不能出招"],
  ["本地版本化存档", "schema 2，按 PetId 保存，可迁移旧档", "精灵图鉴、等级、收服记录；离馆后同步回手机"],
], { x: M, y: 1.5, w: W - 2 * M, colW: [2.9, 4.4, 5.03], fontFace: F, fontSize: 12.5, color: TEXT, rowH: 0.52, border: { type: "solid", pt: 0.75, color: LINE }, fill: { color: "FFFFFF" }, valign: "middle", margin: [0.04, 0.1, 0.04, 0.1] });
footer(s, n);

/* ============ 未来①：形态与属性 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "05 未来展望（未实现）");
title(s, "未来①：精灵的形态、成长与属性", "设想阶段，数字和规则都是草案");
const forms = [
  ["奶蛋", "初始形态，不能战斗；靠点按、说话积累孵化值", "体验馆入口发放 / 新手卡"],
  ["四色奶蛙", "现有的暖黄、湖蓝、朱红、紫四种性格", "孵化时的互动方式决定颜色"],
  ["进化形态", "每色一条进化线，外观和技能增强", "亲密度满 + 对战胜场达标"],
  ["稀有形态", "如“金色奶蛙”“暗影奶蛙”", "只能击败特定 NPC / Boss 收服"],
];
forms.forEach((it, i) => {
  const y = 1.55 + i * 1.22, x = M, w = 6.5;
  card(s, x, y, w, 1.05, i === 1 ? "F3E6C8" : TINT2);
  s.addText(it[0], { x: x + 0.25, y, w: 1.6, h: 1.05, fontSize: 17, bold: true, fontFace: F, color: TEXT, valign: "middle", margin: 0 });
  s.addText(it[1], { x: x + 1.9, y: y + 0.15, w: w - 2.1, h: 0.4, fontSize: 12.5, fontFace: F, color: TEXT, margin: 0 });
  s.addText("获得：" + it[2], { x: x + 1.9, y: y + 0.55, w: w - 2.1, h: 0.35, fontSize: 11.5, fontFace: F, color: MUTED, margin: 0 });
  if (i < 3) vArrow(s, x + 0.9, y + 1.05, 0.17);
});
// 克制环
const cx = 9.6, cy = 3.95, r = 1.4, d = 1.25;
const ring = [["rage", "朱红\n火 · 怒", -90], ["divine", "紫\n神 · 灵", 30], ["calm", "湖蓝\n水 · 静", 150]];
const pos = ring.map((it) => [cx + r * Math.cos(it[2] * Math.PI / 180), cy + r * Math.sin(it[2] * Math.PI / 180)]);
for (let i = 0; i < 3; i++) {
  const [x1, y1] = pos[i], [x2, y2] = pos[(i + 1) % 3];
  const k = (d / 2 + 0.08) / Math.hypot(x2 - x1, y2 - y1);
  const ax = x1 + (x2 - x1) * k, ay = y1 + (y2 - y1) * k, bx = x2 - (x2 - x1) * k, by = y2 - (y2 - y1) * k;
  s.addShape(p.shapes.LINE, { x: Math.min(ax, bx), y: Math.min(ay, by), w: Math.abs(bx - ax), h: Math.abs(by - ay), flipH: bx < ax, flipV: by < ay, line: { color: PRIM_DEEP, width: 2.2, endArrowType: "triangle" } });
}
ring.forEach((it, i) => {
  const [x, y] = pos[i];
  s.addShape(p.shapes.OVAL, { x: x - d / 2, y: y - d / 2, w: d, h: d, fill: { color: C[it[0]] }, line: { type: "none" } });
  s.addText(it[1], { x: x - d / 2, y: y - d / 2, w: d, h: d, fontSize: 13, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
});
s.addShape(p.shapes.OVAL, { x: 11.75, y: 5.55, w: 1.05, h: 1.05, fill: { color: C.classic }, line: { type: "none" } });
s.addText("暖黄\n平衡", { x: 11.75, y: 5.55, w: 1.05, h: 1.05, fontSize: 12, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
s.addText("属性克制（箭头指向被克制的一方）", { x: 7.4, y: 1.55, w: 5.4, h: 0.35, fontSize: 13, bold: true, fontFace: F, color: TEXT, margin: 0 });
s.addText("暖黄为平衡型：\n不克制别人，也不被克制", { x: 7.4, y: 5.7, w: 4.2, h: 0.8, fontSize: 12, fontFace: F, color: MUTED, margin: 0, align: "right" });
footer(s, n);

/* ============ 未来②：对战规则 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "05 未来展望（未实现）");
title(s, "未来②：卡牌对战规则（草案）", "补上童年精灵卡缺的那一块：真正能打、规则统一");
const rules = [
  ["开战", "双方把精灵卡相向放在桌面上；系统识别两卡正对、距离合适即进入对战（沿用现有接近规则）"],
  ["回合", "每回合各选一个技能：攻击、防守、特技。可以点屏幕，也可以直接喊技能名（沿用语音口令）"],
  ["数值", "生命、攻击、速度三项 + 属性克制倍率；亲密度越高，暴击率越高、越不容易“不听话”"],
  ["胜负与收服", "把对方生命打到 0 即胜；击败 NPC 后有概率收服其精灵，获得一张数字卡，可打印成实体卡"],
  ["公平", "真人之间只打友谊赛，不夺走对方精灵；收服只限 NPC 与 Boss。判定由程序完成，不靠口头约定"],
];
rules.forEach((it, i) => {
  const y = 1.55 + i * 0.98;
  s.addShape(p.shapes.OVAL, { x: M, y: y + 0.08, w: 0.62, h: 0.62, fill: { color: i === 4 ? ACCENT : PRIMARY }, line: { type: "none" } });
  s.addText(String(i + 1), { x: M, y: y + 0.08, w: 0.62, h: 0.62, fontSize: 18, bold: true, fontFace: F, color: "FFFFFF", align: "center", valign: "middle", margin: 0 });
  s.addText(it[0], { x: M + 0.85, y: y + 0.12, w: 1.8, h: 0.5, fontSize: 17, bold: true, fontFace: F, color: TEXT, valign: "middle", margin: 0 });
  card(s, M + 2.7, y, W - 2 * M - 2.7, 0.8, i % 2 ? TINT2 : TINT);
  s.addText(it[1], { x: M + 2.95, y, w: W - 2 * M - 3.15, h: 0.8, fontSize: 13, fontFace: F, color: TEXT, valign: "middle", margin: 0 });
  if (i < 4) vArrow(s, M + 0.31, y + 0.72, 0.24, PRIM_DEEP);
});
s.addText("这里的“收服”和童年拍卡“赢了就把对方的卡拿走”是一回事，只是规则由系统统一裁决。", { x: M, y: 6.5, w: W - 2 * M, h: 0.35, fontSize: 12.5, fontFace: F, color: PRIM_DEEP, bold: true, margin: 0 });
footer(s, n);

/* ============ 未来③：体验馆动线 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "05 未来展望（未实现）");
title(s, "未来③：沉浸式 AR 精灵体验馆", "戴上 AR 眼镜走进场地：领蛋、孵化、挑战道馆、收服伙伴、打败 Boss");
const zones = [
  ["① 迎宾区", "领取 AR 眼镜和初始奶蛋卡，\n看 1 分钟引导动画", "5 min"],
  ["② 孵化花园", "陪奶蛋说话、点按，孵出第一只奶蛙", "10 min"],
  ["③ 训练道馆 ×3", "每馆一位 NPC 训练师，各擅长一种颜色；赢了拿徽章、有机会收服", "每馆 8～10 min"],
  ["⑥ 纪念区", "打印收服的精灵卡，生成本次冒险回顾短片", "5 min"],
  ["⑤ Boss 殿堂", "集齐三枚徽章开放；最终 Boss “奶蛙王”多阶段战斗，可组队", "15 min"],
  ["④ 野外草丛", "场地里藏着标记，靠近会刷出\n野生精灵，可以对战收服", "自由探索"],
];
const zw = 3.85, zh = 1.75, zg = 0.39;
zones.forEach((it, i) => {
  const row = Math.floor(i / 3), col = i % 3;
  const x = M + col * (zw + zg), y = 1.55 + row * (zh + 0.5);
  const boss = it[0].includes("Boss");
  card(s, x, y, zw, zh, boss ? "EDE6F5" : (row === 0 ? TINT2 : TINT));
  s.addText(it[0], { x: x + 0.25, y: y + 0.15, w: zw - 1.5, h: 0.42, fontSize: 16, bold: true, fontFace: F, color: boss ? C.divine : TEXT, margin: 0 });
  s.addText(it[2], { x: x + zw - 1.45, y: y + 0.17, w: 1.25, h: 0.38, fontSize: 11, fontFace: F, color: MUTED, align: "right", margin: 0 });
  s.addText(it[1], { x: x + 0.25, y: y + 0.65, w: zw - 0.45, h: 0.95, fontSize: 12.5, fontFace: F, color: TEXT, valign: "top", lineSpacingMultiple: 1.15, margin: 0 });
});
hArrow(s, M + zw + 0.04, 1.55 + zh / 2, zg - 0.08);
hArrow(s, M + 2 * zw + zg + 0.04, 1.55 + zh / 2, zg - 0.08);
vArrow(s, M + 2 * (zw + zg) + zw / 2, 1.55 + zh + 0.04, 0.42);
const y2 = 1.55 + zh + 0.5 + zh / 2;
s.addShape(p.shapes.LINE, { x: M + 2 * (zw + zg) - zg + 0.04, y: y2, w: zg - 0.08, h: 0, flipH: true, line: { color: PRIM_DEEP, width: 2.2, endArrowType: "triangle" } });
s.addShape(p.shapes.LINE, { x: M + zw + 0.04, y: y2, w: zg - 0.08, h: 0, flipH: true, line: { color: PRIM_DEEP, width: 2.2, endArrowType: "triangle" } });
card(s, M, 5.95, W - 2 * M, 0.85, "F4F1EC");
s.addText([
  { text: "硬件与技术设想　", options: { bold: true, color: TEXT } },
  { text: "光学透视 AR 眼镜（低成本版先用平板）；实体卡继续用图像跟踪，场地用预先扫描的空间锚点；NPC 可以是真人演员 + AR 精灵；\nBoss 战搭配灯光和音效；场馆服务器保存进度，离馆后同步回手机。" },
], { x: M + 0.25, y: 5.95, w: W - 2 * M - 0.5, h: 0.85, fontSize: 12, fontFace: F, color: MUTED, valign: "middle", lineSpacingMultiple: 1.2, margin: 0 });
footer(s, n);

/* ============ 体验馆如何回应宝可梦 GO ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "05 未来展望（未实现）");
title(s, "AR 精灵馆如何回应宝可梦 GO 的两个问题", "左列为公开事实，右列为我们的设计设想");
const hdr3 = (t, c) => ({ text: t, options: { bold: true, color: "FFFFFF", fill: { color: c } } });
const b = (t) => ({ text: t, options: { bold: true } });
s.addTable([
  [hdr3("对比项", PRIM_DEEP), hdr3("宝可梦 GO", "8C8172"), hdr3("AR 精灵馆（设想）", C.divine)],
  [b("位置和输入由谁说了算"), "玩家手机上报 GPS，可以被虚拟定位", "实体卡 + 场馆固定标记：人必须在场，卡必须在桌上"],
  [b("作弊空间"), "改版客户端、模拟器、机器人脚本；官方只能事后封号", "设备由场馆发放和管理，对战判定在场馆系统内完成"],
  [b("规则"), "线上规则统一，但能被外挂绕开", "程序统一裁决，补上童年精灵卡“没有统一规则”的缺口"],
  [b("沉浸感"), "低头看手机；AR 是相机画面加叠层", "AR 眼镜 + 场景布置 + NPC 演员 + 灯光音效"],
  [b("社交"), "以各自看手机为主", "面对面出卡对战，组队打 Boss"],
  [b("代价"), "开放世界，覆盖面广", "需要线下场地，单次体验约 60 分钟，覆盖人数有限"],
], { x: M, y: 1.5, w: W - 2 * M, colW: [2.6, 4.6, 5.13], fontFace: F, fontSize: 13, color: TEXT, rowH: 0.62, border: { type: "solid", pt: 0.75, color: LINE }, fill: { color: "FFFFFF" }, valign: "middle", margin: [0.04, 0.1, 0.04, 0.1] });
s.addText("把“权限”从用户手机收回到实体卡和场地上，把体验从手机屏幕搬到真实空间里。", { x: M, y: 6.3, w: W - 2 * M, h: 0.4, fontSize: 14, bold: true, fontFace: F, color: PRIM_DEEP, margin: 0 });
footer(s, n);

/* ============ 路线图与风险 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "05 未来展望（未实现）");
title(s, "落地路线与主要风险", "先在手机和平板上验证玩法，再上 AR 眼镜和线下场馆");
const stages = [
  ["阶段一", "单机桌宠", "四色奶蛙 · 触屏互动\n满值演出 · 共同大笑\n语音口令", "本课程项目，已完成"],
  ["阶段二", "双人卡牌对战", "两部手机同时识别\n局域网同步回合", ""],
  ["阶段三", "孵化与收服", "奶蛋孵化 · 进化线\n图鉴 · NPC 对战 AI", ""],
  ["阶段四", "体验馆原型", "小场地 + 3 道馆 + Boss\n先用平板试运营", ""],
  ["阶段五", "正式体验馆", "AR 眼镜版\n多人组队打 Boss", ""],
];
const tw2 = 2.33, tg = 0.17, ty = 1.6;
s.addShape(p.shapes.LINE, { x: M + 0.22, y: ty + 0.2, w: W - 2 * M - 0.22, h: 0, line: { color: LINE, width: 3 } });
stages.forEach((it, i) => {
  const x = M + i * (tw2 + tg);
  const done = i === 0;
  s.addShape(p.shapes.OVAL, { x: x + 0.05, y: ty + 0.03, w: 0.34, h: 0.34, fill: { color: done ? ACCENT : PRIMARY }, line: { color: "FFFFFF", width: 2 } });
  card(s, x, ty + 0.55, tw2, 2.3, done ? "E7EFDD" : TINT2);
  s.addText(it[0], { x: x + 0.2, y: ty + 0.65, w: tw2 - 0.3, h: 0.32, fontSize: 12, bold: true, fontFace: F, color: done ? ACCENT : PRIM_DEEP, margin: 0 });
  s.addText(it[1], { x: x + 0.2, y: ty + 0.98, w: tw2 - 0.3, h: 0.42, fontSize: 16, bold: true, fontFace: F, color: TEXT, margin: 0 });
  s.addText(it[2], { x: x + 0.15, y: ty + 1.45, w: tw2 - 0.25, h: 0.9, fontSize: 12.5, fontFace: F, color: MUTED, valign: "top", lineSpacingMultiple: 1.15, margin: 0 });
  if (it[3]) tag(s, x + 0.2, ty + 2.42, it[3], ACCENT, 1.85);
});
const risks = [
  ["AR 眼镜贵、戴久了累", "先用平板验证玩法；单次体验控制在 60 分钟内"],
  ["多人同场的识别与同步延迟", "回合制对延迟不敏感；同步只传回合指令，不传画面"],
  ["内容制作量大（形态 × 技能 × 动画）", "沿用本项目“共享骨架 + 换贴图 + 二维表情”管线，降低每只精灵的成本"],
  ["与已有 IP 玩法相似", "突出实体卡 + 桌面 AR + 原创奶蛙形象，不使用他人角色和名称"],
];
s.addText("主要风险与应对", { x: M, y: 4.75, w: 4, h: 0.35, fontSize: 15, bold: true, fontFace: F, color: TEXT, margin: 0 });
risks.forEach((it, i) => {
  const col = i % 2, row = Math.floor(i / 2);
  const x = M + col * 6.25, y = 5.15 + row * 0.82;
  s.addText(it[0], { x, y, w: 6.0, h: 0.32, fontSize: 13, bold: true, fontFace: F, color: C.rage, margin: 0 });
  s.addText(it[1], { x, y: y + 0.32, w: 6.0, h: 0.4, fontSize: 12, fontFace: F, color: TEXT, margin: 0 });
});
footer(s, n);

/* ============ 测试结果与不足 ============ */
s = p.addSlide(); n++;
s.background = { color: BG };
sectionBar(s, "06 测试结果与不足");
title(s, "测试结果与不足", "真机：iQOO Z9 Turbo（vivo V2352A）/ Android 15；自动化：Unity EditMode 273 / 273 通过（2026-10-30）");
const cols = [
  ["已在真机验证", ACCENT, "E7EFDD", [
    "四张卡分别识别出对应颜色的奶蛙",
    "四卡同屏 60.4 FPS，单卡 59 FPS",
    "单击 / 双击加分，满值专属演出",
    "卡片丢失后隐藏，恢复后数值保留",
    "语音“安迪”命中，开心值 70%→82%",
    "旧版存档自动迁移到新格式",
  ]],
  ["已修复、待真机复测", PRIMARY, "F8F1E2", [
    "首次授权麦克风后语音不可用",
    "点奶蛙上半身没有反应",
    "演出中被拒绝的点按关掉了招牌表情",
    "四卡不动时共同大笑反复触发",
    "横屏布局",
  ]],
  ["尚未完成", C.rage, "F7E6E1", [
    "切后台存档与长时间温升未测",
    "其余三句口令的命中率未统计",
    "三张新卡识别星级待记录",
    "拍照留念、追球、特效未实现",
    "WalkLoop 走路动作穿地检查未通过",
  ]],
];
cols.forEach((it, i) => {
  const x = M + i * 4.17, w = 3.95, y = 1.55, h = 3.95;
  card(s, x, y, w, h, it[2]);
  s.addShape(p.shapes.RECTANGLE, { x, y, w, h: 0.08, fill: { color: it[1] }, line: { type: "none" } });
  s.addText(it[0], { x: x + 0.3, y: y + 0.25, w: w - 0.5, h: 0.45, fontSize: 18, bold: true, fontFace: F, color: it[1], margin: 0 });
  s.addText(it[3].map((t) => ({ text: t, options: { bullet: bu(), breakLine: true } })),
    { x: x + 0.25, y: y + 0.85, w: w - 0.4, h: h - 1.0, fontSize: 14.5, fontFace: F, color: TEXT, valign: "top", paraSpaceAfter: 9, lineSpacingMultiple: 1.15, margin: 0 });
});
s.addText("说明：本轮真机测试未系统记录光照与观察距离；温升、长时间连续互动与平板适配尚无实测数据，这些都需要在下一轮测试中补齐。", { x: M, y: 5.75, w: W - 2 * M, h: 0.5, fontSize: 13, fontFace: F, color: MUTED, margin: 0 });
footer(s, n);

/* ============ 结束 ============ */
s = p.addSlide(); n++;
s.background = { color: BG_DARK };
s.addText("谢谢！敬请各位老师指正", { x: 0.8, y: 2.1, w: 7, h: 0.9, fontSize: 36, bold: true, fontFace: F, color: "FFFFFF", margin: 0 });
s.addText("今天：一张卡，一只会回应你的奶蛙。\n未来：一叠卡，一座可以走进去的精灵馆。", { x: 0.8, y: 3.2, w: 7, h: 1.1, fontSize: 17, fontFace: F, color: PRIMARY, lineSpacingMultiple: 1.35, margin: 0 });
s.addText("交付物：Android APK · Unity 源码 · Blender 源资产与 FBX\n四张角色卡打印稿 · 使用说明与测试记录", { x: 0.8, y: 4.8, w: 7, h: 0.7, fontSize: 12.5, fontFace: F, color: "C9BFA9", lineSpacingMultiple: 1.3, margin: 0 });
["classic", "calm", "rage", "divine"].forEach((k, i) => imgH(s, `card_${k}.jpg`, 8.1 + (i % 2) * 2.35, 0.75 + Math.floor(i / 2) * 3.05, 2.85));

p.writeFile({ fileName: OUT }).then((f) => console.log("written", f));
