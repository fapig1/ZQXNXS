# 奶蛙表情参考——嘴部与表情既有画法审查

- **日期**：2026-09-21
- **性质**：一次性图片审查子代理，结论落盘；主对话未看图。
- **用途**：为愤怒卡（02_rage_朱红_权杖骑士）提示词 v6.6 的"大张怒吼嘴"提供**角色自身表情包/梗图里的既有画法依据**，并与 `findings_rage_02_子代理审查.md` 第七节方案对齐。

**审查材料**（全部在项目内）：
`Docs/AssetReview/20260919_卡面基准形象/img/meme_rage.jpg`、`meme_calm.jpg`、`meme_divine.jpg`；`ArtSource/References/20260908_naiwa_assets/video_frames/naiwalaugh_t1～t6.png`；`naiwa_meme/naiwa-smiling.png`、`naiwa-thinking.png`；`Docs/AssetReview/20260908_naiwa/expression_frames.jpg`（18 帧网格）、`primary_references.jpg`、`secondary_references.jpg`（含 police / sad / kid / angel / meditation / apple / egg / glimpse / rain / turtle / last_supper_parody 等变体）。

---

## 一、奶蛙嘴部画法体系（综合全部表情包）

1. **嘴永远是"一个小巧的简单图形"，从不是写实口腔**。闭合态全部画法为一条短细深色线，无嘴唇、无鼻孔、无嘴角皱纹。
2. **位置固定**：位于两只大 dome 眼下方、面部中央（两眼间距中点偏下），不偏斜。
3. **占比小**：闭合嘴宽约占面宽 1/4～1/3，与超大圆眼形成"反差萌"——这是该角色"奶萌"比例的核心，任何表情都不应破坏这个量级关系（张嘴态除外，见第二节）。
4. **闭合态只有三种形态**：
   - **微笑弧**：短弧、两端微扬（naiwa-smiling、kid）——"恬静微笑"，即 v6.2 定过的口型；
   - **平直线**：短直条（thinking、angel、sad、meme_calm、meme_divine、rage meme 本体）——中性/严肃/庄严；
   - **微小线**：比平直更短（sad）——低落。
5. **风格语言一致**：粗黑描边、平涂硬边、木刻感，嘴与皮肤同体系，无渐变、无高光、无湿反光。
6. **表情语义主要不靠嘴**：该角色的情绪更多由**眼睛**（圆睁 / 眯弧 / 低垂）与**眉毛**（怒眉）承担，嘴是辅助——这一点直接决定愤怒卡的设计策略（见第三、四节）。

## 二、张嘴态逐张记录

### naiwalaugh 六帧（laughing 视频逐帧）

- **t1**：闭合基准。短弧线（略上扬），双眼闭合成满足弧——张嘴前的起手帧。
- **t2**：大张嘴。横向椭圆开口，嘴角向两侧**向上**拉起；**上排一排白色小三角牙**清晰可见；口腔**暗红褐/深栗纯色平涂**；双眼挤成开心眯弧；头微后仰。
- **t3**：同 t2 构型：横向椭圆口、上排白牙、暗色口腔；单眼闭合，得意大笑感。
- **t4**：同前：双眼闭合弧，嘴上扬张到最大，上排白牙 + 暗口腔。
- **t5**：头更后仰，大张横椭圆嘴，上排白牙，暗口腔。
- **t6**（躺姿后仰）：嘴张成近圆/竖椭圆，**未见上排牙**，口底可见一坨**浅粉红圆舌**；暗色口腔；双眼为圆睁（非眯弧）。

### 其他张嘴图

- **expression_frames.jpg 网格**：mfstream_t2/t3/t6 与 naiwalaugh 各帧构型一致——站立张嘴统一为"横向椭圆 + 上排小白牙 + 暗口腔 + 开心眯眼"；躺姿两帧（mfstream_t7/t8）与 naiwalaugh_t6 一致，圆口 + 粉舌。
- **primary_references.jpg**：`mf-pose-belly.png` 张嘴成圆口、**粉舌向下探出**、暗口腔、双眼闭合（大笑）；`mf-pose-rolling.png` 躺姿张嘴，上排白牙 + 粉舌同屏。
- **police.jpg**（右侧角色）：咧嘴露出上排白牙 + 暗色口缝，属"得意/强势"而非愤怒。

### 共同点总结

1. **嘴形是简单几何开口**：横向椭圆或圆角形，不是写实血口。
2. **口腔填充纯深色平涂**：暗红褐/深栗色，无渐变、无湿光、无唾液。
3. **牙齿只画上排**：一排小而均匀的白色圆角三角（约 5 颗上下量级），**从不画下排牙**；部分帧无牙。
4. **舌头是浅粉红/暖红简单圆椭圆**，无舌纹，仅在躺姿/特定帧出现。
5. **位置低且居中**：张嘴时下沿贴近下颌弧线。
6. **关键语义绑定**：站立张嘴帧的眼睛一律挤成**开心眯弧**——在该角色体系里，"张嘴"默认绑定"大笑/开心"语义。这是愤怒卡必须显式打破的唯一定式（见第四节）。

## 三、愤怒/强势表情现成依据

