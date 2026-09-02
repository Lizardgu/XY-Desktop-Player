import React from "react";
import Navigation from "./components/Navigation";
import Clock from "./components/Clock";
import Player from "./components/Player";
import AudioVisualizer from "./components/AudioVisualizer";
import Playlist from "./components/Playlist";
import Lyrics from "./components/Lyrics";
import Localization from "./components/Localization.json";
import { randomExcluded } from "./helpers";
import {
  normalizeThemePayload,
  resolveEffect,
  resolveIcon,
} from "./theme-model.mjs";

const Main = () => {
  const [theme, setTheme] = React.useState(null);
  const [themeError, setThemeError] = React.useState(null);
  const [songIndex, setSongIndex] = React.useState(0);
  const [playerVisible, setPlayerVisible] = React.useState(true);
  const [clockVisible, setClockVisible] = React.useState(true);
  const [visualizerVisible, setVisualizerVisible] = React.useState(true);
  const [playlistVisible, setPlaylistVisible] = React.useState(true);
  const [lyricsVisible, setLyricsVisible] = React.useState(true);
  const [playlistMode, setPlaylistMode] = React.useState(0);
  const [songLists, setSongLists] = React.useState([[], []]);
  const [shuffle, setShuffle] = React.useState(true);
  const [replay, setReplay] = React.useState(false);
  const [userPaused, setUserPaused] = React.useState(false);
  const [hostPlaybackAllowed, setHostPlaybackAllowed] = React.useState(false);
  const [audioPrepared, setAudioPrepared] = React.useState(false);
  const [uiVolume] = React.useState(0.5);
  const [textSize] = React.useState(1);
  const audioRef = React.useRef(new Audio());

  const receiveTheme = React.useCallback((payload) => {
    try {
      const normalized = normalizeThemePayload(payload);
      audioRef.current.pause();
      audioRef.current.removeAttribute("src");
      audioRef.current.load();
      setTheme(normalized);
      setSongIndex(0);
      setPlaylistMode(0);
      setSongLists(loadSongLists(normalized.id, normalized.songs.length));
      setUserPaused(false);
      setHostPlaybackAllowed(false);
      setAudioPrepared(false);
      setThemeError(null);
    } catch (error) {
      setTheme(null);
      setThemeError(error instanceof Error ? error.message : String(error));
    }
  }, []);

  React.useEffect(() => {
    const webView = window.chrome?.webview;
    const onMessage = (event) => receiveTheme(event.data);
    webView?.addEventListener("message", onMessage);
    window.__xyDesktopReceiveTheme = receiveTheme;
    webView?.postMessage({ type: "player-ready" });
    return () => {
      webView?.removeEventListener("message", onMessage);
      delete window.__xyDesktopReceiveTheme;
    };
  }, [receiveTheme]);

  React.useEffect(() => {
    window.__xyDesktopGetPlayerState = () => ({
      themeId: theme?.id ?? null,
      manualPaused: userPaused,
      audioPrepared,
    });
    window.__xyDesktopApplyManualPaused = (paused) => {
      const nextPaused = paused === true;
      setUserPaused(nextPaused);
      if (nextPaused) audioRef.current.pause();
      return nextPaused;
    };
    window.__xyDesktopStartPlayback = async () => {
      if (!theme || !audioPrepared || !audioRef.current.src) {
        return { started: false, source: null, error: "theme audio is not ready" };
      }
      try {
        setHostPlaybackAllowed(true);
        setUserPaused(false);
        window.__xyDesktopResumeAudioContext?.();
        await audioRef.current.play();
        return { started: true, source: audioRef.current.currentSrc || audioRef.current.src, error: null };
      } catch (error) {
        return {
          started: false,
          source: audioRef.current.currentSrc || audioRef.current.src || null,
          error: error instanceof Error ? error.message : String(error),
        };
      }
    };
    window.__xyDesktopReleasePlayer = () => {
      audioRef.current.pause();
      audioRef.current.removeAttribute("src");
      audioRef.current.load();
      setHostPlaybackAllowed(false);
      setAudioPrepared(false);
    };
    return () => {
      delete window.__xyDesktopGetPlayerState;
      delete window.__xyDesktopApplyManualPaused;
      delete window.__xyDesktopStartPlayback;
      delete window.__xyDesktopReleasePlayer;
    };
  }, [theme, userPaused, audioPrepared]);

  React.useEffect(() => {
    if (!theme || !audioPrepared) return;
    window.chrome?.webview?.postMessage({
      type: "theme-ready",
      themeId: theme.id,
      songCount: theme.songs.length,
    });
  }, [theme, audioPrepared]);

  React.useEffect(() => {
    if (theme) saveSongLists(theme.id, songLists);
  }, [theme, songLists]);

  const currentSong = theme?.songs[songIndex] ?? null;
  const handleAudioPrepared = React.useCallback(() => setAudioPrepared(true), []);
  const icon = React.useCallback((name) => resolveIcon(theme, name), [theme]);
  const playEffect = React.useCallback((name) => {
    const source = resolveEffect(theme, name);
    if (!source) return;
    const sound = new Audio(source);
    sound.volume = uiVolume;
    sound.play().catch(() => {});
  }, [theme, uiVolume]);

  const changeSong = (forward) => {
    if (!theme || theme.songs.length < 2) return;
    setAudioPrepared(false);
    setSongIndex((current) => {
      if (shuffle) return randomExcluded(0, theme.songs.length - 1, current);
      const direction = forward ? 1 : -1;
      return (current + direction + theme.songs.length) % theme.songs.length;
    });
  };

  const selectSong = (index) => {
    if (!theme || index < 0 || index >= theme.songs.length || index === songIndex) return;
    setAudioPrepared(false);
    setSongIndex(index);
  };

  const setPlaybackMode = (kind, enabled) => {
    if (kind === "shuffle") {
      setShuffle(enabled);
      setReplay(false);
    } else {
      setReplay(enabled);
      setShuffle(false);
    }
  };

  const addSong = (index, listNumber) => {
    setSongLists((current) => current.map((list, listIndex) =>
      listIndex === listNumber - 1 && !list.includes(index)
        ? [...list, index]
        : list));
  };

  const removeSong = (index, listNumber) => {
    setSongLists((current) => current.map((list, listIndex) =>
      listIndex === listNumber - 1
        ? list.filter((entry) => entry !== index)
        : list));
  };

  const translate = (key, ...values) => {
    const language = navigator.language?.toLowerCase().startsWith("zh") ? "zh-chs" : "en-us";
    const template = Localization[language]?.[key] ?? Localization["en-us"][key] ?? key;
    return values.reduce((text, value) => text.replace("%d", String(value)), template);
  };

  if (themeError) {
    return (
      <main className="themeState themeStateError" role="alert">
        <h1>主题无法加载</h1>
        <p>{themeError}</p>
        <p>请从托盘菜单重新扫描主题，或切换回“孤独摇滚”。</p>
      </main>
    );
  }

  if (!theme || !currentSong) {
    return (
      <main className="themeState" aria-live="polite">
        <div className="themeLoadingMark" aria-hidden="true" />
        <p>正在等待主题…</p>
      </main>
    );
  }

  const mainStyle = {
    backgroundColor: currentSong.backgroundColor,
    color: currentSong.textColor,
    "--accent-color": currentSong.accentColor,
    "--theme-text-color": currentSong.textColor,
    ...(currentSong.backgroundImage
      ? { backgroundImage: `url("${currentSong.backgroundImage}")` }
      : {}),
  };

  return (
    <main className="Main" style={mainStyle} data-theme-id={theme.id}>
      {theme.appearance.logo ? (
        <img className="themeLogo" src={theme.appearance.logo} alt={theme.name} />
      ) : null}
      {visualizerVisible ? <AudioVisualizer lineColor={currentSong.accentColor} /> : null}
      <img className="mainImage" src={currentSong.cover} alt={`${currentSong.title} 专辑封面`} />
      <Navigation
        icon={icon}
        playEffect={playEffect}
        clockHandler={() => setClockVisible((value) => !value)}
        playerHandler={() => setPlayerVisible((value) => !value)}
        playlistHandler={() => setPlaylistVisible((value) => !value)}
        visualizerHandler={() => setVisualizerVisible((value) => !value)}
        lyricsHandler={() => setLyricsVisible((value) => !value)}
        changeSong={() => changeSong(true)}
      />
      {playlistVisible ? (
        <Playlist
          songs={theme.songs}
          songIndex={songIndex}
          changeId={selectSong}
          songLists={songLists}
          changeMode={setPlaylistMode}
          mode={playlistMode}
          addSong={addSong}
          removeSong={removeSong}
          backgroundColor={currentSong.backgroundColor}
          lineColor={currentSong.accentColor}
          textColor={currentSong.textColor}
          translate={translate}
          icon={icon}
          playEffect={playEffect}
        />
      ) : null}
      {clockVisible ? <Clock textSize={textSize} use24HourClock showSeconds /> : null}
      {playerVisible ? (
        <Player
          song={currentSong}
          audioRef={audioRef}
          userPaused={userPaused}
          hostPlaybackAllowed={hostPlaybackAllowed}
          onManualPause={setUserPaused}
          onAudioPrepared={handleAudioPrepared}
          changeSong={changeSong}
          shuffle={shuffle}
          replay={replay}
          setPlaybackMode={setPlaybackMode}
          textSize={textSize}
          icon={icon}
          playEffect={playEffect}
        />
      ) : null}
      {lyricsVisible ? (
        <Lyrics
          song={currentSong}
          audioRef={audioRef}
          backgroundColor={currentSong.backgroundColor}
          lineColor={currentSong.accentColor}
          playEffect={playEffect}
        />
      ) : null}
    </main>
  );
};

function loadSongLists(themeId, songCount) {
  try {
    const value = JSON.parse(localStorage.getItem(`xy-playlists:${themeId}`));
    if (!Array.isArray(value) || value.length !== 2) return [[], []];
    return value.map((list) => Array.isArray(list)
      ? list.filter((entry) => Number.isInteger(entry) && entry >= 0 && entry < songCount)
      : []);
  } catch {
    return [[], []];
  }
}

function saveSongLists(themeId, lists) {
  try {
    localStorage.setItem(`xy-playlists:${themeId}`, JSON.stringify(lists));
  } catch {
    // Player preferences are optional and must never block playback.
  }
}

export default Main;
