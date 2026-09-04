const THEME_HOST_PATTERN =
  /^theme-[a-z0-9](?:[a-z0-9-]{0,55}[a-z0-9])?\.xydesktop\.local$/;
const THEME_HOST_EXAMPLE = "theme-<id>.xydesktop.local";
const HEX_COLOR = /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/;

export const DEFAULT_ICONS = Object.freeze({
  backward: "/ui/icons/backward.png",
  clock: "/ui/icons/darkclock.png",
  forward: "/ui/icons/forward.png",
  lyrics: "/ui/icons/lyricsIcon.png",
  next: "/ui/icons/darkbackground.png",
  pause: "/ui/icons/pause.png",
  play: "/ui/icons/play.png",
  playlist: "/ui/icons/playlist.png",
  replay: "/ui/icons/replay.png",
  replayActive: "/ui/icons/replayToggle.png",
  settings: "/ui/icons/darksetting.png",
  shuffle: "/ui/icons/shuffle.png",
  shuffleActive: "/ui/icons/shuffleToggle.png",
  visualizer: "/ui/icons/darksound.png",
  volumeDown: "/ui/icons/volumeMinus.png",
  volumeUp: "/ui/icons/volumePlus.png",
  player: "/ui/icons/darkheadphones.png",
  pageUp: "/ui/icons/upBar.png",
  pageDown: "/ui/icons/downBar.png",
});

export function normalizeThemePayload(payload) {
  requireObject(payload, "theme");
  if (payload.format !== 1) {
    throw new Error(`Unsupported theme format: ${payload.format}`);
  }

  const id = requireString(payload.id, "theme id");
  const name = requireString(payload.name, "theme name");
  const sourceAppearance = payload.appearance ?? {};
  requireObject(sourceAppearance, "theme appearance");
  const textColor = colorOr(sourceAppearance.textColor, "#FFFFFF", "theme text color");
  const accentColor = colorOr(sourceAppearance.accentColor, "#FFFFFF", "theme accent color");
  const icons = normalizeAssetMap(sourceAppearance.icons, "theme icon");
  const effects = normalizeAssetMap(sourceAppearance.effects, "theme effect");
  const songs = Array.isArray(payload.songs) ? payload.songs : [];
  if (songs.length === 0) {
    throw new Error("Theme must contain at least one song.");
  }

  return {
    format: 1,
    id,
    name,
    author: optionalString(payload.author),
    appearance: {
      logo: optionalThemeUrl(sourceAppearance.logo, "theme logo"),
      font: optionalThemeUrl(sourceAppearance.font, "theme font"),
      textColor,
      accentColor,
      icons,
      effects,
    },
    songs: songs.map((song, index) => normalizeSong(song, index, textColor, accentColor)),
  };
}

export function resolveIcon(theme, name) {
  return theme?.appearance?.icons?.[name] ?? DEFAULT_ICONS[name] ?? null;
}

export function resolveEffect(theme, name) {
  return theme?.appearance?.effects?.[name] ?? null;
}

function normalizeSong(song, index, defaultTextColor, defaultAccentColor) {
  requireObject(song, `song ${index + 1}`);
  const lyrics = song.lyrics ?? {};
  requireObject(lyrics, `song ${index + 1} lyrics`);
  return {
    title: requireString(song.title, `song ${index + 1} title`),
    artist: optionalString(song.artist),
    audio: requireThemeUrl(song.audio, `song ${index + 1} audio`),
    cover: requireThemeUrl(song.cover, `song ${index + 1} cover`),
    lyrics: {
      original: optionalString(lyrics.original),
      romanized: optionalString(lyrics.romanized),
      translation: optionalString(lyrics.translation),
    },
    backgroundColor: requireColor(song.backgroundColor, `song ${index + 1} background color`),
    textColor: colorOr(song.textColor, defaultTextColor, `song ${index + 1} text color`),
    accentColor: colorOr(song.accentColor, defaultAccentColor, `song ${index + 1} accent color`),
    backgroundImage: optionalThemeUrl(song.backgroundImage, `song ${index + 1} background image`),
  };
}

function normalizeAssetMap(value, label) {
  if (value == null) return {};
  requireObject(value, `${label} map`);
  return Object.fromEntries(
    Object.entries(value).map(([key, url]) => [
      requireString(key, `${label} key`),
      requireThemeUrl(url, `${label} ${key}`),
    ]),
  );
}

function requireThemeUrl(value, label) {
  const text = requireString(value, label);
  let url;
  try {
    url = new URL(text);
  } catch {
    throw new Error(`${label} must use https://${THEME_HOST_EXAMPLE}/.`);
  }
  if (
    url.protocol !== "https:" ||
    !THEME_HOST_PATTERN.test(url.hostname) ||
    url.username ||
    url.password ||
    url.port
  ) {
    throw new Error(`${label} must use https://${THEME_HOST_EXAMPLE}/.`);
  }
  return url.href;
}

function optionalThemeUrl(value, label) {
  return value == null || value === "" ? null : requireThemeUrl(value, label);
}

function requireColor(value, label) {
  const color = requireString(value, label);
  if (!HEX_COLOR.test(color)) {
    throw new Error(`${label} must use #RRGGBB or #RRGGBBAA.`);
  }
  return color;
}

function colorOr(value, fallback, label) {
  return value == null || value === "" ? fallback : requireColor(value, label);
}

function requireString(value, label) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${label} must be a non-empty string.`);
  }
  return value.trim();
}

function optionalString(value) {
  return typeof value === "string" && value.trim() !== "" ? value.trim() : null;
}

function requireObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }
}
