import React from "react";
import Optionbar from "./Optionbar";

const Navigation = ({ icon, playEffect, ...handlers }) => {
  const [active, setActive] = React.useState(false);
  const toggle = () => {
    setActive((value) => !value);
    playEffect("open");
  };
  return (
    <nav aria-label="播放器显示选项">
      {active ? <Optionbar icon={icon} playEffect={playEffect} {...handlers} /> : null}
      <button className="navigation iconButton" type="button" onClick={toggle}
        aria-label={active ? "关闭显示选项" : "打开显示选项"} aria-expanded={active}>
        <img src={icon("settings")} alt="" />
      </button>
    </nav>
  );
};

export default Navigation;
