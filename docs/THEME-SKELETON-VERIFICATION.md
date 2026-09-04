# 主题骨架候选版验证记录

> 历史记录说明：以下内容记录 2026-09-01 的旧候选版，不等于 2026-09-02 恢复分支的当前实机验收。此后窗口检测源码又有修改，用户此前仍报告自动暂停失败。当前状态以 `docs/PROJECT-STATUS.md` 和 `docs/自动暂停故障交接-2026-09-02.md` 为准。

验证日期：2026-09-01

分支：`feature/theme-pack-skeleton`

候选提交：`f1448bc`（发布结构）及此前同分支提交

## 自动测试

- .NET Release `win-x64` 构建：0 警告、0 错误。
- 核心控制台测试：全部通过。
- Web 主题模型：4/4 通过。
- React 生产构建：成功，无 source map 进入发布播放器。
- 界面机械检查：0 项警告。
- 内置主题生成测试：通过，确认使用 `assets/songs`、封面、歌词并对缺失素材明确失败。
- 品牌测试：通过。
- 500 ms 音频淡出静态测试：通过。
- 新发布布局测试：通过；根目录整洁，启动器 CRLF 正常，依赖只在 `app`。
- `git diff --check`：通过。
- `git fsck --full`：对象库完整；仅报告不可达 blob，不影响提交或回滚。

## 真实窗口验证

从源码宿主和最终候选目录各运行一次捕获：

- 页面地址：`https://xydesktop.local/index.html`
- 主题素材地址：每个主题使用独立地址，例如 `https://theme-bocchi.xydesktop.local/...`
- 当前主题：`bocchi`
- 歌曲数量：32
- 页面图片：11/11 成功加载
- 音频实际推进：约 7.17 秒
- 启动播放：成功
- 手动暂停保护：成功
- 前台监控：运行中，无错误

真实前台程序验收：

- 淡出耗时：约 501 ms
- 自动暂停：1 次
- 回桌面恢复：1 次
- 原本手动暂停的音频：未被恢复

候选包从自己的 `app/XYDesktopPlayer.exe` 与 `content` 目录启动成功，不依赖源码目录中的网页或主题定义。

## 候选包审计

路径：`artifacts/publish/XY桌面播放器-theme-skeleton-win-x64`

- 文件：613
- 总大小：819,176,697 字节（约 781.2 MiB）
- 内置主题媒体：32 首歌曲、17 张封面、64 份歌词
- 不包含：`preview.gif`、`project.json`、旧 `content/reference-player`、旧主题网页 `static`、旧 `audios` 点击音目录

关键文件 SHA-256：

| 文件 | SHA-256 |
|---|---|
| `app/XYDesktopPlayer.exe` | `0A28C372040733A978F3B892483B8D0DA98A8E4337EFF9181B2AFE4CA0911602` |
| `content/player/index.html` | `128621DD167FE9F33F6DA82DF2EE9C29FA84ADF70F42B9C8892BF8AC082EE013` |
| `content/themes/孤独摇滚/pack.json` | `B0D4271E3AB038852021819A249819555CF50F6BFC5E72471624D717F7CD8B3C` |
| `content/themes/孤独摇滚/songs.json` | `73A3AFC242D3B767009F7C059F777E8D89FA009E742CB017904EE13CACBF8382` |
| `使用说明.txt` | `C73D78F32BEFB118E469FC7AA7A455A2ECCB2189899D29F23DFDF53CC9977615` |

## 尚需用户手动确认

自动验证不能替代以下桌面体验检查：

1. 托盘主题菜单显示、勾选和“打开主题安装目录”。
2. 手动复制一个外部主题文件夹后扫描、切换、删除和回退。
3. 桌面文件遮罩、空白区域操作和 Windows 右键菜单。
4. 从托盘退出后原桌面图片恢复。

在这些项目由用户明确验收前，不合并到 `main`、不打新正式标签、不推送本分支。
