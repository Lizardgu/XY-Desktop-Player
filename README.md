# Nikki Desktop

一个面向 Windows 10 的本地动态桌面播放器宿主。目标是让普通网页播放器脱离 Wallpaper Engine 独立运行，并支持两种模式：

- `window`：普通独立窗口，用于调试和内容验收。
- `wallpaper`：嵌入桌面图标后方，由系统托盘控制。

当前分支正在按 `docs/superpowers/plans/2026-08-30-standalone-desktop-player.md` 实现。Steam Workshop 原目录不会被修改。

## Git 与大文件

源代码、测试、导入工具和文档进入 Git。导入后的音乐、封面、歌词、WebView2 数据和编译产物均被忽略。将来公开上传前，需要另行选择 Git LFS、GitHub Release 或“用户自行导入素材”的分发方式。

## 构建环境

- Windows 10/11 x64
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

仓库内的 `tools/Invoke-DotNet.ps1` 会优先使用 `NIKKI_DOTNET` 指定的 SDK，也能识别本次工作区内的项目级 SDK。

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
& '.\tools\Invoke-DotNet.ps1' restore '.\NikkiDesktop.sln' --configfile '.\NuGet.Config'
```

构建与非图形自检：

```powershell
& '.\tools\Invoke-DotNet.ps1' build '.\NikkiDesktop.sln' --no-restore
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

启动后播放器窗口不显示在任务栏，而是进入 Windows Explorer 的 `WorkerW` 桌面层。右下角托盘中的 Nikki Desktop 菜单提供：

- 切换到普通窗口
- 重新嵌入桌面图标后方
- 重新加载播放器
- 退出 Nikki Desktop

双击托盘图标也会切回普通窗口。Explorer 重启后，程序会监听任务栏重建消息并尝试重新嵌入；失败时自动回退为普通窗口。

如果 Wallpaper Engine 正在运行，两者可能同时占用桌面层。长期使用 B 前，建议由用户自己暂停或退出 Wallpaper Engine；Nikki Desktop 不会擅自关闭它。

可重复执行短时 B 验收（数秒后自动退出）：

```powershell
& '.\tools\Run-CaptureTest.ps1' -Mode wallpaper -Output '.\artifacts\smoke\wallpaper-player.png'
```

同名 `.host.json` 中的 `desktopAttached: true` 与 `parentClassName: WorkerW` 是桌面嵌入成功的机器可读证据。
