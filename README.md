# XY桌面播放器

一个面向 Windows 10 的本地动态桌面播放器宿主。目标是让普通网页播放器脱离 Wallpaper Engine 独立运行，并支持两种模式：

- `window`：普通独立窗口，用于调试和内容验收。
- `wallpaper`：嵌入桌面图标后方，由系统托盘控制。

当前开发分支已经提炼为“共用播放器 + 主题文件夹”：内置不可删除的“孤独摇滚”，外部主题由用户手动解压到主题安装目录后扫描加载。每个主题可为每首歌分别提供音频、封面、歌名、歌手、歌词和固定背景色。格式见 [`docs/THEME-PACK-GUIDE.md`](docs/THEME-PACK-GUIDE.md)。

Steam Workshop 原目录不会被修改。当前版本状态与后续换主题方法见 `docs/PROJECT-STATUS.md`。

## Git 与大文件

源代码、主题定义、测试、工具和文档进入 Git。导入后的音乐、封面、歌词、WebView2 数据、Node 依赖和编译产物均被忽略。含媒体的完整包只适合在确认素材授权后作为 Release 附件分发。

## 构建环境

- Windows 10/11 x64
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

仓库内的 `tools/Invoke-DotNet.ps1` 会优先使用 `XY_DOTNET` 指定的 SDK，也能识别本次工作区内的项目级 SDK。

## 导入参考播放器

在 PowerShell 中进入仓库目录后运行：

```powershell
& '.\tools\Import-ReferencePlayer.ps1' -Source 'E:\SteamLibrary\steamapps\workshop\content\431960\2905017768'
```

脚本只读取来源，把完整副本写到 `content/reference-player`，并在 `manifests/reference-player.manifest.json` 记录每个文件的大小和 SHA-256。目标已存在时脚本会停止，不会静默覆盖。

导入器自身可用一个微型临时内容包测试：

```powershell
& '.\tests\ImportReferencePlayer.Tests.ps1'
```

## 构建和运行 A

首次还原 WebView2 开发包：

```powershell
& '.\tools\Invoke-DotNet.ps1' restore '.\XYDesktopPlayer.sln' --configfile '.\NuGet.Config'
```

构建与非图形自检：

```powershell
& '.\tools\Invoke-DotNet.ps1' build '.\XYDesktopPlayer.sln' --no-restore
& '.\tools\Run-SelfTest.ps1'
```

打开普通独立窗口：

```powershell
& '.\tools\Run-Window.ps1'
```

自动加载页面、启动一首歌、保存 PNG/DOM 报告并退出：

```powershell
& '.\tools\Run-CaptureTest.ps1'
```

验收输出位于 `artifacts/smoke/reference-player.png` 和同名 JSON；该目录是可重复生成的，因此不进入 Git。

## 运行 B：桌面图标后方

```powershell
& '.\tools\Run-Wallpaper.ps1'
```

启动后播放器窗口不显示在任务栏，而是进入 Windows Explorer 的 `WorkerW` 桌面层。右下角托盘中的“XY桌面播放器”菜单提供：

- 切换-窗口模式
- 切换-桌面模式
- 开启或关闭桌面交互
- 切换主题、打开主题安装目录、重新扫描主题
- 重新加载播放器
- 退出 XY桌面播放器

“切换-窗口模式”和“切换-桌面模式”左侧的勾表示当前实际模式，两项始终只有一项被勾选；桌面嵌入失败并回退窗口模式时，勾也会跟着实际模式变化。

“重新加载播放器”相当于重新读取当前主题，适合页面卡住或覆盖主题素材后使用；它会从主题第一首歌重新开始，但保留当前进程内的手动暂停状态。

新主题下载为 ZIP 后，需要先由用户解压成文件夹，再放入托盘菜单打开的主题安装目录。程序不直接读取 ZIP，也不会执行主题中的脚本。新建或删除主题文件夹后点“重新扫描主题”；普通覆盖不会打断当前播放。

