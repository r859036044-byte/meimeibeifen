#!/usr/bin/env bash
# 在雲端開遊戲並用瀏覽器串流（noVNC）。網頁埠：6080，網址 /vnc.html?autoconnect=1&resize=scale
# 需先跑過 setup.sh，並裝 x11vnc novnc websockify（apt-get install -y x11vnc novnc websockify xdotool）
. "$(dirname "$0")/env.sh"
cd "$(dirname "$0")/../.."
pkill -f "Xvfb :99" 2>/dev/null; pkill x11vnc 2>/dev/null; pkill websockify 2>/dev/null; pkill -f Godot_v4 2>/dev/null
dotnet build meimei.csproj -nologo -v q 2>&1 | grep -E " error " || true
"$GODOT" --headless --import --path . >/dev/null 2>&1
Xvfb :99 -screen 0 1920x1080x24 >/dev/null 2>&1 &
sleep 1
DISPLAY=:99 nohup "$GODOT" --path . --rendering-driver opengl3 >/tmp/meimei_game.log 2>&1 &
DISPLAY=:99 nohup x11vnc -display :99 -forever -shared -nopw -rfbport 5900 >/tmp/x11vnc.log 2>&1 &
nohup websockify --web /usr/share/novnc 6080 localhost:5900 >/tmp/websockify.log 2>&1 &
sleep 3; echo "已啟動：埠 6080"
