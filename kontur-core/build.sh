#!/usr/bin/env bash
# Сборка и самопроверка ядра. Godot не требуется.
set -e
cd "$(dirname "$0")"
dotnet build Kontur.Core.sln -c Release
dotnet run --project src/Kontur.Harness -c Release -- --selftest
