"""Validate handoff consistency without dependencies or any live app access."""
from pathlib import Path
import hashlib
import json
import struct
import sys

pack = Path(__file__).resolve().parents[1]
repo = pack.parents[1]
manifest = json.loads((pack/'reference-manifest.json').read_text(encoding='utf-8-sig'))
tokens = json.loads((pack/'visual-tokens.json').read_text(encoding='utf-8-sig'))
fixture = json.loads((pack/'fixtures.json').read_text(encoding='utf-8-sig'))
errors = []
for entry in manifest['sources']:
    path = repo/entry['path']
    if not path.is_file() or hashlib.sha256(path.read_bytes().replace(b'\r\n',b'\n')).hexdigest() != entry['sha256LF']:
        errors.append('Source baseline changed: '+entry['path'])
for entry in manifest['assets']:
    path = repo/entry['path']
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
        errors.append('Asset missing or changed: '+entry['path'])
for entry in manifest['captures']:
    path = pack/entry['file']
    if not path.is_file():
        errors.append('Missing capture: '+entry['file']);continue
    if list(struct.unpack('>II',path.read_bytes()[16:24])) != entry['pngPixels']:
        errors.append('PNG dimension mismatch: '+entry['file'])
    if not .97 <= entry['approxScreenshotPixelsPerDip'] <= 1.03:
        errors.append('Capture is not normalized to source DIP: '+entry['file'])
    metadata = json.loads((pack/entry['metadata']).read_text(encoding='utf-8-sig'))
    if metadata['data'] != 'synthetic' or metadata['playerTransportConnected'] or metadata['animations']:
        errors.append('Capture profile mismatch: '+entry['file'])
required = {f'{scene}-{theme}.png' for scene in ['weather','weather-hover','weather-capsule','weather-lite','music','settings'] for theme in ['light','dark']}
required |= {'weather-wide-light.png','weather-alerts-light.png'}
if required != {Path(x['file']).name for x in manifest['captures']}:
    errors.append('Reference scene coverage differs from the documented fourteen images.')
if len(tokens['settings']['accentOptions']) != 9 or len(fixture['media']['spectrum']) != 20:
    errors.append('Accent or spectrum contract mismatch.')
if fixture['media']['lyrics'][fixture['media']['activeLyricIndex']]['seconds'] > fixture['media']['positionSeconds']:
    errors.append('Active lyric fixture is inconsistent.')
if errors:
    print('\n'.join(errors));sys.exit(1)
print(f'PASS: {len(manifest["captures"])} captures, {len(manifest["sources"])} source hashes, {len(manifest["assets"])} assets; tokens and fixtures agree.')
