# 内容目录

运行时内容放在 `content/reference-player`。该目录由 `tools/Import-ReferencePlayer.ps1` 从用户指定的本地来源复制生成，不进入普通 Git 历史。

这样既能让播放器真正脱离 Steam Workshop 目录运行，也不会把数百 MiB 的音乐和图片写进每一次 Git 克隆。

