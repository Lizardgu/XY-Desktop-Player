import React from "react";
import { MultipleLrc, useRecoverAutoScrollImmediately } from "react-lrc";

const Lyrics = ({ song, audioRef, backgroundColor, lineColor, playEffect }) => {
  const [currentTime, setCurrentTime] = React.useState(0);
  const { signal, recoverAutoScrollImmediately } = useRecoverAutoScrollImmediately();

  React.useEffect(() => {
    const audio = audioRef.current;
    const updateCurrentTime = () => setCurrentTime(audio.currentTime * 1000);
    audio.addEventListener("timeupdate", updateCurrentTime);
    return () => audio.removeEventListener("timeupdate", updateCurrentTime);
  }, [audioRef]);

  const lrcs = [song.lyrics.original, song.lyrics.translation ?? song.lyrics.romanized].filter(Boolean);
  const lineRenderer = ({ active, line: { children, startMillisecond } }) => (
    <button type="button" className="lyricLine"
      style={{
        backgroundColor: active ? lineColor : "transparent",
        color: active ? backgroundColor : "var(--theme-text-color)",
        fontWeight: active ? 600 : 400,
      }}
      onClick={() => {
        audioRef.current.currentTime = (startMillisecond + 1) / 1000;
        recoverAutoScrollImmediately();
        playEffect("click");
      }}>
      {children.map((child) => <span key={child.id}>{child.content}</span>)}
    </button>
  );

  return (
    <section className="lrc-container" style={{ borderColor: lineColor }} aria-label="歌词">
      <MultipleLrc className="lrc" lrcs={lrcs} lineRenderer={lineRenderer}
        currentMillisecond={currentTime} recoverAutoScrollSingal={signal} />
    </section>
  );
};

export default Lyrics;
