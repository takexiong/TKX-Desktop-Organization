@echo off
chcp 65001 >nul
cd /d "%~dp0"

if exist "%~dp0publish_fix\塔克熊桌面整理工具.exe" (
  start "" "%~dp0publish_fix\塔克熊桌面整理工具.exe"
  exit /b 0
)

if exist "%~dp0publish_fix\桌面图标整理.exe" (
  start "" "%~dp0publish_fix\桌面图标整理.exe"
  exit /b 0
)

if exist "%~dp0bin\Release\net9.0-windows\塔克熊桌面整理工具.exe" (
  start "" "%~dp0bin\Release\net9.0-windows\塔克熊桌面整理工具.exe"
  exit /b 0
)

dotnet run -c Release --no-launch-profile
if errorlevel 1 (
  echo.
  echo 启动失败。请确认已安装 .NET 9 Desktop Runtime：
  echo https://dotnet.microsoft.com/download/dotnet/9.0
  pause
)
