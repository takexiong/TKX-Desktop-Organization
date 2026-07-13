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

## 自动更新（GitHub Releases）

1. 更新源仓库：`takexiong/TKX-Desktop-Organization`（见 `Services/UpdateSettings.cs`）

2. 发版时：
   - 把 `DesktopOrganizer.csproj` 里的 `Version` 改成新版本（如 `1.0.1`）
   - 重新 `dotnet publish ...`
   - 在 GitHub 创建 Release，**Tag** 写成 `v1.0.1`（需与版本号对应）
   - 上传资源文件，文件名保持为 **`塔克熊桌面整理工具.exe`**
   - Release 正文会显示在更新确认窗口里

程序启动后会自动检查；有新版本时主界面出现蓝色提示条，点击后可查看更新内容并确认更新。

## 说明

- 框内是图标引用，**不会删除**桌面原文件
- 关闭主窗口会隐藏控制面板，分区仍保留；点「退出程序」才完全关闭
