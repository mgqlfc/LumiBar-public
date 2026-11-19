#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

output="${1:-build}"
app="$output/LumiBar.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
xcrun --sdk macosx swiftc -O -swift-version 6 -parse-as-library \
  -sdk "$sdk" -target "$(uname -m)-apple-macosx27.0" \
  -module-cache-path "$output/ModuleCache" Sources/*.swift \
  -o "$app/Contents/MacOS/LumiBar"
assets="../LumiBar/Assets"
if [[ -f "$assets/weather-icons-clean.png" ]]; then
  cp "$assets/weather-icons-clean.png" "$app/Contents/Resources/"
fi
if [[ -d "$assets/Weather" ]]; then
  mkdir -p "$app/Contents/Resources/Weather"
  cp "$assets/Weather/"*.png "$assets/Weather/LICENSE.txt" "$app/Contents/Resources/Weather/"
fi
if [[ -f "$assets/LumiBar.ico" ]]; then
  sips -s format icns "$assets/LumiBar.ico" --out "$app/Contents/Resources/LumiBar.icns" >/dev/null
fi
cp Resources/Info.plist "$app/Contents/Info.plist"
codesign --force --sign "${LUMIBAR_SIGN_IDENTITY:--}" "$app"
printf '%s\n' "$app"
