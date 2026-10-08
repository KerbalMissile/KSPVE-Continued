@echo off
rem Double-click: switch the Sol + Kcalbeloh + KSS2 cluster to the full layout (all KSS2 systems).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0switch-cluster.ps1" full
pause
