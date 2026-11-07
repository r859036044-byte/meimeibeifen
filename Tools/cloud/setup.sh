#!/usr/bin/env bash
# 雲端環境一次性安裝：Godot 4.7.2 (.NET) + .NET 8 SDK，裝到 /opt/tools。
set -euo pipefail
T=/opt/tools; V=4.7.2-stable
mkdir -p "$T"; cd "$T"
if [ ! -d "Godot_v${V}_mono_linux_x86_64" ]; then
  curl -sSL -o godot.zip "https://github.com/godotengine/godot-builds/releases/download/${V}/Godot_v${V}_mono_linux_x86_64.zip"
  unzip -qo godot.zip
fi
if [ ! -d dotnet ]; then
  curl -sSL -o dotnet-install.sh https://dot.net/v1/dotnet-install.sh
  bash dotnet-install.sh --channel 8.0 --install-dir "$T/dotnet"
fi
echo "OK: 接著執行 Tools/cloud/test.sh"
