@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ===== 塔克熊桌面整理工具 v1.0.6 发布到 GitHub =====
echo.

where gh >nul 2>&1
if errorlevel 1 (
  echo 未找到 GitHub CLI。请先安装：winget install GitHub.cli
  pause
  exit /b 1
)

gh auth status
if errorlevel 1 (
  echo.
  echo 请先登录 GitHub：
  echo   gh auth login -h github.com -p https -w
  pause
  exit /b 1
)

if not exist "publish\TakexiongDesktopOrganizer.exe" (
  echo 未找到 publish\TakexiongDesktopOrganizer.exe，正在打包...
  call 打包.bat
  if not exist "publish\塔克熊桌面整理工具.exe" (
    echo 打包失败。
    pause
    exit /b 1
  )
  copy /y "publish\塔克熊桌面整理工具.exe" "publish\TakexiongDesktopOrganizer.exe" >nul
)

if not exist ".git" (
  git init
  git remote add origin https://github.com/takexiong/TKX-Desktop-Organization.git
)

git add -A
git status
git commit -m "release: v1.0.6 开机自启与桌面图标隐藏等"
git branch -M main
git push -u origin main

echo.
echo 正在创建 GitHub Release v1.0.6 ...
gh release delete v1.0.6 --yes 2>nul
gh release create v1.0.6 "publish\TakexiongDesktopOrganizer.exe" ^
  --title "v1.0.6 塔克熊桌面整理工具" ^
  --notes "## 更新内容%0A- 新增开机自启开关%0A- 桌面图标拖入分区后隐藏，移出/删分区可还原%0A- 托盘唤出、单实例、窗口锁定与退出关窗修复%0A%0A下载后直接运行 TakexiongDesktopOrganizer.exe。"

if errorlevel 1 (
  echo.
  echo Release 创建失败。请检查网络与 GitHub 权限。
  pause
  exit /b 1
)

echo.
echo 完成：
echo   https://github.com/takexiong/TKX-Desktop-Organization
echo   https://github.com/takexiong/TKX-Desktop-Organization/releases/tag/v1.0.6
pause
