# XY 桌面播放器：当前状态与后续交接

更新时间：2026-09-01  
目标版本：`v1.0.0`

## 一句话状态

这是一个可独立运行的 Windows 10/11 x64 本地动态桌面播放器宿主。它使用 WinForms + WebView2 加载本地网页播放器，可在普通窗口与 Explorer 桌面图标后方的 WorkerW 模式之间切换。当前内容来自本地参考播放器；宿主已经完成通用化重命名，后续暖暖或绝区零版本应复制骨架并替换合法素材，不在本仓库直接覆盖当前内容。

## 已完成能力

- 普通窗口模式和桌面模式。
- 托盘切换模式、重新加载、桌面交互开关和退出。
- 当前模式与桌面交互菜单勾选状态。
- 页面加载后自动播放；启动时有其他应用在前台则保持静音，回桌面后首播。
- 任何普通前台程序出现时，声音约 500ms 淡出后暂停；回桌面立即恢复。
- 仅恢复被程序自动暂停的音频，保留用户手动暂停。
- 桌面文件保持在播放器上方；空白区域的左键、移动、滚轮可转给播放器。
- 右键始终交给 Explorer；桌面菜单打开时可用播放器空白区左键收起。
- 退出或切回窗口模式时，只隐藏本次使用、属于 Explorer、且已无子窗口的空 WorkerW，恢复原桌面。
- Explorer 重启后尝试重新挂载，失败时回退普通窗口。
- 完整素材导入、SHA-256 清单、自包含发布、真实 GUI 验收和 ZIP 解包哈希验证。

## 当前结构

- `XYDesktopPlayer.sln`：解决方案。
- `src/XYDesktopPlayer.Core`：不依赖 Windows UI 的解析、策略和几何规则。
- `src/XYDesktopPlayer.App`：WinForms、WebView2、WorkerW、托盘和输入转发。
- `tests/XYDesktopPlayer.Core.Tests`：核心策略测试。
- `tests/*.ps1`：品牌、导入、发布、淡出、启动播放和真实桌面验收。
- `content/reference-player`：本地完整素材，Git 忽略，必须保留。
- `manifests/reference-player.manifest.json`：144 个素材文件的大小与 SHA-256。
- `tools/Publish.ps1`：生成完整 Windows 包。
- `artifacts/publish/XY桌面播放器-v1.0.0-win-x64`：最终手测目录。
- `artifacts/release`：最终 GitHub 源码 ZIP 与 Windows Release ZIP。

## 已确认的失败方案

以下内容不要再次尝试；这里保留原因，替代大量重复日志：

1. **PowerShell 直接输入 EXE 文件名。** 当前目录默认不在命令搜索路径中，会报找不到命令。应使用 `./XYDesktopPlayer.exe`，普通用户直接使用根目录启动器。
2. **退出时只调用 RedrawWindow。** 空 WorkerW 仍可保持可见，结果是黑色桌面。正确做法是验证目标 WorkerW 的类名、Explorer 进程归属和子窗口数；仅在为空时隐藏，再重绘 Progman。
3. **只检查真正全屏窗口或只检查最大化状态。** 会漏掉普通大小前台窗口。最终规则是任何可见、未最小化、未 cloaked、非本进程、非桌面 Shell 的前台窗口都阻止桌面播放。
4. **直接 audio.pause()。** 声音会生硬截断。最终通过 Web Audio 全局 GainNode 在约 500ms 内线性淡出，再暂停媒体元素。
5. **启动后先播放再由监控暂停。** 会出现约一秒短促声音。最终在首次播放前检查前台状态；被阻止时延迟首播。
6. **桌面交互把网页右键也交给播放器。** 会破坏 Windows 桌面菜单。最终右键始终属于 Explorer，左键空白区域负责收起已有桌面菜单并继续转发。
7. **把 Progman 句柄当作唯一桌面焦点。** Windows 可能把前台焦点给包含图标的 WorkerW。真实验收应识别 Explorer 桌面表面，并在测试期间保持桌面焦点。
8. **在发布目录内直接运行多轮测试。** WebView2 会生成 `app/data` 缓存并污染分发包。发布目录必须从干净暂存区生成，自检在包外独立工作目录运行。
9. **使用 Windows tar 生成中文文件名 ZIP。** 曾出现中文路径丢失但命令仍返回成功。最终使用 .NET `ZipFile` + UTF-8，并在新目录解压后逐文件比较。
10. **重命名项目后继续使用 --no-restore。** 新项目路径没有对应 assets 文件，会表现为 WebView2 引用缺失。先对新解决方案 restore，再 build `--no-restore`。

## 自动验证

在仓库根目录执行：

```powershell
& '.\tools\Invoke-DotNet.ps1' restore '.\XYDesktopPlayer.sln' --configfile '.\NuGet.Config' --ignore-failed-sources '-p:NuGetAudit=false'
& '.\tools\Invoke-DotNet.ps1' build '.\XYDesktopPlayer.sln' --configuration Release --no-restore
& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\XYDesktopPlayer.Core.Tests\XYDesktopPlayer.Core.Tests.csproj' --no-restore
& '.\tests\Branding.Tests.ps1'
& '.\tests\AudioFade.Tests.ps1'
& '.\tests\DesktopRestoration.Tests.ps1'
& '.\tests\ImportReferencePlayer.Tests.ps1'
& '.\tests\Publish.Tests.ps1'
& '.\tools\Run-SelfTest.ps1'
& '.\tools\Verify-ContentManifest.ps1'
```

需要真实 Windows 桌面会话的验收：

```powershell
& '.\tests\FullscreenPauseAcceptance.ps1'
& '.\tests\StartupPlaybackAcceptance.ps1'
& '.\tests\DesktopRestorationAcceptance.ps1'
```

## 用户手动验收清单

1. 解压 Release ZIP 到新目录，不要覆盖旧包。
2. 双击“普通窗口（备用）.cmd”，确认画面、音乐、歌词和按钮。
3. 退出后双击“启动桌面壁纸”，确认托盘和默认自动播放。
4. 打开普通窗口、最大化窗口和全屏程序，分别确认约 0.5 秒淡出。
5. 回桌面，确认立即恢复；手动暂停后切换程序，确认不会擅自恢复。
6. 测试桌面文件、空白区域点击、滚轮、右键桌面菜单与菜单收起。
7. 从托盘切换窗口/桌面模式，确认勾选状态。
8. 从托盘退出，确认原桌面图片恢复且没有黑屏。

## Git、标签与回滚

- 最终发布提交使用带注释的本地标签 `v1.0.0`。
- 没有远程仓库，不执行 push。
- 标签保存代码、测试、脚本与文档；媒体因体积与版权原因不进入 Git。
- 若手测发现问题，不删除当前故障证据。先记录复现步骤，再从 `v1.0.0` 检出源码并使用保留的 `content/reference-player` 重新发布；不要用 `git reset --hard` 覆盖未记录的问题现场。

## 制作其他游戏版本

1. 从 `v1.0.0` 导出 GitHub 源码骨架并建立新仓库。
2. 不复制本仓库的 `artifacts`、`data`、`bin/obj`、`.packages` 或日志。
3. 为新主题建立独立 `content/reference-player`，保持 `index.html`、`static`、`assets/covers`、`assets/audios`、`assets/lyrics` 接口。
4. 生成新的内容清单，先在普通窗口验收，再验收桌面交互。
5. 暖暖和绝区零分别使用独立仓库、独立产品名和独立 Release，不在一个仓库互相覆盖。
