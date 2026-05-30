@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%setup-online-license-config.ps1"

if not exist "%PS1%" (
  echo [CamboBIM] File not found: "%PS1%"
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
  echo [CamboBIM] License config setup completed.
) else (
  echo [CamboBIM] License config setup failed. Exit code: %EXITCODE%
)

pause
exit /b %EXITCODE%

