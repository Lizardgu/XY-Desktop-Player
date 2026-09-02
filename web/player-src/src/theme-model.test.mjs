import assert from "node:assert/strict";
import test from "node:test";
import {
  DEFAULT_ICONS,
  normalizeThemePayload,
  resolveEffect,
  resolveIcon,
} from "./theme-model.mjs";

const validPayload = () => ({
  format: 1,
  id: "warm-nikki",
  name: "无限暖暖",
  author: "测试作者",
  appearance: {
    accentColor: "#F2B8D5",
    textColor: "#FFFFFF",
    icons: {
      play: "https://theme.xydesktop.local/images/icons/play.png",
    },
    effects: {
      click: "https://theme.xydesktop.local/audio/click.mp3",
    },
  },
  songs: [
    {
      title: "测试歌曲",
      artist: "测试歌手",
      audio: "https://theme.xydesktop.local/audio/01.mp3",
      cover: "https://theme.xydesktop.local/images/covers/01.webp",
      lyrics: {
        original: "[00:00.00]测试歌词",
        translation: "[00:00.00]translation",
      },
      backgroundColor: "#D7A0B7",
      textColor: "#101820",
      accentColor: "#F2B8D5",
    },
  ],
});

test("normalizes per-song media lyrics and fixed colors", () => {
  const theme = normalizeThemePayload(validPayload());

  assert.equal(theme.id, "warm-nikki");
  assert.equal(theme.songs.length, 1);
  assert.equal(theme.songs[0].title, "测试歌曲");
  assert.equal(theme.songs[0].audio, "https://theme.xydesktop.local/audio/01.mp3");
  assert.equal(theme.songs[0].lyrics.original, "[00:00.00]测试歌词");
  assert.equal(theme.songs[0].backgroundColor, "#D7A0B7");
  assert.equal(theme.songs[0].textColor, "#101820");
});

test("uses common icons and silence when optional theme assets are absent", () => {
  const payload = validPayload();
  payload.appearance.icons = {};
  payload.appearance.effects = {};
  const theme = normalizeThemePayload(payload);

  assert.equal(resolveIcon(theme, "play"), DEFAULT_ICONS.play);
  assert.equal(resolveEffect(theme, "click"), null);
});

test("rejects network and local media URLs not issued by the desktop host", () => {
  const network = validPayload();
  network.songs[0].audio = "https://example.com/song.mp3";
  assert.throws(() => normalizeThemePayload(network), /theme\.xydesktop\.local/);

  const local = validPayload();
  local.songs[0].cover = "file:///C:/private/cover.png";
  assert.throws(() => normalizeThemePayload(local), /theme\.xydesktop\.local/);
});

test("rejects an empty song list", () => {
  const payload = validPayload();
  payload.songs = [];
  assert.throws(() => normalizeThemePayload(payload), /song/i);
});
