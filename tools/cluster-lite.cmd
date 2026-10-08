@echo off
rem Double-click: switch the Sol + Kcalbeloh + KSS2 cluster to the lite layout (Kcalbeloh, Kerr, Sol, Nova Kirbani).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0switch-cluster.ps1" lite
pause
