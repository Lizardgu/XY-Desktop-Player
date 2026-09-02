const Optionbar = ({
  icon,
  playEffect,
  clockHandler,
  playerHandler,
  playlistHandler,
  visualizerHandler,
  lyricsHandler,
  changeSong,
}) => {
  const invoke = (handler) => {
    handler();
    playEffect("click");
  };
  const options = [
    ["clock", "显示或隐藏时钟", clockHandler],
    ["visualizer", "显示或隐藏音频可视化", visualizerHandler],
    ["next", "切换歌曲", changeSong],
    ["player", "显示或隐藏播放器", playerHandler],
    ["playlist", "显示或隐藏播放列表", playlistHandler],
    ["lyrics", "显示或隐藏歌词", lyricsHandler],
  ];
  return (
    <div className="optionBar">
      {options.map(([name, label, handler]) => (
        <button key={name} className="optionIcon iconButton" type="button"
          onClick={() => invoke(handler)} aria-label={label} title={label}>
          <img src={icon(name)} alt="" />
        </button>
      ))}
    </div>
  );
};

export default Optionbar;
