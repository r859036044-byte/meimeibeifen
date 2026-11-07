#!/usr/bin/env bash
# 用法：Tools/cloud/test.sh [輸出資料夾] [幀數]
# 編譯 → 匯入資源 → 在虛擬螢幕下跑遊戲並錄成 PNG 序列（可用 Read 看圖）。
set -euo pipefail
. "$(dirname "$0")/env.sh"
OUT=${1:-/tmp/meimei_shots}; FRAMES=${2:-60}
cd "$(dirname "$0")/../.."
dotnet build meimei.csproj -nologo -v q 2>&1 | grep -E "error|Error" || true
"$GODOT" --headless --import --path . >/dev/null 2>&1
mkdir -p "$OUT"
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" --path . --rendering-driver opengl3 \
  --write-movie "$OUT/f.png" --fixed-fps 10 --quit-after "$FRAMES" 2>&1 | grep -iE "error|exception" || true
echo "完成：$OUT"
