# 塔克熊桌面整理工具

Windows 桌面分区整理工具：在桌面创建可拖动、可缩放的方框，把图标拖入框内集中管理。

当前版本：**v1.0.6**

## 下载

- [Releases](https://github.com/takexiong/TKX-Desktop-Organization/releases)
- 最新安装包：`TakexiongDesktopOrganizer.exe`

## 快速开始

1. 下载并双击 `TakexiongDesktopOrganizer.exe`
2. 或在本仓库执行 `打包.bat` / `启动.bat`

（独立发布包为 self-contained，一般无需再装 .NET；开发运行需 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)）

## 功能

| 操作 | 说明 |
|------|------|
| 添加窗口 | 桌面生成约 2cm×2cm 正方形分区 |
| 拖动 / 缩放 | 拖标题栏移动；拖边缘/四角改大小 |
| 拖入桌面图标 | 自动从桌面隐藏，收入分区 |
| 小/中/大 | 切换框内图标尺寸 |
| 锁 | 锁定窗口，禁止拖动/缩放 |
| 开机自启 | 主界面一键开启/关闭 |
| 托盘图标 | 隐藏后面板可从右下角再次打开 |
| 检查更新 | 自动/手动检查新版本 |

布局自动保存到 `%AppData%\DesktopOrganizer\config.json`。

## v1.0.6 更新

- 新增开机自启开关
- 桌面图标拖入分区后隐藏，移出/删分区可还原
- 托盘唤出、单实例、窗口锁定与退出关窗修复

## 说明

- 移出分区或删除分区时，已隐藏的桌面图标会还原
- 关闭主窗口会隐藏到托盘；点「退出程序」才完全关闭
