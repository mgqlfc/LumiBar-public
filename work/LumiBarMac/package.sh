#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

app="build/LumiBar.app"
image="build/LumiBar-macOS-$(uname -m).dmg"
if [[ ! -d "$app" ]]; then
  bash build.sh
fi
codesign --verify --strict "$app"
stage="$(mktemp -d "$PWD/build/package.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
ditto "$app" "$stage/LumiBar.app"
ln -s /Applications "$stage/Applications"
hdiutil create -fs HFS+ -volname LumiBar -srcfolder "$stage" -format UDZO -ov "$image"