页面加载完成后会自动播放当前默认歌曲，不需要先点播放键。如果启动时已有其他普通程序处于前台，播放器会先保持静音，等用户回到桌面后再开始播放，避免先响一下再暂停。若系统临时拒绝自动播放，程序保持可操作，仍可手动点击播放。

播放器按固定间隔检查桌面上方的普通程序窗口。检测到阻挡窗口后，音乐会先在约 500 毫秒内平滑淡出到静音，再自动暂停；回到桌面后恢复本次被自动暂停的歌曲。原本由用户手动暂停的歌曲不会被自动播放。该检测在恢复分支中已有新的顶层窗口枚举实现，但仍需完成真实桌面验收，不能仅依据自动测试认定所有窗口类型都已覆盖。

桌面交互默认开启。桌面文件和快捷方式像遮罩一样保留在播放器上方：

- 左键、双击、鼠标移动和滚轮只有在桌面图标未覆盖的区域才会转给播放器。
- 点到桌面图标时仍由 Explorer 正常处理，不会同时触发下面的播放器。
- 右键始终属于 Windows，继续打开系统桌面菜单，不会打开网页右键菜单。
- 桌面菜单打开后，左键点播放器空白区域会先收起菜单，并继续把这次点击交给播放器。
- 关闭“显示桌面图标”后，图标遮罩为空，整个空白桌面都可以操作播放器。
- 前台程序、任务栏、开始菜单、托盘和弹出菜单不会被转发。

如果需要暂时完整操作桌面图标，可在托盘取消“桌面交互”，或者切换到普通窗口。图标范围无法可靠读取时，程序会优先把鼠标保留给 Windows，不会锁死桌面。

双击托盘图标也会切回普通窗口。Explorer 重启后，程序会监听任务栏重建消息并尝试重新嵌入；失败时自动回退为普通窗口。

从托盘退出时，程序会先从 WorkerW 桌面层解除播放器；如果本次承载播放器的 Explorer WorkerW 已经没有任何子窗口，程序会把这个空层隐藏，再要求 Explorer 重绘桌面。这样原桌面会重新显露，同时不会改动或清空 Windows 原有壁纸设置，也不会影响桌面图标所在的 WorkerW 或其他程序的窗口。

如果 Wallpaper Engine 正在运行，两者可能同时占用桌面层。长期使用 B 前，建议由用户自己暂停或退出 Wallpaper Engine；本程序不会擅自关闭它。

可重复执行短时 B 验收（数秒后自动退出）：

```powershell
& '.\tools\Run-CaptureTest.ps1' -Mode wallpaper -Output '.\artifacts\smoke\wallpaper-player.png'
```

同名 `.host.json` 中的 `desktopAttached: true` 与 `parentClassName: WorkerW` 是桌面嵌入成功的机器可读证据。

## 发布独立程序

```powershell
& '.\tools\Publish.ps1'
```

默认输出到 `artifacts/publish/XY桌面播放器-theme-skeleton-win-x64`。这是包含共用播放器和内置“孤独摇滚”主题的 win-x64 自包含程序，目标电脑不需要另装 .NET SDK；仍需要系统的 WebView2 Runtime。

成品根目录只保留两个启动器、一个说明文件、`app` 和 `content`。普通用户双击 `双击这里-启动桌面壁纸.cmd`；`普通窗口（备用）.cmd` 仅用于故障排查。`app` 中的 DLL 和运行库不是启动入口。

发布布局为 `app`、`content/player` 和 `content/themes/孤独摇滚`。旧 Wallpaper Engine 的 `preview.gif`、`project.json`、旧网页构建和无用点击音不会进入成品。

完整包包含原播放器的照片、音乐和歌词。对外上传或转发前，需要先确认已取得相应内容的再发布权利；源码骨架与主题模板不附带这些媒体。

远程仓库为 [Lizardgu/XY-Desktop-Player](https://github.com/Lizardgu/XY-Desktop-Player)。当前主题骨架位于本地 `feature/theme-pack-skeleton-recovery` 分支；用户手动验收前不合并、不打新正式标签、不推送该分支。
