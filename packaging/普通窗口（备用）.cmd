@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0app\XYDesktopPlayer.exe" (
  echo Application files are missing.
  echo Please extract the complete package again.
  pause
  exit /b 2
)
if not exist "%~dp0content\player\index.html" (
  echo Wallpaper content is missing.
  echo Please extract the complete package again.
  pause
  exit /b 3
)
start "" /d "%~dp0app" "%~dp0app\XYDesktopPlayer.exe" --mode window --content "%~dp0content"
exit /b 0
