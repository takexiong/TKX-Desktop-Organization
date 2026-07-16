@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 正在打包独立 EXE（约需几十秒）...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish --nologo --ignore-failed-sources
if errorlevel 1 (
  echo.
  echo 打包失败。
  pause
  exit /b 1
)

echo.
echo 完成：publish\塔克熊桌面整理工具.exe
echo 可直接拷贝到其他电脑运行，无需安装 .NET。
explorer "%~dp0publish"
pause
