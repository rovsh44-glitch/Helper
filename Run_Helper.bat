@echo off
title HELPER Starter
setlocal EnableDelayedExpansion

for %%I in ("%~dp0.") do set "HELPER_ROOT=%%~fI"
set "ENV_FILE=%HELPER_ROOT%\.env.local"
set "DEFAULT_API_PORT=5000"
set "DEFAULT_UI_PORT=5173"
set "START_MODE=%~1"
if not defined START_MODE set "START_MODE=fast"

echo ==========================================
echo   STARTING HELPER ECOSYSTEM
echo ==========================================

call :LOAD_ENV
if %ERRORLEVEL% NEQ 0 (
    pause
    exit /b 1
)

echo [PRECHECK] Stopping stale Helper.Api processes...
taskkill /F /IM Helper.Api.exe /T >nul 2>&1
powershell -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'Helper\\.Api' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }" >nul 2>&1
powershell -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -match 'vite' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }" >nul 2>&1

set "HELPER_ROOT=%HELPER_ROOT%"
if not defined HELPER_DATA_ROOT for %%I in ("%HELPER_ROOT%\..") do set "HELPER_DATA_ROOT=%%~fI\HELPER_DATA"
if not exist "%HELPER_DATA_ROOT%" mkdir "%HELPER_DATA_ROOT%" >nul 2>&1
if not defined HELPER_PROJECTS_ROOT set "HELPER_PROJECTS_ROOT=%HELPER_DATA_ROOT%\PROJECTS"
if not defined HELPER_LIBRARY_ROOT set "HELPER_LIBRARY_ROOT=%HELPER_DATA_ROOT%\library"
if not defined HELPER_LOGS_ROOT set "HELPER_LOGS_ROOT=%HELPER_DATA_ROOT%\LOG"
if not defined HELPER_TEMPLATES_ROOT set "HELPER_TEMPLATES_ROOT=%HELPER_LIBRARY_ROOT%\forge_templates"
set "API_PORT_FILE=%HELPER_LOGS_ROOT%\API_PORT.txt"
if exist "%API_PORT_FILE%" del /q "%API_PORT_FILE%" >nul 2>&1
call :CONFIGURE_START_MODE
echo [MODE] Startup mode: %START_MODE%
echo [DATA] Data root: %HELPER_DATA_ROOT%

echo [1/2] Starting .NET Backend API...
start "Helper_Backend" /D "%HELPER_ROOT%\src\Helper.Api" dotnet run

call :RESOLVE_API_PORT
set "VITE_HELPER_API_PORT=!API_PORT!"
set "VITE_HELPER_API_BASE=http://localhost:!API_PORT!"
set "UI_PORT=%DEFAULT_UI_PORT%"
call :WAIT_API_READINESS

echo [2/2] Starting React Frontend (Vite)...
start "Helper_Frontend" /D "%HELPER_ROOT%" cmd /c "npm run dev -- --host 127.0.0.1 --port !UI_PORT! --strictPort"
call :WAIT_UI_READY
call :RUN_RUNTIME_SMOKE
if %ERRORLEVEL% NEQ 0 exit /b 1

echo ==========================================
echo   SYSTEMS LAUNCHED
echo   Backend: http://localhost:!API_PORT!
echo   Frontend: http://127.0.0.1:!UI_PORT!
echo ==========================================
echo Keep this window open or close it (processes run in separate windows).
pause
exit /b 0

:LOAD_ENV
if not exist "%ENV_FILE%" (
    echo [ERROR] %ENV_FILE% not found.
    echo Create .env.local and add HELPER_API_KEY.
    exit /b 1
)

set "HELPER_API_KEY="
set "HELPER_SESSION_SIGNING_KEY="
set "HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP="
set "ASPNETCORE_ENVIRONMENT="
set "DOTNET_ENVIRONMENT="
set "VITE_HELPER_API_PROTOCOL="
set "VITE_HELPER_API_HOST="
set "VITE_HELPER_API_PORT="
set "VITE_HELPER_API_BASE="
set "VITE_API_BASE_LEGACY="

for /f "usebackq tokens=1,* delims==" %%A in ("%ENV_FILE%") do (
    set "K=%%A"
    set "V=%%B"
    if not "!K!"=="" if not "!K:~0,1!"=="#" (
        if /I "!K!"=="VITE_API_BASE" (
            set "VITE_API_BASE_LEGACY=!V!"
        ) else if /I "!K:~0,7!"=="HELPER_" (
            set "!K!=!V!"
        ) else if /I "!K:~0,5!"=="VITE_" (
            set "!K!=!V!"
        ) else if /I "!K:~0,11!"=="ASPNETCORE_" (
            set "!K!=!V!"
        ) else if /I "!K:~0,7!"=="DOTNET_" (
            set "!K!=!V!"
        )
    )
)

if not defined HELPER_API_KEY (
    echo [ERROR] HELPER_API_KEY is missing in %ENV_FILE%.
    exit /b 1
)

