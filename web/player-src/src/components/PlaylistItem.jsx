const PlaylistItem = ({
  song,
  index,
  active,
  changeId,
  backgroundColor,
  lineColor,
  textColor,
  playEffect,
}) => (
  <button className="playlistItem" type="button" onClick={() => {
    changeId(index);
    playEffect("click");
  }} style={{
    borderBottomColor: lineColor,
    backgroundColor: active ? lineColor : "transparent",
    color: active ? backgroundColor : textColor,
    fontWeight: active ? 600 : 400,
  }}>
    <span>{index + 1}.</span>
    <span>{song.title}</span>
  </button>
);

export default PlaylistItem;
