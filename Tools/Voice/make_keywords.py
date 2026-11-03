"""Regenerate the KWS keyword file after changing voice commands.

    pip install sherpa-onnx sentencepiece pypinyin
    python Tools/Voice/make_keywords.py

Reads the phrases from ARPet/Assets/StreamingAssets/Voice/kws/keywords_raw.txt (one
"<phrase> @<phrase>" per line) and writes keywords.txt next to it, converting Chinese to
the model's partial-pinyin tokens. Keep the phrases in sync with PetConfig.VoiceCommands
(VoiceAssetsTests.KeywordModel_IsComplete_AndCoversEveryCommand checks this).
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
KWS = ROOT / 'ARPet/Assets/StreamingAssets/Voice/kws'

raw = KWS / 'keywords_raw.txt'
out = KWS / 'keywords.txt'
cmd = ['sherpa-onnx-cli', 'text2token', '--tokens', str(KWS / 'tokens.txt'), '--tokens-type', 'ppinyin',
       str(raw), str(out)]
result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8')
if result.returncode != 0:
    sys.stderr.write(result.stderr)
    sys.exit(result.returncode)
print(out.read_text(encoding='utf-8'))
