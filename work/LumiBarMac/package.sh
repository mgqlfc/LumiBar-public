#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

output="${1:-build}"
app="$output/LumiBar.app"
image="${2:-$output/LumiBar-macOS-$(uname -m).dmg}"
if [[ ! -d "$app" ]]; then
  bash build.sh "$output"
fi
codesign --verify --strict "$app"
stage="$(mktemp -d "$output/package.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
ditto "$app" "$stage/LumiBar.app"
ln -s /Applications "$stage/Applications"
hdiutil create -fs HFS+ -volname LumiBar -srcfolder "$stage" -format UDZO -ov "$image"
