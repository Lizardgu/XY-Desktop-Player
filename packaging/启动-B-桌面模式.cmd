@echo off
cd /d "%~dp0"
if not exist "%~dp0content\reference-player\index.html" (
  echo [Nikki Desktop] Missing content\reference-player\index.html
  echo Read 运行说明.txt before launching.
  pause
  exit /b 2
)
start "Nikki Desktop" "%~dp0NikkiDesktop.App.exe" --mode wallpaper --content "%~dp0content\reference-player"
