@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

set "PS_EXE="
where pwsh.exe >nul 2>nul && set "PS_EXE=pwsh.exe"
if not defined PS_EXE where powershell.exe >nul 2>nul && set "PS_EXE=powershell.exe"
if not defined PS_EXE (
  echo.
  echo Could not find PowerShell.
  exit /b 1
)

set "WORK_BRANCH=%REVITEXTENSION_WORK_BRANCH%"
if "%WORK_BRANCH%"=="" (
  for /f "delims=" %%i in ('git -C "%SCRIPT_DIR%." branch --show-current 2^>nul') do set "WORK_BRANCH=%%i"
)
if "%WORK_BRANCH%"=="" set "WORK_BRANCH=main"

echo Saving RevitExtension PT work on branch "%WORK_BRANCH%"...
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\git-daily-save-pt.ps1" -Branch "%WORK_BRANCH%" %*
set "exit_code=%ERRORLEVEL%"
if not "%exit_code%"=="0" pause
exit /b %exit_code%
