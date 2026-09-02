# XY 桌面播放器主题包指南

## 安装方式

主题包下载时可以是 ZIP，但程序不会直接读取 ZIP。用户需要：

1. 完整解压 ZIP。
2. 从托盘打开“主题 → 打开主题安装目录”。
3. 把解压后的整个主题文件夹复制进去。
4. 点“重新扫描主题”，再选择主题。

外部主题位于 `%LOCALAPPDATA%\XYDesktopPlayer\Themes`。每个一级子文件夹是一套主题；删除该文件夹就是卸载。文件被覆盖时，当前播放不会突然变化，需手动点“重新加载播放器”。

内置 `bocchi`（孤独摇滚）是最终回退主题，不能被外部主题覆盖或删除。以后可以修改发布包的默认选择，但不需要改变骨架。

## 文件夹结构

```text
示例主题/
├─ pack.json
├─ songs.json
├─ audio/
│  ├─ 01.mp3
│  └─ 02.flac
├─ images/
│  ├─ covers/
│  │  ├─ 01.png
│  │  └─ 02.jpg
│  └─ background-01.webp       # 可选
├─ lyrics/
│  ├─ 01-original.lrc
│  ├─ 01-romanized.lrc         # 可选
│  └─ 01-translation.lrc       # 可选
└─ ui/                         # 可选：主题自定义图标、字体、提示音
```

路径统一相对于主题文件夹。禁止网址、盘符绝对路径、`..` 越界和重解析点绕出主题目录。主题只能提供数据与媒体，不能携带或执行 JavaScript、EXE。

## pack.json

```json
{
  "format": 1,
  "id": "warm-nikki",
  "name": "无限暖暖",
  "author": "作者名称",
  "appearance": {
    "textColor": "#FFF8EC",
    "accentColor": "#F6B44A",
    "logo": "images/logo.png",
    "font": "ui/theme-font.woff2",
    "icons": {
      "play": "ui/icons/play.png",
      "pause": "ui/icons/pause.png"
    },
    "effects": {
      "click": "ui/audio/click.mp3"
    }
  }
}
```

- `format` 当前固定为 `1`。
- `id` 只能用小写字母、数字、点、横线和下划线；复制更新时保持不变。
- `name` 是托盘中显示的主题名称。
- `appearance` 全部可选；缺少图标时使用播放器共用图标，缺少提示音时静音。

## songs.json

```json
[
  {
    "title": "歌曲名称",
    "artist": "歌手名称",
    "audio": "audio/01.mp3",
    "cover": "images/covers/01.png",
    "lyrics": {
      "original": "lyrics/01-original.lrc",
      "romanized": "lyrics/01-romanized.lrc",
      "translation": "lyrics/01-translation.lrc"
    },
    "backgroundColor": "#3C2946",
    "textColor": "#FFF8EC",
    "accentColor": "#F6B44A",
    "backgroundImage": "images/background-01.webp"
  }
]
```

每首歌必须有歌名、音频、封面、至少一份 LRC 歌词和固定背景色。歌手、文字色、强调色、背景图以及另外两种歌词可省略；省略颜色时继承 `pack.json`。

颜色使用 `#RRGGBB` 或 `#RRGGBBAA`。背景颜色是逐首固定值，不会从封面自动取色，因此可以针对暖暖、绝区零等主题逐曲人工搭配。

支持的常见素材类型由程序白名单控制：音频、图片、LRC、字体；不支持的扩展名会被拒绝。某一首歌损坏时会跳过；整包没有任何有效歌曲时，该主题不会出现在菜单中。

## 制作新主题的建议流程

1. 复制仓库中的 `themes/template`。
2. 先只放一首歌，确认音频、封面、歌词和颜色都正确。
3. 再批量补充其余歌曲，并保持稳定的文件名和主题 ID。
4. 普通窗口测试完成后，再测试桌面模式、前台淡出和退出恢复。
5. 分发前确认音乐、图片、歌词和字体的授权；空骨架本身不附带这些素材。
