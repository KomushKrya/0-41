@echo off
rem ============================================================
rem  Commit and push the working folder to GitHub.
rem
rem  ASCII-only on purpose: cmd.exe parses a .bat using the console
rem  code page, so Cyrillic inside batch commands breaks before chcp
rem  can help. All Russian text lives in the PowerShell script.
rem ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\push.ps1"

echo.
pause
