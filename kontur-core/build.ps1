# Сборка и самопроверка ядра. Godot не требуется.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet build Kontur.Core.sln -c Release
dotnet run --project src/Kontur.Harness -c Release -- --selftest
