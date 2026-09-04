import React from "react";
import { toReadableTime, useEffectEvent } from "../helpers";

const Player = ({
  song,
  audioRef,
  userPaused,
  autoPaused,
  hostPlaybackAllowed,
  onManualPause,
  onAudioPrepared,
  changeSong,
  shuffle,
  replay,
  setPlaybackMode,
  textSize,
  icon,
  playEffect,
  themeEpoch,
}) => {
  const [trackProgress, setProgress] = React.useState(0);
  const [volume, setVolume] = React.useState(() => {
    const stored = Number(localStorage.getItem("xy-volume"));
    return Number.isFinite(stored) && stored >= 0 && stored <= 1 ? stored : 0.2;
  });
  const [volumeBarOpacity, setVolumeBarOpacity] = React.useState(0);
  const [duration, setDuration] = React.useState(0);
  const volumeBarFadeTimeoutId = React.useRef(null);
  const playbackStateRef = React.useRef({ hostPlaybackAllowed, userPaused });
  const displayPaused = userPaused || !hostPlaybackAllowed || autoPaused;

  React.useEffect(() => {
    playbackStateRef.current = { hostPlaybackAllowed, userPaused };
  }, [hostPlaybackAllowed, userPaused]);

  React.useEffect(() => {
    const audio = audioRef.current;
    const prepared = () => {
      onAudioPrepared();
      const playbackState = playbackStateRef.current;
      if (playbackState.hostPlaybackAllowed && !playbackState.userPaused) {
        window.__xyDesktopPrepareManualResume?.();
        audio.play().catch(() => {});
      }
    };
    // Retry the media load a couple of times when metadata is slow or the first
    // attempt fails, so a single slow/aborted load cannot leave the theme silent.
    let attempts = 0;
    let retryTimer = null;
    const report = (kind) => {
      try {
        window.chrome?.webview?.postMessage({
          type: "xy-page-report",
          kind,
          attempts,
          readyState: audio.readyState,
          networkState: audio.networkState,
          errorCode: audio.error?.code ?? null,
          src: String(audio.src || song.audio).slice(0, 160),
        });
      } catch (_) {}
    };
    const tryLoad = () => {
      attempts += 1;
      audio.src = song.audio;
      audio.load();
      if (retryTimer !== null) clearTimeout(retryTimer);
      if (attempts <= 2) {
        retryTimer = setTimeout(() => {
          if (audio.src === song.audio && !audio.error && audio.readyState < 1) {
            console.error(`xy-audio-stalled retry=${attempts} src=${song.audio}`);
            report("audio-stalled");
            tryLoad();
          }
        }, 4000);
      }
    };
    const onAudioError = () => {
      console.error(
        `xy-audio-error code=${audio.error?.code ?? "none"} src=${song.audio}`);
      report("audio-error");
      if (attempts <= 2) tryLoad();
    };
    audio.pause();
    audio.crossOrigin = "anonymous";
    tryLoad();
    setProgress(0);
    setDuration(0);
    audio.addEventListener("loadedmetadata", prepared, { once: true });
    audio.addEventListener("error", onAudioError);
    return () => {
      audio.removeEventListener("loadedmetadata", prepared);
      audio.removeEventListener("error", onAudioError);
      if (retryTimer !== null) clearTimeout(retryTimer);
    };
  }, [audioRef, onAudioPrepared, song.audio, themeEpoch]);

  React.useEffect(() => {
    const audio = audioRef.current;
    if (userPaused || !hostPlaybackAllowed) {
      // 手动暂停与切换窗口暂停统一:同样做 500ms 淡出后停止。
      if (userPaused && audio.src && !audio.paused) {
        window.__xyDesktopFadeStop?.();
      } else {
        audio.pause();
      }
    } else if (audio.src) {
      window.__xyDesktopPrepareManualResume?.();
      audio.play().catch(() => {});
    }
  }, [audioRef, hostPlaybackAllowed, userPaused]);

  React.useEffect(() => {
    audioRef.current.volume = volume;
    localStorage.setItem("xy-volume", String(volume));
  }, [audioRef, volume]);

  React.useEffect(() => {
    const audio = audioRef.current;
    const updateProgress = () => setProgress(Math.floor(audio.currentTime));
    const updateDuration = () => setDuration(Number.isFinite(audio.duration) ? audio.duration : 0);
    audio.addEventListener("timeupdate", updateProgress);
    audio.addEventListener("durationchange", updateDuration);
    return () => {
      audio.removeEventListener("timeupdate", updateProgress);
      audio.removeEventListener("durationchange", updateDuration);
    };
  }, [audioRef]);

  const onSongEnded = useEffectEvent(() => {
    if (replay) {
      window.__xyDesktopPrepareManualResume?.();
      audioRef.current.play().catch(() => {});
    } else changeSong(true);
  });

  React.useEffect(() => {
    const audio = audioRef.current;
    audio.addEventListener("ended", onSongEnded);
    return () => audio.removeEventListener("ended", onSongEnded);
  }, [audioRef, onSongEnded]);

  React.useEffect(() => () => clearTimeout(volumeBarFadeTimeoutId.current), []);

  React.useEffect(() => () => audioRef.current.pause(), [audioRef]);

  const showVolume = () => {
    setVolumeBarOpacity(1);
    clearTimeout(volumeBarFadeTimeoutId.current);
    volumeBarFadeTimeoutId.current = setTimeout(() => setVolumeBarOpacity(0), 2000);
  };

  const adjustVolume = (difference) => {
    setVolume((current) => Math.min(1, Math.max(0, Math.round((current + difference) * 10) / 10)));
    playEffect("click");
    showVolume();
  };

  const previous = () => {
    if (audioRef.current.currentTime >= 3) audioRef.current.currentTime = 0;
    else changeSong(false);
    playEffect("click");
  };

  return (
    <section className="player" aria-label="音乐播放器">
      <p className="playerText" style={{ fontSize: `${1.25 * textSize}rem` }}>
        {song.title}{song.artist ? ` · ${song.artist}` : ""}
      </p>
      <div className="progressRow">
        <span>{toReadableTime(trackProgress)}</span>
        <input aria-label="播放进度" type="range" step="1" min="0" value={trackProgress}
          max={duration || 0} className="audio-progress"
          onChange={(event) => { audioRef.current.currentTime = Number(event.target.value); }} />
        <span>{toReadableTime(duration)}</span>
      </div>
      <div className="audioControls">
        <Control icon={icon(replay ? "replayActive" : "replay")} label="单曲循环" onClick={() => {
          setPlaybackMode("replay", !replay); playEffect("click");
        }} />
        <Control icon={icon("volumeDown")} label="降低音量" onClick={() => adjustVolume(-0.1)} />
        <Control icon={icon("backward")} label="上一首" onClick={previous} />
        <Control icon={icon(displayPaused ? "play" : "pause")} label={displayPaused ? "播放" : "暂停"} onClick={() => {
          onManualPause(!displayPaused); playEffect("click");
        }} />
        <Control icon={icon("forward")} label="下一首" onClick={() => {
          changeSong(true); playEffect("click");
        }} />
        <Control icon={icon("volumeUp")} label="提高音量" onClick={() => adjustVolume(0.1)} />
        <Control icon={icon(shuffle ? "shuffleActive" : "shuffle")} label="随机播放" onClick={() => {
          setPlaybackMode("shuffle", !shuffle); playEffect("click");
        }} />
      </div>
      <div className="volumeDisplay" style={{ opacity: volumeBarOpacity }} aria-live="polite">
        <div className="volume-bar"><div className="volume-bar-current" style={{ width: `${volume * 100}%` }} /></div>
        <span>{Math.round(volume * 100)}%</span>
      </div>
    </section>
  );
};

const Control = ({ icon, label, onClick }) => (
  <button className="iconButton" type="button" onClick={onClick} aria-label={label} title={label}>
    <img className="audioIcon" src={icon} alt="" />
  </button>
);

export default Player;
