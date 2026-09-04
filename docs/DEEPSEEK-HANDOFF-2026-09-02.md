# XY桌面播放器：DeepSeek 交接说明

更新时间：2026-09-02

## 1. 真实项目与 Git 状态

请直接打开：

```text
C:\Users\Administrator\Documents\Codex\2026-08-30\d-yt-dlp-downloads-bv1yc1gbaekf-mkv\outputs\XY桌面播放器-GitHub恢复版
```

- 远程仓库：`https://github.com/Lizardgu/XY-Desktop-Player`
- 当前本地分支：`feature/theme-pack-skeleton-recovery`
- 当前 HEAD：`f739561 docs: record completed recovery cleanup`
- 远程 `main` 与 `v1.0.0`：`3d9144e`

不要从 GitHub 的 `main` 重新开始，不要重置当前分支。主题骨架已经在本地恢复分支中，而且最新的主题切换修复仍是未提交修改。先运行 `git status` 和 `git diff`。

## 2. 项目概览

这是一个 Windows 10/11 x64 桌面播放器，把 Wallpaper Engine 网页播放器移植成独立程序。

- C# / .NET 8 WinForms：Windows 宿主、托盘、WorkerW 桌面嵌入、窗口检测、主题扫描和鼠标转发。
- WebView2：承载播放器网页。
- React 静态页面：封面、歌词、歌单、播放控制、颜色和时钟。
- 普通窗口模式：调试和素材验收。
- 桌面壁纸模式：嵌入桌面图标后方，通过托盘控制。
- 自包含 win-x64 发布：目标电脑不需要 .NET SDK，但需要 WebView2 Runtime。

## 3. 最终主题架构

采用“一个共用播放器 + 多个独立主题文件夹”，不要为每个游戏复制整套程序。

- 发布包内置不可删除的“孤独摇滚”，ID 为 `bocchi`，作为最终回退主题。
- 以后可下载“无限暖暖”“绝区零”等主题。
- 下载物可以是 ZIP，但程序不直接读取 ZIP；用户手动解压后，把完整主题文件夹放入：

```text
%LOCALAPPDATA%\XYDesktopPlayer\Themes
```

- 一个一级子文件夹是一套主题；删除文件夹就是卸载。
- 新建或删除主题后点托盘中的“重新扫描主题”。
- 默认主题以后可以切换，不需要修改骨架。
- 外部主题不能覆盖内置 `bocchi`。
- 主题只能提供数据和媒体，不能执行 JavaScript 或 EXE。

每首歌支持独立的歌名、歌手、音频、封面、LRC 歌词、背景色、文字色和强调色；背景图可选。完整格式见 `docs\THEME-PACK-GUIDE.md`。

## 4. 已完成且必须保留的行为

托盘菜单包含：

- `切换-窗口模式`
- `切换-桌面模式`
- 当前模式左侧勾选，两个模式只能勾选一个
- 桌面交互
- 主题选择、打开主题目录、重新扫描主题
- 重新加载播放器
- 退出

播放和桌面行为：

- 启动后自动尝试播放，不要求再点一次播放键。
- 桌面上存在其他普通程序窗口时，声音约 500 ms 淡出，然后暂停。
- 回到桌面后，仅恢复本次自动暂停的歌曲；用户手动暂停的不能自动恢复。
- 启动或主题加载期间已有其他窗口时，不能先响一下或稍后突然播放。
- 桌面图标像遮罩一样覆盖播放器；图标区域仍由 Explorer 处理。
- 空白区域可把左键、双击、滚轮和鼠标移动转给播放器。
- 右键始终打开 Windows 桌面菜单，不打开网页右键菜单。
- 桌面右键菜单打开后，左键点播放器空白区域应收起菜单并继续传递该点击。
- 托盘退出时必须解除 WorkerW、隐藏播放器创建的空 WorkerW并重绘 Explorer，使原 Windows 壁纸恢复，不能黑屏。

这些已有代码和测试。处理主题功能时不要重写或删除。

## 5. 当前未提交的主题切换修复

用户已经确认 MP3 和 JPG 本身可以播放、显示。真正的问题是：运行中从“绝区零”切换到“孤独摇滚”后，两个主题的音乐和图片都可能失效。

根因：所有主题原来共用 `theme.xydesktop.local`，切换文件夹映射后 WebView2/网页可能继续使用旧资源环境；React 的 `theme-model.mjs` 也把允许地址写死成旧主机。

当前未提交修复：

