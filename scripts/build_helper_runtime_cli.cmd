@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_helper_runtime_cli.ps1" %*
exit /b %ERRORLEVEL%
