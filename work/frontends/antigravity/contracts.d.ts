/** LumiBar local frontend protocol v1. All property names are camelCase. */
export interface City { name: string; latitude: number; longitude: number; region: string; timeZone: string; }
export interface WeatherAlert {
  id: string; event: string; level: 'red' | 'orange' | 'yellow' | 'blue' | 'unknown';
  headline: string; description: string; instruction: string; source: string; area: string;
  issuedAt: string | null; startsAt: string | null; endsAt: string | null; cancelled?: boolean;
}
export interface Forecast {
  temperature: number; feelsLike: number; code: number; humidity: number;
  wind: number; windDirection: number; rain: number | null;
  hours: Array<{ time: string; temperature: number; code: number; rain: number; isDay?: boolean | null }>;
  days: Array<{ date: string; min: number; max: number; code: number; rain: number }>;
  updated: string; aqi: number | null; pm25: number | null; pm10: number | null;
  airError: string | null; source: string; estimatedFeelsLike: boolean; timeZoneLabel: string;
  shortTermPrecipitation?: PrecipitationSeries | null;
  sunrise?: string | null; sunset?: string | null; isDay?: boolean | null;
  /** Sea-level pressure in hPa; null/missing when unavailable or from an older cache. */
  pressureHpa?: number | null;
  /** Current UV index (dimensionless). Zero is valid, including at night. */
  uvIndex?: number | null;
  alerts?: WeatherAlert[] | null;
  aqiStandard?: 'CN AQI' | 'US AQI' | 'QAQI' | string;
  aqiCategory?: string | null; conditionText?: string | null;
  attributions?: string[] | null; providerNote?: string[] | null;
}
export interface RainInterval { start: string; end: string; amountMm: number | null; }
export interface PrecipitationSeries { updatedAt: string; source: string; precisionNote: string; intervals: RainInterval[]; }
export interface PrecipitationTrend {
  windowStart: string; windowEnd: string; source: string; kind: 'model-trend' | 'nowcast'; intervalMinutes: 5 | 15;
  precisionNote: string; complete: boolean; intervals: RainInterval[];
}
export interface Media {
  key: string; title: string; artist: string; album: string; source: string;
  playing: boolean; position: number; duration: number; hasArtwork: boolean; accent: string;
  /** Optional second cover colour, #RRGGBB. Fall back to accent when absent/null. */
  secondaryAccent?: string | null;
  controls: { toggle: boolean; previous: boolean; next: boolean; seek: boolean; shuffle?: boolean; repeat?: boolean } | null;
  shuffleEnabled?: boolean | null; repeatMode?: 'None' | 'Track' | 'List' | null;
}
export interface Settings {
  city: City | null; favorites: City[]; theme: 'System' | 'Light' | 'Dark';
  /** Read-only through settings.patch; cities.locate enables Windows location. cities.locateGps is a legacy alias. */
  autoLocateCity?: boolean;
  /** Legacy write alias: sets both script fonts unless explicit script fields override it. */
  fontFamily?: string | null;
  /** Missing: older backend. Explicit null: system default for this script. */
  chineseFontFamily?: string | null; westernFontFamily?: string | null;
  glow: number; glassOpacity: number; coverGlow: boolean; coverSaturation?: number; animations: boolean;
  showWeather: boolean; showMusic: boolean; spectrum: boolean; hideInFullscreen: boolean;
  autoLyrics: boolean; autoStart: boolean; closeToTray: boolean; weatherRefreshMinutes: 10 | 20 | 30 | 60;
  fahrenheit: boolean; windMetersPerSecond: boolean; gap: number; musicWidth: number;
  lyricsFontSize: number;
  /** Offset of the current song only. Patch with lyricsOffsetTrackKey to reject a stale song. */
  lyricsOffset: number; lyricsOffsetTrackKey?: string | null;
  /** Defaults used when the matching font preference is null; bundled with the builtin UI. */
  defaultChineseFontFamily?: string; defaultWesternFontFamily?: string;
  sensitivity: number; enabledPlugins: string[];
}
export interface State {
  protocolVersion: 1; capturedAt: string; stateVersion?: number; instanceId?: string;
  appearance: { preference: 'System' | 'Light' | 'Dark'; popupTheme: 'Light' | 'Dark'; taskbarTheme: 'Light' | 'Dark'; coverGlow: boolean; coverSaturation?: number; glow: number; glassOpacity: number; animations: boolean; fontFamily?: string | null; chineseFontFamily?: string | null; westernFontFamily?: string | null };
  weather: { city: City | null; status: string; locationBusy?: boolean; locationStatus?: string; forecast: Forecast | null; precipitationNextHour?: PrecipitationTrend | null };
  media: Media | null;
  lyrics: { trackKey: string | null; status: string; offsetSeconds: number; lines: Array<{ seconds: number | null; text: string }> };
  settings: Settings;
}
export interface Spectrum { capturedAt: string; playing: boolean; source: 'system-output'; status: string; bands: number[]; }
export interface Rect { left: number; top: number; right: number; bottom: number; visible: boolean; }
export interface WindowHints {
  coordinateSpace: 'physical-screen-pixels'; recommendedGapDip: number; popupSizeOwnedBy: 'frontend';
  taskbar: (Rect & { scale: number }) | null; weatherAnchor: Rect; musicAnchor: Rect; reservationSupported: boolean;
  blankZoneRect?: Rect | null; geometryStatus?: 'measured' | 'unavailable'; measuredAt?: string | null;
  layoutRecommendation?: { weatherWidth: number; musicWidth: number; lite: boolean; requiredWidth: number; requiresReservation: boolean } | null;
  musicSlot?: CapsuleSlot | null; weatherSlot?: CapsuleSlot | null; trayWidthPx?: number | null;
  layoutState?: 'full' | 'lite' | 'hidden' | 'unavailable' | 'reservation-required';
  nativeReservation?: ReservationSnapshot | null;
  renderingOwner?: RenderingOwner; slotsVersion?: number; renderer?: RendererStatus;
}
/** Physical pixel geometry. at uses a monotonic clock, not Unix time. */
export interface ReservationSnapshot { status: string; error: number; slot: Omit<Rect, 'visible'> | null; widthDip: number; explorerPid: number; at: number; layerError?: number; taskbarBand?: number; layerRepairs?: number; }
export interface ReservationState { snapshot: ReservationSnapshot | null; slots: RendererSlots | null; owner: RenderingOwner; slotsVersion: number; renderer: RendererStatus; }
export interface CapsuleSlot { rect: Omit<Rect, 'visible'> | null; mode: 'full' | 'lite' | 'hidden'; }
export interface CapsulePlanRequest { musicPreferredDip?: number; musicMinDip?: number; weatherPreferredDip?: number; weatherMinDip?: number; heightDip?: number; wasLite?: boolean; }
export interface ChangeBatch { instanceId: string; version: number; topics: string[]; resyncRequired: boolean; timedOut: boolean; }
export interface InstalledFont { family: string; displayName: string; aliases: string[]; }
export interface FontList { source: 'windows-directwrite'; installedCount: number; total: number; offset: number; hasMore: boolean; items: InstalledFont[]; }
export type SettingsPatch = Partial<Omit<Settings, 'city' | 'favorites' | 'autoLocateCity' | 'autoStart' | 'closeToTray' | 'enabledPlugins'>>;
export interface RpcRequest { id: string; method: string; params?: Record<string, unknown>; }
export type RpcResponse<T> = { id: string; ok: true; data: T } | { id: string | null; ok: false; error: { code: string; message: string } };