- **meme_rage.jpg（愤怒卡基准梗图本体）**：奶蛙**并没有张嘴怒吼**。怒意由三处承担：① **内端下压的倒八字怒眉**（愤怒的唯一现成信号）；② **圆睁直视的眼**（白眼球 + 黑瞳，正视镜头）；③ 嘴保持**短平直线**（不上扬不下垂）。肢体为单手抬起做"制止/喝止"手势。
- **meme_calm / meme_divine**：同系列"模式切换"图，均用短直嘴 + 圆眼，divine 更把眼画成发光白球——进一步印证"嘴从简、眼眉主情绪"。
- **police**：可借"咧嘴 + 上排白牙"的强势感，但语义是得意。
- **sad**：严肃不悦靠低垂眼 + 微小直嘴，无可借张嘴元素。
- **kid / angel**：小弧微笑 / 平直庄严嘴，与愤怒无关。
- **结论**：变体里**没有现成的"张嘴怒吼"愤怒图**——张嘴素材全部来自大笑帧。愤怒卡需要做的是**"借大笑嘴的形，改愤怒的意"**：嘴形沿用张嘴体系（横椭圆、暗口腔、上排小牙、可选圆舌），语义改用**不上扬的嘴角 + 怒眉 + 圆睁直视眼**（怒眉与直视眼正是 meme_rage 的实证画法）。

## 四、愤怒怒吼嘴设计提炼

**嘴形**：横向大张的椭圆/圆角矩形开口，**宽约占面宽 1/3～1/2**（大笑帧实证接近 1/2，与 findings_rage_02 提议一致），位于双眼下方居中，下沿贴近下颌弧线。

**嘴角**：向两侧大幅拉开，**保持水平或略下扣，绝不上扬**——上扬嘴角在大笑帧里是开心语义（t2～t5 实证），水平/下扣是"怒吼"与"大笑"的唯一分界线。

**口腔填充**：**纯深色平涂，暗红褐/深栗色**（角色实证色），外沿粗黑硬边描边；无渐变、无湿反光、无唾液。

**牙齿**：上排**一排小而均匀的白色圆角三角牙**（canonical 画法）；如需更凶，可将中间两颗略大（findings_rage_02 的"1～2 颗小圆獠牙"是此变体的风格化子集，但"一排"才是角色的既有画法）。

**舌头**：可选，口底放一颗**浅粉红/暖红小圆椭圆**（躺姿帧实证），无纹理。

**眼睛**：保持 dome 大眼 + 极大近黑圆瞳不变（用户未要求改眼）；**可选增强**：加一对**短粗倒八字怒眉**（meme_rage 实证的愤怒信号），眼睛保持圆睁直视、**不得画成大笑的开心眯弧**。

**必须保持的奶蛙特征**（奶萌底线）：圆身圆嘴的玩具比例；无唇无鼻孔；简单几何形；平涂硬边粗黑描边；黏土哑光质感；嘴再大也是一个"可爱的圆开口"，不能退化成写实血口獠牙排。

**愤怒态新增元素**：口腔大开口本身、暗色口腔填充、上排小牙排、（可选）圆舌、（可选）怒眉、（可选）嘴角略下扣。

## 五、可写入提示词的英文句（gpt-image-2.5，5 条）

1. "The frog knight's mouth is a wide OPEN roar: a large horizontally stretched oval opening about one third to one half of the face width, sitting low and centered below the huge dome eyes, filled with solid dark maroon (dark reddish-brown) flat color with a thick black hard-edge outline — never a closed line, never a smile, never a gentle upturned arc."
2. "Along the top inside edge of the open mouth, draw a row of small, uniform, rounded white triangle teeth (upper teeth only, no lower teeth, no realistic fang rows); optionally place one small rounded warm-pink tongue ellipse at the bottom of the mouth, with no tongue texture."
3. "The mouth corners are pulled wide apart and held level or slightly downturned — never curled upward, never happy, never a calm or content expression."
4. "Draw the roaring mouth in the character's woodcut clay-toy language: flat solid fills, hard edges, thick dark outline, no lips, no nostrils, no gradients, no gloss, no saliva — keep the shape rounded and cute, not a realistic scary maw."
5. "The huge round dome eyes stay wide open and staring straight forward with the big near-black pupils unchanged — NOT squeezed into happy squint arcs; optionally add a pair of short thick V-shaped angry brows angled down toward the center above the eyes."

## 六、结论与建议

**与 findings_rage_02 第七节"大张怒吼嘴"方案总体一致**（大张嘴、宽 1/3～1/2、嘴角拉开不上扬、深色平涂 + 粗黑描边、圆舌/小獠牙、怒眉可选、否定句）。本文件用角色表情包实证为其补证，并给出四点细化建议：

1. **填充色**：rage_02 写"近黑或暗红黑"；实证为**暗红褐/深栗**，建议定稿描述采用 "solid dark maroon (dark reddish-brown)"，比"近黑"更贴角色既有画法。
2. **牙齿**：rage_02 的"1～2 颗小圆獠牙"是风格化变体；角色 canonical 画法是**上排一排小而均匀的白色圆角三角牙**。建议英文句写"a row of small rounded white triangle teeth"，如需更凶再加"the two middle ones slightly larger"。
3. **眼睛**：角色张嘴时实证绑定"开心眯弧"（naiwalaugh_t2～t5），怒吼时必须显式否定（圆睁直视），否则模型容易顺手画成大笑眼；怒眉是 meme_rage 里唯一的现成愤怒信号，建议保留为可选增强句。
4. **嘴角分界**：上扬 = 大笑、水平/略下扣 = 怒吼，是两者唯一形态分界，建议"NOT upturned"与"no happy squint eyes"两句同时写入，缺一会被模型回退成开心表情。
