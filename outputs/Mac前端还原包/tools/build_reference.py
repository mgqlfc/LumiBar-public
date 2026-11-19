"""Rebuild data-only handoff files from the current Windows sources and captures."""
from pathlib import Path
import hashlib
import json
import math
import re
import struct

PACK = Path(__file__).resolve().parents[1]
ROOT = PACK.parents[1]
SRC = ROOT / 'work/LumiBar'


def dump(name, value):
    (PACK / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def text_hash(path):
    return hashlib.sha256(path.read_bytes().replace(b'\r\n', b'\n')).hexdigest()


def source(name):
    return (SRC / name).read_text(encoding='utf-8-sig')


def require(name, fragment):
    if fragment not in source(name):
        raise RuntimeError(f'Source changed; review tokens before updating: {name}: {fragment}')


require('CapsuleGeometry.cs', 'Height=34,Radius=Height/2,Gutter=10')
require('SurfaceWindow.cs', 'Ui.ClipRounded(Root,12)')
require('HoverPreviewWindow.cs', 'protected override int ExitDurationMs=>120')
require('WeatherWindow.cs', 'new[]{112d,32d,58d,1d,36d}')
require('MusicWindow.cs', 'Ui.Text("",19)')
require('FadingLyricsHost.cs', '_duration=animate?680:0')
require('MarqueeTitle.cs', 'overflow/22')
require('AnimatedReveal.cs', '_duration=expanded?280:220')

accent_rows = re.findall(r'new AccentPalette\("([^"]+)","([^"]+)",0x([A-F0-9]+),0x([A-F0-9]+),0x([A-F0-9]+),0x([A-F0-9]+),0x([A-F0-9]+)\)', source('Backend/AccentPalettes.cs'))
assert len(accent_rows) == 9
accents = {r[0]: {'name': r[1], 'colors': ['#' + v for v in r[2:5]], 'lightInk': '#' + r[5], 'darkInk': '#' + r[6]} for r in accent_rows}
rgb_pairs = re.findall(r'Rgb\((\d+),(\d+),(\d+)\),Rgb\((\d+),(\d+),(\d+)\)', source('WeatherAtmosphere.cs'))
assert len(rgb_pairs) == 10
weather_names = ['haze', 'dustStorm', 'thunderstorm', 'snow', 'fog', 'rain', 'overcast', 'partlyCloudyDay', 'clearNight', 'clearDay']
weather_pairs = {name: ['#' + ''.join(f'{int(v):02X}' for v in row[:3]), '#' + ''.join(f'{int(v):02X}' for v in row[3:])] for name, row in zip(weather_names, rgb_pairs)}

tokens = {
    'schemaVersion': 1,
    'baseline': {'windowsVersion': '0.7.56', 'date': '2026-11-19', 'frontend': 'builtin WinUI 3', 'units': 'DIP; start with equal macOS points', 'authority': 'current source + real reference PNGs'},
    'fonts': {'western': 'Geist', 'chinese': 'PingFang SC', 'separateSettings': True, 'avoidRoundedNumerals': True},
    'defaults': json.loads((SRC / 'Assets/default-settings.json').read_text(encoding='utf-8-sig')),
    'surface': {'cornerRadius': 12, 'shadowGutter': 16, 'lightFallback': '#F7F9FD', 'darkFallback': '#181B23', 'referenceBackdropLight': '#F3F5FA', 'referenceBackdropDark': '#171922'},
    'capsule': {'height': 34, 'radius': 17, 'outerGutter': 10, 'outerHeight': 54, 'weatherMinWidth': 104, 'weatherLiteMinWidth': 64, 'musicFullWidth': 238, 'musicLiteWidth': 48, 'normalWeatherIcon': 24, 'liteWeatherIcon': 22, 'temperatureFont': 15, 'temperatureWeight': 600, 'rainIcon': 14, 'rainFont': 11, 'rainOpacity': .85, 'normalGap': 4, 'liteGap': 3, 'rainIconToNumberGap': 2, 'widthPolicy': 'measure actual glyphs; include minus sign, degree, percent and font changes'},
    'weatherHover': {'width': 264, 'height': 160, 'padding': [14.5, 16, 17.5, 16], 'headerHeight': 40, 'headerIcon': 30, 'headerCityFont': 15, 'headerConditionFont': 11, 'headerTemperatureFont': 24, 'metricColumns': 3, 'metricColumnGap': 10, 'metricRowHeights': [18, 31, 17], 'metricRowGap': 3, 'metricTitleFont': 11, 'metricValueFont': 22, 'metricNoteFont': 10, 'metricValueWeight': 600},
    'weatherDetail': {'width': 480, 'height': 'content-driven; bounded by usable screen', 'padding': [22, 16, 22, 16], 'sectionGap': 8, 'cityFont': 24, 'metadataFont': 11, 'currentIcon': 68, 'currentTemperatureFont': 52, 'currentColumnsGap': 12, 'temperatureSummaryOrder': ['min', 'max', 'feelsLike'], 'hourlySampleColumns': 8, 'dailyPreviewRows': 4, 'dailyColumnWidths': [112, 32, 58, 'star', 36], 'dailyColumnGap': 10, 'dailyRowMinHeight': 28, 'dailyBarHeight': 5, 'summaryColumns': 5, 'summaryColumnGap': 12, 'summaryTitleFont': 10, 'summaryValueFont': 16, 'summaryDirectionFont': 10, 'summaryOrder': ['relativeHumidity', 'wind', 'airQuality', 'pressure', 'uvIndex']},
    'musicDetail': {'width': 420, 'height': 650, 'padding': [22, 14, 22, 16], 'cover': 58, 'coverRadius': 11, 'titleFont': 19, 'artistFont': 13, 'albumFont': 11, 'playButton': 42, 'layersBackToFront': ['glass', 'albumLight', 'lyrics', 'spectrum', 'sourceAndControls']},
    'lyrics': {'defaultFont': 22, 'allRowsSameFontSize': True, 'weight': 600, 'spacingFormula': 'clamp(size*.5,9,18)', 'stepFormula': 'size*1.3+spacing', 'rowsFormula': 'size<=21 ? 6 : size<=29 ? 5 : 4', 'baselineViewportFormula': 'clamp(step*rows+120,312,392)', 'topExtensionSteps': .5, 'bottomExtensionSteps': 1.5, 'focusTopFormula': 'clamp(step+48,80,128)+topExtension', 'focusFadeSafetyDip': 8, 'focusOpacity': 1, 'nearbyOpacity': .62, 'farOpacity': .30, 'normalOpacityWhenUntimed': .78, 'focusFont22': {'spacing': 11, 'step': 39.6, 'topExtension': 19.8, 'bottomExtension': 59.4, 'viewport': 397.2, 'focusTop': 107.4}, 'offsetStepSeconds': .5, 'offsetScope': 'track'},
    'marquee': {'edgeStops': [0, .05, .95, 1], 'edgeAlpha': [0, 1, 1, 0], 'pauseSeconds': 1.8, 'speedDipPerSecond': 22, 'minTravelSeconds': 1, 'reservedWidthTransitionMs': 260, 'disabledMotionFallback': 'ellipsis'},
    'settings': {'width': 560, 'height': 650, 'headerHeight': 54, 'accentOptions': accents},
    'temperatureGradient': ['#4799FF', '#A497DA', '#FFA54F'],
    'weatherLightPairs': weather_pairs,
    'weatherAtlas': {'path': 'work/LumiBar/Assets/weather-icons-clean.png', 'columns': 4, 'rows': 4, 'indices': {'clearDay': 0, 'clearNight': 1, 'partlyCloudyDay': 2, 'partlyCloudyNight': 3, 'unknown': 4, 'overcast': 5, 'rain': 6, 'heavyRain': 7, 'snow': 9, 'freezingRain': 10, 'thunderstorm': 11, 'fogAndHaze': 12, 'dustStorm': 13}, 'iconsHaveBakedGlow': False},
    'motionMs': {'hoverOpenDelay': 320, 'hoverLeaveGrace': 350, 'hoverExit': 120, 'detailExit': 180, 'revealExpand': 280, 'revealCollapse': 220, 'revealContentEnter': 200, 'revealContentExit': 140, 'lyricFollow': 680, 'lyricOpacity': 520, 'capsuleMove': 220, 'windowResize': 320},
    'albumLightPeriodsSeconds': [17, 21],
    'glassWindowsReference': {'kind': 'Thin', 'materialMinimum': .35, 'lightTint': '#FFFFFF', 'lightTintOpacity': .492, 'lightLuminosityOpacity': .2375, 'darkTint': '#1E2B44', 'darkTintOpacity': .24, 'darkLuminosityOpacity': .4575, 'washFormula': 'slope*(density-.35)/(1-baseline)', 'lightWashBaseline': .492, 'lightWashSlope': .32, 'darkWashBaseline': .4575, 'darkWashSlope': .45, 'porting': 'match visual result on the reference backdrop, not Windows API constants'},
}
dump('visual-tokens.json', tokens)

lyric_text = ['在街灯之间，慢慢走远', 'The city leaves a light for us', '晚风穿过透明的窗', 'We keep a little room for dawn', '让这一刻，停在微光里', 'A quieter song beneath the sky', '沿着熟悉的街道回去', 'And every window starts to glow', '留一点光，等下个晴天', 'The morning finds its way to you', '把喜欢的歌留在此刻', 'We carry all the colors home']
fixture = {'schemaVersion': 1, 'dataKind': 'synthetic UI reference', 'clock': {'date': '2026-11-19', 'hourlyStart': '12:00', 'updatedLabel': '上次更新：12:00'}, 'city': {'name': '武清区', 'region': '天津市', 'favorites': ['奎文区', '北京市'], 'located': True}, 'weather': {'temperature': 8, 'feelsLike': 5, 'code': 3, 'isDay': True, 'condition': '阴', 'rainProbabilityPercent': 20, 'relativeHumidityPercent': 66, 'windKmh': 14.1, 'windDegrees': 45, 'windDirection': '东北风', 'aqi': 83, 'aqiStandard': 'CN AQI', 'aqiCategory': '良', 'pressureHpa': 1026, 'uvi': 1.6, 'sourceLabel': '和风天气', 'hourly': [{'hoursFromStart': i, 'temperature': 8+3*math.sin(i/5), 'code': 61 if i%4 == 0 else 3, 'rainProbabilityPercent': 20} for i in range(24)], 'daily': [{'daysFromStart': i, 'min': 3-i*.2, 'max': 12-i*.3, 'code': 61 if i%3 == 0 else 3, 'rainProbabilityPercent': 20} for i in range(10)], 'precipitation15MinAmountMm': [0, .1, .3, .1], 'precipitationNotice': 'model trend; do not relabel as minute radar prediction'}, 'wideWeather': {'temperature': -20, 'feelsLike': -24, 'rainProbabilityPercent': 100}, 'alerts': [{'event': '大风', 'level': 'yellow'}, {'event': '海上大风', 'level': 'blue'}], 'media': {'title': 'City Lights · Reference Session', 'artist': 'LumiBar Ensemble', 'album': 'Through the Glass', 'playerLabel': 'Spotify', 'lyricsLabelInCapture': '歌词', 'playing': True, 'positionSeconds': 36, 'durationSeconds': 222, 'coverPath': 'fixtures/reference-cover.png', 'accent': '#71B6D7', 'secondaryAccent': '#AE9BD2', 'lyrics': [{'seconds': i*8, 'text': t} for i, t in enumerate(lyric_text)], 'activeLyricIndex': 4, 'spectrum': [.18,.32,.52,.68,.47,.36,.42,.56,.44,.31,.26,.34,.28,.22,.17,.24,.19,.12,.08,.04]}, 'capture': {'glassDensity': .5, 'glow': .26, 'saturation': .8, 'lyricsFontSize': 22, 'animations': False, 'playerTransportConnected': False}}
dump('fixtures.json', fixture)

sources = ['CapsuleGeometry.cs', 'CapsuleWindow.cs', 'CapsuleEdge.cs', 'CapsuleTransparency.cs', 'RoundedLightSurface.cs', 'GlassBackdrop.cs', 'SurfaceWindow.cs', 'PopupShadow.cs', 'SoftLightTexture.cs', 'WeatherWindow.cs', 'WeatherGlyph.cs', 'WeatherAtmosphere.cs', 'TemperaturePalette.cs', 'WeatherAlertBadge.cs', 'HoverPreviewWindow.cs', 'MusicWindow.cs', 'FadingLyricsHost.cs', 'MarqueeTitle.cs', 'AlbumAtmosphere.cs', 'AlbumLightColor.cs', 'AnimatedReveal.cs', 'WindowMotion.cs', 'SettingsWindow.cs', 'Backend/FontDefaults.cs', 'Backend/GlassDensity.cs', 'Backend/CoverPalette.cs', 'Backend/AccentPalettes.cs', 'Backend/LyricLayout.cs', 'Backend/LyricFade.cs', 'Assets/default-settings.json']
assets = [SRC / 'Assets/weather-icons-clean.png', SRC / 'Assets/Fonts/Geist-VariableFont_wght.ttf', SRC / 'Assets/Fonts/Geist-Italic-VariableFont_wght.ttf', SRC / 'Assets/Fonts/PingFangSC-Light.otf']
assets += sorted((PACK / 'references').glob('*'))
assets += [PACK / 'fixtures/reference-cover.png']
records = []
for path in assets:
    raw = path.read_bytes()
    entry = {'path': path.relative_to(ROOT).as_posix(), 'sha256': hashlib.sha256(raw).hexdigest(), 'bytes': len(raw)}
    if path.suffix == '.png':
        assert raw[:8] == b'\x89PNG\r\n\x1a\n'
        entry['pngWidth'], entry['pngHeight'] = struct.unpack('>II', raw[16:24])
    records.append(entry)
captures = []
for path in sorted((PACK / 'references').glob('*.png')):
    meta = json.loads(path.with_suffix('.json').read_text(encoding='utf-8-sig'))
    w, h = struct.unpack('>II', path.read_bytes()[16:24])
    capsule = meta['scene'] in ['weather-capsule', 'weather-lite', 'weather-wide']
    captures.append({'file': 'references/'+path.name, 'metadata': 'references/'+path.with_suffix('.json').name, 'scene': meta['scene'], 'theme': meta['theme'], 'pngPixels': [w, h], 'nativePixels': [meta['physicalWidth'], meta['physicalHeight']], 'sourceDpiScale': meta['scale'], 'approxScreenshotPixelsPerDip': w/(meta['physicalWidth']/meta['scale']), 'visibleInsetsDip': [10, 10, 10, 10] if capsule else [16,16,16,16]})
dump('reference-manifest.json', {'schemaVersion': 1, 'baselineWindowsVersion': '0.7.56', 'capturedDate': '2026-11-19', 'captureMethod': 'Computer Use window screenshot; normalized logical dimensions', 'captures': captures, 'sources': [{'path': (SRC/n).relative_to(ROOT).as_posix(), 'sha256LF': text_hash(SRC/n)} for n in sources], 'assets': records, 'auditedMacCommit': '35f9f23f337bc4835bcd3a3e2729157cb63dcef1', 'unverifiedHere': ['new macOS build/render', 'runtime motion on macOS', 'ten-day expand/collapse visual state']})
print(f'Built tokens, fixtures and manifest: {len(captures)} captures, {len(sources)} sources, {len(records)} assets.')
