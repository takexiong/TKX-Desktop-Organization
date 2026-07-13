# 塔克熊桌面整理工具

作者：**塔克熊**

Windows 桌面分区整理工具：在桌面创建可拖动、可缩放的方框，把图标拖入框内集中管理。

## 快速开始

双击 **`启动.bat`**，或双击：

`publish\塔克熊桌面整理工具.exe`

（需已安装 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)）

若要生成可拷贝到其他电脑的独立包：

```bat
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## 功能

| 操作 | 说明 |
|------|------|
| 添加窗口 | 桌面生成约 2cm×2cm 正方形分区 |
| 拖动标题栏 | 移动整个方框，框内图标一起移动 |
| 拖边缘/四角 | 随意调整长宽 |
| 拖入图标 | 从桌面或资源管理器拖入文件/快捷方式 |
| 小/中/大 | 右上角切换框内图标尺寸 |
| 双击图标 | 打开对应程序或文件 |
| 右键图标 | 移出分区 |
| × | 删除该分区 |

布局自动保存到 `%AppData%\DesktopOrganizer\config.json`。

## 自动更新

程序会按顺序尝试：

1. `update.json` 清单（jsDelivr / gitmirror 等国内较稳的 CDN）
2. GitHub Releases API（兜底）
3. 下载时若是 GitHub 直链，会自动再试若干镜像

发版时请同时：

1. 提高 `DesktopOrganizer.csproj` 的 `Version`
2. 上传 `TakexiongDesktopOrganizer.exe` 到 GitHub Release
3. 更新根目录 `update.json` 里的 version / downloadUrl / mirrors

若仍不稳定，建议把 `update.json` 和安装包放到 **Gitee** 或 **阿里云 OSS**，并把 `Services/UpdateSettings.cs` 里的 `ManifestUrls` 第一项改成该地址。

## 说明

- 框内是图标引用，**不会删除**桌面原文件
- 关闭主窗口会隐藏控制面板，分区仍保留；点「退出程序」才完全关闭
- 翻墙时请开启「系统代理」或 TUN，有助于访问 GitHub 源
