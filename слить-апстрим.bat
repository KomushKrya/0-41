@echo off
rem ============================================================
rem  Merge the upstream repository into the current branch,
rem  resolving conflicts by path: core stays ours, prose theirs.
rem
rem  ASCII-only on purpose: cmd.exe parses a .bat using the console
rem  code page, so Cyrillic inside batch commands breaks. All Russian
rem  text lives in the PowerShell script.
rem ============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\merge-upstream.ps1"

echo.
pause
