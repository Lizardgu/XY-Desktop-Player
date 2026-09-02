import React from "react";
import PlaylistItem from "./PlaylistItem";

const PageSize = 5;

const Playlist = ({
  songs,
  songIndex,
  changeId,
  songLists,
  changeMode,
  mode,
  addSong,
  removeSong,
  backgroundColor,
  lineColor,
  textColor,
  translate,
  icon,
  playEffect,
}) => {
  const [page, setPage] = React.useState(0);
  const songIds = React.useMemo(
    () => mode === 0 ? songs.map((_, index) => index) : songLists[mode - 1],
    [mode, songLists, songs],
  );
  const pageCount = Math.max(1, Math.ceil(songIds.length / PageSize));

  React.useEffect(() => {
    const position = songIds.indexOf(songIndex);
    if (position >= 0) setPage(Math.floor(position / PageSize));
    else setPage(0);
  }, [mode, songIds, songIndex]);

  const turnPage = (direction) => {
    setPage((current) => (current + direction + pageCount) % pageCount);
    playEffect("click");
  };

  const selectMode = (nextMode) => {
    changeMode(nextMode);
    playEffect("click");
  };

  const inList = (listNumber) => songLists[listNumber - 1].includes(songIndex);
  const toggleList = (listNumber) => {
    if (inList(listNumber)) removeSong(songIndex, listNumber);
    else addSong(songIndex, listNumber);
    playEffect("click");
  };

  return (
    <section className="playlist" style={{ borderColor: lineColor, color: textColor }} aria-label="播放列表">
      <div className="playlistNavigation">
        {[0, 1, 2].map((entry) => (
          <button key={entry} type="button" onClick={() => selectMode(entry)}
            aria-pressed={mode === entry} style={{ borderColor: lineColor }}>
            {entry === 0 ? translate("default") : translate("playlistNumber", entry)}
          </button>
        ))}
      </div>
      <div className="playlist-container">
        <div className="playlist-item-container">
          {songIds.slice(page * PageSize, page * PageSize + PageSize).map((id) => (
            <PlaylistItem key={id} song={songs[id]} index={id} active={songIndex === id}
              changeId={changeId} backgroundColor={backgroundColor} lineColor={lineColor}
              textColor={textColor} playEffect={playEffect} />
          ))}
          {songIds.length === 0 ? <p className="emptyPlaylist">这个歌单还是空的</p> : null}
        </div>
        <div className="playlist-scroll" style={{ borderColor: lineColor }}>
          <button type="button" className="playlist-scroll-img iconButton" onClick={() => turnPage(-1)} aria-label="上一页">
            <img src={icon("pageUp")} alt="" />
          </button>
          <button type="button" className="playlist-scroll-img iconButton" onClick={() => turnPage(1)} aria-label="下一页">
            <img src={icon("pageDown")} alt="" />
          </button>
        </div>
      </div>
      <div className="playlistFooter" style={{ borderColor: lineColor }}>
        {mode === 0 ? [1, 2].map((listNumber) => (
          <button key={listNumber} type="button" onClick={() => toggleList(listNumber)}>
            {inList(listNumber) ? "−" : "+"} {translate("playlistNumber", listNumber)}
          </button>
        )) : (
          <button type="button" onClick={() => {
            removeSong(songIndex, mode);
            playEffect("click");
          }}>{translate("removeCurrentSong")}</button>
        )}
      </div>
    </section>
  );
};

export default Playlist;
