from pathlib import Path
from PIL import Image

ROOT = Path(r"D:\ZengQiangXianShi")
OUT = ROOT / "Tools/_render/final_img"
OUT.mkdir(parents=True, exist_ok=True)


def crop_pct(im, l, t, r, b):
    w, h = im.size
    return im.crop((int(w * l / 100), int(h * t / 100), int(w * r / 100), int(h * b / 100)))


def save(im, name, max_side=1400, quality=88):
    im = im.convert("RGB")
    im.thumbnail((max_side, max_side), Image.LANCZOS)
    im.save(OUT / name, "JPEG", quality=quality, optimize=True)
    print(name, im.size)


tmp = ROOT / ".workbuddy-ai" / "tmp"
dev = ROOT / "ArtSource/Characters/Pet01/Logs/ShoulderCheck_20261026/device"

# 四卡同屏：横持拍摄，裁掉左侧显示器/小字，顺时针转正
im = Image.open(tmp / "screen-09-landscape.png")
save(crop_pct(im, 8, 25, 100, 90).rotate(-90, expand=True), "real_four_cards.jpg")
# 四卡同屏时的状态面板（四只均"在场"、60 FPS）
save(crop_pct(im, 0, 0, 100, 25).rotate(-90, expand=True), "real_four_panel.jpg")

# 正式界面：跟踪提示、开心值、四宠列表、按住说话
save(Image.open(tmp / "screen-02.png"), "real_ui_voice.jpg", max_side=1600)

# 单卡召唤：脸部清晰的暖黄奶蛙
im = Image.open(dev / "Screenshot_20261026_211415_com.DefaultCompany.ARPet.jpg")
save(crop_pct(im, 0, 18, 79, 96), "real_single_card.jpg")

# 四张定稿卡面与塔罗原图
cards = ROOT / "Docs/AssetReview/20260920_卡面2D/picked"
for pid in ("classic", "calm", "rage", "divine"):
    save(Image.open(cards / f"{pid}.png"), f"card_{pid}.jpg", max_side=900)

tarot_dir = ROOT / "Docs/AssetReview/20260920_塔罗卡面参考"
tarot = {
    "calm": "RWS1909_-_12_Hanged_Man.jpeg",
    "rage": "RWS1909_-_Wands_12.jpeg",
    "divine": "RWS1909_-_05_Hierophant.jpeg",
    "classic": "RWS1909_-_Pentacles_11.jpeg",
}
for pid, fname in tarot.items():
    src = next(tarot_dir.rglob(fname))
    save(Image.open(src), f"tarot_{pid}.jpg", max_side=900)

# 动作预览渲染（Blender）
prev = ROOT / "ArtSource/Characters/Pet01/ArmSeparation/Previews"
gassho = ROOT / "ArtSource/Characters/Pet01/CalmGassho/Previews"
actions = {
    "act_classic.jpg": prev / "classic_proud_hero_28_wave_b.png",
    "act_calm.jpg": gassho / "calm_zen_hero_30_nod1.png",
    "act_rage.jpg": prev / "rage_roar_hero_32_peak.png",
    "act_divine.jpg": prev / "divine_awaken_hero_41_spread.png",
    "act_laugh.jpg": prev / "social_laugh_hero_19_wave1.png",
}
for name, src in actions.items():
    save(Image.open(src), name, max_side=600)

# 奶蛙多形态参考（第三方素材，仅作灵感展示）
var = ROOT / "ArtSource/References/20260908_naiwa_assets/xinye_variants"
for n in ("egg", "angel", "meditation", "police"):
    save(Image.open(var / f"{n}.jpg"), f"form_{n}.jpg", max_side=720)

