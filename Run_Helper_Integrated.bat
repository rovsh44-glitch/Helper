@echo off
TITLE HELPER Launcher
SETLOCAL EnableDelayedExpansion

for %%I in ("%~dp0.") do SET "HELPER_ROOT=%%~fI"
SET "SOLUTION_PATH=%HELPER_ROOT%\Helper.sln"
SET "RUN_HELPER_SCRIPT=%HELPER_ROOT%\Run_Helper.bat"
SET "STOP_HELPER_SCRIPT=%HELPER_ROOT%\scripts\stop_helper_processes.ps1"
SET "DEFAULT_UI_URL=http://127.0.0.1:5173"

echo ============================================================
echo      HELPER SYSTEM LAUNCHER (v2.7)
echo ============================================================
echo.

:MENU
echo 1. Full Rebuild and Start (Warm)
echo 2. Start (Fast)
echo 3. Start (Warm)
echo 4. Open UI In Browser
echo 5. Stop all Helper Processes
echo 6. Exit
echo.
SET /P choice="Select option (1-6): "

IF "%choice%"=="1" GOTO REBUILD_AND_START
IF "%choice%"=="2" GOTO START_FAST
IF "%choice%"=="3" GOTO START_WARM
IF "%choice%"=="4" GOTO OPEN_UI
IF "%choice%"=="5" GOTO STOP_ALL
IF "%choice%"=="6" EXIT /B 0
GOTO MENU

:REBUILD_AND_START
echo.
echo [PREBUILD] Stopping running Helper processes that can lock build outputs...
powershell -NoProfile -ExecutionPolicy Bypass -File "%STOP_HELPER_SCRIPT%" >nul 2>&1
IF %ERRORLEVEL% NEQ 0 echo [WARN] Cleanup did not fully complete. Build may still encounter locked outputs.
echo.
echo [1/2] Rebuilding HELPER backend solution...
dotnet build "%SOLUTION_PATH%" -c Debug
IF %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Backend build failed. Aborting.
    pause
    GOTO MENU
)
echo [2/2] Rebuilding HELPER frontend bundle...
powershell -NoProfile -ExecutionPolicy Bypass -Command "npm run build"
IF %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Frontend build failed. Aborting.
    pause
    GOTO MENU
)
echo [BUILD] Rebuild complete.
call "%RUN_HELPER_SCRIPT%" warm
GOTO MENU

:START_FAST
call "%RUN_HELPER_SCRIPT%" fast
GOTO MENU

:START_WARM
call "%RUN_HELPER_SCRIPT%" warm
GOTO MENU

:OPEN_UI
echo.
echo [OPEN] Opening HELPER UI...
start "" "%DEFAULT_UI_URL%"
echo [OPEN] Requested browser launch for %DEFAULT_UI_URL%.
echo.
pause
GOTO MENU

:STOP_ALL
echo.
echo [CLEANUP] Stopping all Helper processes...
powershell -NoProfile -ExecutionPolicy Bypass -File "%STOP_HELPER_SCRIPT%" >nul 2>&1
IF %ERRORLEVEL% NEQ 0 (
    echo [WARN] Some Helper processes may still be running.
) ELSE (
    echo [CLEANUP] Helper process stop completed.
)
echo.
pause
GOTO MENU
