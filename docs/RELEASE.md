# 发布流程与 Release 命名

## 1. 打发布前自检(本地)

1. 本体候选:从当前源码构建(网页 `web/player` + EXE),布局为根目录 `app/ content/ 两个.cmd 使用说明.txt`,没有 `app\app` 嵌套与截图/缓存残留。
2. 清除候选包内残留(例如 `content/themes/绝区零/原料` 之类)。
3. 核对内容清单与哈希:
   - 内置主题素材与素材源逐文件 SHA-256 一致;
   - `git diff --check` 无错误。
4. 回归:核心测试全过、网页主题测试 4/4、自检/实机启动(窗口+壁纸各一次)。

## 2. 产出的附件命名(固定风格)

| 附件 | 命名 |
|---|---|
| 本体 | `XY桌面播放器-v{版本}-win-x64.zip` |
| 主题包 | `{主题名}主题包-v{版本}.zip`(zip 根 = 主题文件夹 + 一键安装 cmd) |
| 源码包(可选) | `XY桌面播放器-v{版本}-source.zip`(不含媒体,仓库 tag 已含) |

主题包结构(zip 根):
```
绝区零/
  安装到本机主题目录.cmd
  install-theme.ps1
  主题说明.txt
```

## 3. 建 Release(网页端,无需命令行)

1. GitHub 仓库 → **Releases** → **Draft a new release**;
2. 选/建标签 `v1.1.0`(本体与主题包建议同版本或用主题独立小版本);
3. 标题与简介(见 README 顶部"特点"可复制);
4. 依次把附件 zip **拖入 Attach binaries**;
5. 勾选 "Set as the latest release"(本体那次),点 **Publish release**。

## 4. 给使用者的三步话术(README/群内)

> 1) 先装本体:下载并解压 `XY桌面播放器-vX.Y.Z-win-x64.zip`,双击「双击这里-启动桌面壁纸.cmd」;
> 2) 想要某主题:下载 `绝区零主题包-vX.Y.Z.zip`,解压后双击「安装到本机主题目录.cmd」;
> 3) 托盘 → 主题 → 「重新扫描主题」→ 点主题名切换。

## 5. 一次性对账清单

- [ ] README 顶部截图已替换为真实截图
- [ ] 本体 zip 在干净机器(无 SDK)可运行(有 WebView2 Runtime)
- [ ] 主题 zip 解压→一键安装→重新扫描成功
- [ ] 附件大小与文件数记录在本次 Release 说明里
- [ ] 媒体授权声明(ATTRIBUTION.md)随包/附注出现