/** API v1 / backend 0.7: real taskbar renderer ownership, see BACKEND_RENDERER-0.7.md. */
export type RenderingOwner = 'builtin' | 'external' | 'preview';
export interface RendererSizes { musicFullDip: number; musicLiteDip: number; weatherFullDip: number; weatherLiteDip: number; gapDip: number; heightDip: number; }
export interface RegisterRendererRequest { rendererId: string; pid: number; capsuleHwnds: { music: string; weather: string }; preferredSizes?: Partial<RendererSizes>; }
export interface RendererAuth { leaseId: string; leaseToken: string; }
export interface RendererSlots { musicSlot: CapsuleSlot | null; weatherSlot: CapsuleSlot | null; state: string; }
export interface RendererLeaseResponse { leaseId: string; leaseToken: string | null; phase: 'pending' | 'active'; leaseDurationMs: number; renewAfterMs: number; expiresInMs: number; slotsVersion: number; slots: RendererSlots | null; renderingOwner: RenderingOwner; }
export interface RendererStatus { owner: RenderingOwner; lease: { leaseId: string; rendererId: string; pid: number; phase: 'pending' | 'active'; expiresInMs: number; leaseDurationMs: number; renewAfterMs: number; sizes: RendererSizes } | null; lastReleaseReason: string | null; }
export interface ExternalFrontend { id: string; name: string; version: string; manifestPath: string; entry: string; mode: string; error: string | null; }
export interface FrontendList { roots: string[]; items: ExternalFrontend[]; renderer: RendererStatus; }