if not defined VITE_HELPER_API_PROTOCOL set "VITE_HELPER_API_PROTOCOL=http"
if not defined VITE_HELPER_API_HOST set "VITE_HELPER_API_HOST=localhost"
if not defined VITE_HELPER_API_PORT set "VITE_HELPER_API_PORT=%DEFAULT_API_PORT%"
if not defined VITE_HELPER_API_BASE if defined VITE_API_BASE_LEGACY (
    set "VITE_HELPER_API_BASE=!VITE_API_BASE_LEGACY!"
    if /I "!VITE_HELPER_API_BASE:~-4!"=="/api" set "VITE_HELPER_API_BASE=!VITE_HELPER_API_BASE:~0,-4!"
)
if not defined VITE_HELPER_API_BASE set "VITE_HELPER_API_BASE=!VITE_HELPER_API_PROTOCOL!://!VITE_HELPER_API_HOST!:!VITE_HELPER_API_PORT!"

echo [ENV] HELPER_API_KEY loaded.
if defined HELPER_SESSION_SIGNING_KEY echo [ENV] HELPER_SESSION_SIGNING_KEY loaded.
if defined HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP echo [ENV] HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP=!HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP!
if defined ASPNETCORE_ENVIRONMENT echo [ENV] ASPNETCORE_ENVIRONMENT=!ASPNETCORE_ENVIRONMENT!
echo [ENV] API target from env: !VITE_HELPER_API_BASE!
exit /b 0

:RESOLVE_API_PORT
set "API_PORT=!VITE_HELPER_API_PORT!"
if not defined API_PORT set "API_PORT=%DEFAULT_API_PORT%"
set /a WAIT_ITER=0

:WAIT_API_PORT
if exist "%API_PORT_FILE%" (
    set /p FILE_PORT=<"%API_PORT_FILE%"
    if defined FILE_PORT set "API_PORT=!FILE_PORT!"
    exit /b 0
)

if !WAIT_ITER! GEQ 12 exit /b 0
set /a WAIT_ITER+=1
timeout /t 1 >nul
goto WAIT_API_PORT

:WAIT_API_READINESS
set /a WAIT_READY_ITER=0

:CHECK_API_READY
for /f "delims=" %%A in ('powershell -Command "$ProgressPreference='SilentlyContinue'; try { $r = Invoke-RestMethod -Method Get -Uri ('http://localhost:' + $env:API_PORT + '/api/readiness') -TimeoutSec 3; if ($r.readyForChat) { 'ready' } else { 'wait:' + $r.phase } } catch { 'wait:unreachable' }"') do set "READY_STATE=%%A"
if /I "!READY_STATE!"=="ready" exit /b 0
if !WAIT_READY_ITER! GEQ 60 (
    echo [WARN] Backend readiness wait timed out. Last state: !READY_STATE!
    exit /b 0
)
set /a WAIT_READY_ITER+=1
echo [WAIT] Backend readiness !WAIT_READY_ITER!/60: !READY_STATE!
timeout /t 1 >nul
goto CHECK_API_READY

:WAIT_UI_READY
set /a WAIT_UI_ITER=0

:CHECK_UI_READY
for /f "delims=" %%A in ('powershell -Command "$ProgressPreference='SilentlyContinue'; try { $r = Invoke-WebRequest -UseBasicParsing -Uri ('http://127.0.0.1:' + $env:UI_PORT) -TimeoutSec 3; if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { 'ready' } else { 'wait:' + $r.StatusCode } } catch { 'wait:unreachable' }"') do set "UI_STATE=%%A"
if /I "!UI_STATE!"=="ready" exit /b 0
if !WAIT_UI_ITER! GEQ 45 (
    echo [WARN] UI readiness wait timed out. Last state: !UI_STATE!
    exit /b 0
)
set /a WAIT_UI_ITER+=1
echo [WAIT] UI readiness !WAIT_UI_ITER!/45: !UI_STATE!
timeout /t 1 >nul
goto CHECK_UI_READY

:RUN_RUNTIME_SMOKE
echo [CHECK] Running deterministic runtime smoke...
powershell -ExecutionPolicy Bypass -File "%HELPER_ROOT%\scripts\run_runtime_smoke.ps1" -ApiBaseUrl "http://localhost:!API_PORT!" -UiUrl "http://127.0.0.1:!UI_PORT!"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Deterministic runtime smoke failed.
    exit /b 1
)
echo [CHECK] Deterministic runtime smoke passed.
exit /b 0

:CONFIGURE_START_MODE
if /I "%START_MODE%"=="warm" (
    set "HELPER_MODEL_WARMUP_MODE=full"
    set "HELPER_MODEL_PREFLIGHT_ENABLED=true"
    exit /b 0
)
set "HELPER_MODEL_WARMUP_MODE=minimal"
set "HELPER_MODEL_PREFLIGHT_ENABLED=false"
exit /b 0
