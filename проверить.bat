@echo off
rem ============================================================
rem  Build the Kontur core and run its self-tests. Godot not needed.
rem
rem  This wrapper is deliberately ASCII-only. cmd.exe parses a .bat
rem  using the console code page, so Cyrillic inside batch commands
rem  breaks before chcp can help. All Russian text lives in the
rem  PowerShell script, which reads as UTF-8 and does not break.
rem
rem  Full output goes to the log file next to this one.
rem ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\check-core.ps1"

echo.
pause