- 每个主题根据 ID 获得独立 WebView2 主机。
- 示例：`theme-bocchi.xydesktop.local`、`theme-zenless-zone-zero.xydesktop.local`。
- C# 发送主题数据时使用本次映射的真实主机。
- React 接受合法的 `theme-<id>.xydesktop.local`，仍拒绝其他网络/本地地址。
- 不能直接转换成 DNS 标签的 ID 使用稳定 SHA-256 短哈希。

主要修改文件：

```text
src\XYDesktopPlayer.App\PlayerForm.cs
src\XYDesktopPlayer.Core\ThemeRuntimePayload.cs
src\XYDesktopPlayer.Core\ThemeWebContentMapping.cs
tests\XYDesktopPlayer.Core.Tests\Program.cs
web\player-src\src\theme-model.mjs
web\player-src\src\theme-model.test.mjs
web\player
docs\plans\2026-09-02-theme-host-isolation-design.md
```

不要用 `git reset --hard`、`git checkout --` 或重新克隆覆盖这些修改。

## 6. 已有验证证据

- .NET Release 构建：0 警告、0 错误。
- 核心控制台测试全部通过。
- React 主题模型测试 4/4 通过。
- 500 ms 淡出、桌面退出恢复、品牌和发布结构测试通过。
- `git diff --check` 通过。
- 真实 WebView2 捕获达到 `theme-ready: id=bocchi; songs=32`。
- 11/11 张界面图片加载成功，识别到 3 个音频元素。
- 内置主题与恢复候选包核对：115 个文件，哈希差异为 0。

捕获时桌面存在其他程序，因此自动暂停规则阻止声音启动，捕获退出码为 6、音频时间为 0。这是当时窗口环境的预期结果，不是资源加载失败。

## 7. 当前候选包与人工验收

最新候选包：

```text
artifacts\publish\XY桌面播放器-theme-switch-fix-win-x64
```

- 617 个文件，823,275,423 字节。
- 根目录应只有 `app`、`content`、两个 CMD 启动器和 `使用说明.txt`。
- 没有错误的 `app\app` 嵌套，也没有截图测试的 `app\data` 缓存。

回滚候选包：

```text
artifacts\publish\XY桌面播放器-theme-skeleton-recovery-win-x64
```

人工测试前必须从托盘彻底退出旧实例，因为程序有全局单实例锁。然后运行最新候选包的 `普通窗口（备用）.cmd`，依次测试：

```text
孤独摇滚 -> 绝区零 -> 孤独摇滚 -> 再重复一次
```

每次检查封面、歌名、音乐和歌词。自动测试不能代替这次托盘热切换验收；用户确认前不要宣称问题最终修复。

## 8. 绝区零测试主题

外部“绝区零”主题在用户主题目录中。已有单曲：

- title：`Come Alive`
- artist：`San Z`
- MP3 和封面已经单独验证可用。
- MP3 受支持，并非只支持 FLAC；图片尺寸也不是故障根因。
- 若暂时没有歌词，需要补一份符合主题格式的最小 LRC。

不要把所有游戏素材混进同一个主题文件夹，也不要为每个游戏复制播放器。

## 9. 自动暂停的风险提醒

`docs\自动暂停故障交接-2026-09-02.md` 保存了旧问题的详细分析，但其中旧 worktree 路径和候选包名已经过时，只参考判断逻辑。

窗口检测过去在用户真实桌面出现过误判，所以还需在桌面模式复测资源管理器、浏览器、设置、普通/最大化窗口、任务栏缩略图、Win+D、Alt+Tab、手动暂停以及主题加载时打开程序。

如果失败，先记录真实运行 EXE 路径、窗口句柄/类名/进程、检测分类、播放状态和采取的动作，不要凭猜测继续添加窗口类名或定时器补丁。

## 10. 工作边界

1. 先阅读差异并诊断，不要重构整个项目。
2. 不要删除内置孤独摇滚媒体、外部绝区零主题或两个候选包。
3. 不要运行破坏性 Git 命令。
4. 用户验收前不合并 `main`、不打标签、不推送、不创建正式 Release。
5. 每次修改后重新运行核心测试、网页测试、Release 构建和候选包实机启动。
6. 明确区分自动验证与用户真实验收。
7. 删除缓存前先列出精确路径和大小，只删除可重建且确认无用的目录。

## 11. DeepSeek 的第一项任务

先不要改代码。请：

1. 确认当前分支和未提交修改仍存在。
2. 阅读本文件、`README.md`、`docs\THEME-PACK-GUIDE.md` 和主题资源地址隔离设计。
3. 确认最新候选包结构完整。
4. 指导用户完成“孤独摇滚 -> 绝区零 -> 孤独摇滚”实机切换。
5. 如果复现失败，先收集日志和实际运行进程路径，再提出单一根因和最小修复。
