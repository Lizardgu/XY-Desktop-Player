# GitHub 与 Release 使用说明

## 两个压缩包的区别

- `XY桌面播放器-GitHub源码-v1.0.0.zip`：来自 Git 标签，只含源码、测试、文档和脚本。可以用于创建 GitHub 仓库，不包含音乐图片和编译产物。
- `XY桌面播放器-v1.0.0-win-x64.zip`：可直接运行的 Windows 完整包，包含当前播放器素材，适合本机手动测试或在明确拥有授权时放入 Release。

## 推荐上传顺序

1. 在 GitHub 新建空仓库，不勾选自动生成 README、License 或 `.gitignore`。
2. 将本地仓库添加为远程并推送分支和 `v1.0.0` 标签，或者解压 GitHub 源码 ZIP 后新建仓库。
3. 创建标题为 `XY桌面播放器 v1.0.0` 的 Release。
4. 只有确认当前媒体的再发布权后，才上传 Windows 完整 ZIP。
5. Release 说明写明系统要求：Windows 10/11 x64、Microsoft Edge WebView2 Runtime。

## 大文件与版权边界

`content/reference-player` 约 621MiB，已被 `.gitignore` 排除。不要把它直接强制加入普通 Git 历史；否则每次克隆都会永久下载这部分内容，且 GitHub 单文件限制可能拒绝提交。

宿主源码与第三方网页播放器/媒体是两个层次。生成本地 Release ZIP 不代表已经取得音乐、封面、歌词或动画的公开再发布权。若权利不明确，公开仓库只上传源码；完整 ZIP 仅本地保留，或放在由用户自行负责权限的私有存储中。

GitHub Desktop 不是必需工具。当前仓库已由命令行 Git 管理；在用户明确选择远程仓库地址和公开/私有状态之前，不配置远程、不 push。
