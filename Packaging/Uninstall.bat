@echo off
rem Deletes the data AF-DecalFinder saved (settings, work data, pending imports). See Uninstall.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
pause
