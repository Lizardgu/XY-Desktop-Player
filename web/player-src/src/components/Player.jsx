import React from "react";
import { toReadableTime, useEffectEvent } from "../helpers";

const Player = ({
  song,
  audioRef,
  userPaused,
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
  const displayPaused = userPaused || !hostPlaybackAllowed;

  React.useEffect(() => {
    playbackStateRef.current = { hostPlaybackAllowed, userPaused };
  }, [hostPlaybackAllowed, userPaused]);

  React.useEffect(() => {
    const audio = audioRef.current;
    const prepared = () => {
      onAudioPrepared();
      const playbackState = playbackStateRef.current;
      if (playbackState.hostPlaybackAllowed && !playbackState.userPaused) {
        audio.play().catch(() => {});
      }
    };
    audio.pause();
    audio.crossOrigin = "anonymous";
    audio.src = song.audio;
    audio.load();
    setProgress(0);
    setDuration(0);
    audio.addEventListener("loadedmetadata", prepared, { once: true });
    return () => audio.removeEventListener("loadedmetadata", prepared);
  }, [audioRef, onAudioPrepared, song.audio]);

  React.useEffect(() => {
    const audio = audioRef.current;
    if (userPaused || !hostPlaybackAllowed) audio.pause();
    else if (audio.src) audio.play().catch(() => {});
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
    if (replay) audioRef.current.play().catch(() => {});
    else changeSong(true);
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
