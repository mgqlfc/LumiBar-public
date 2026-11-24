#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

output="${1:-build}"
app="$output/LumiBar.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
IFS=. read -r sdk_major sdk_minor _ <<< "$(xcrun --sdk macosx --show-sdk-version)"
arch="$(uname -m)"
identity="${LUMIBAR_SIGN_IDENTITY:--}"
compiler=(xcrun --sdk macosx swiftc -O -swift-version 6 -parse-as-library
  -sdk "$sdk" -module-cache-path "$output/ModuleCache")
swift_options=()
if [[ "$sdk_major" -gt 27 || ( "$sdk_major" -eq 27 && "${sdk_minor:-0}" -ge 3 ) ]]; then
  swift_options+=(-D LUMIBAR_LEGACY_ACTIVITY_CLEANUP)
fi

cp Resources/Info.plist "$app/Contents/Info.plist"
rm -rf "$app/Contents/PlugIns/LumiBarLiveActivity.appex" "$app/Contents/Resources/Metadata.appintents"
qq_des="Vendor/QQMusicDES"
xcrun --sdk macosx clang -arch "$arch" -mmacosx-version-min=27.0 -O2 -fvisibility=hidden -c "$qq_des/des.c" -o "$output/qq-des.o"
xcrun --sdk macosx clang -arch "$arch" -mmacosx-version-min=27.0 -O2 -fvisibility=hidden -c "$qq_des/QQMusicDES.c" -o "$output/qq-qrc.o"
"${compiler[@]}" -target "$arch-apple-macosx27.0" -module-name LumiBar -whole-module-optimization \
  ${swift_options[@]+"${swift_options[@]}"} -I "$qq_des" Sources/*.swift "$output/qq-des.o" "$output/qq-qrc.o" \
  -o "$app/Contents/MacOS/LumiBar"
cp "$qq_des/LICENSE.txt" "$app/Contents/Resources/QQMusicDES-LICENSE.txt"
assets="../LumiBar/Assets"
if [[ -f "$assets/weather-icons-clean.png" ]]; then
  cp "$assets/weather-icons-clean.png" "$app/Contents/Resources/"
fi
if [[ -d "$assets/Weather" ]]; then
  mkdir -p "$app/Contents/Resources/Weather"
  cp "$assets/Weather/"*.png "$assets/Weather/LICENSE.txt" "$app/Contents/Resources/Weather/"
fi
cp Resources/LumiBar.icns "$app/Contents/Resources/LumiBar.icns"
if [[ -f "$assets/Fonts/Geist-VariableFont_wght.ttf" ]]; then
  cp "$assets/Fonts/Geist-VariableFont_wght.ttf" "$assets/Fonts/Geist-OFL.txt" "$app/Contents/Resources/"
fi
reference="../../outputs/Mac前端还原包"
if [[ -f "$reference/fixtures.json" && -f "$reference/fixtures/reference-cover.png" ]]; then
  mkdir -p "$app/Contents/Resources/Reference"
  cp "$reference/fixtures.json" "$reference/fixtures/reference-cover.png" "$app/Contents/Resources/Reference/"
fi
for media_root in "${LUMIBAR_MEDIA_CONTROL_ROOT:-}" /opt/homebrew/opt/media-control /usr/local/opt/media-control \
    /Applications/Lyrimuse.app/Contents/Resources/media-control; do
  [[ -n "$media_root" && -x "$media_root/bin/media-control" && -d "$media_root/Frameworks" ]] || continue
  media_target="$app/Contents/Resources/media-control"
  for directory in bin lib Frameworks; do
    ditto "$media_root/$directory" "$media_target/$directory"
  done
  cp Resources/media-control-LICENSE.txt "$media_target/LICENSE"
  break
done
codesign --force --sign "$identity" "$app"
printf '%s\n' "$app"
