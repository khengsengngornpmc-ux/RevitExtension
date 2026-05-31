@echo off
setlocal
cd /d "%~dp0.."

if not exist "Run-Git-Daily-Save-RevitExtension-PT.cmd" (
  echo.
  echo Run-Git-Daily-Save-RevitExtension-PT.cmd was not found.
  pause
  exit /b 1
)

echo.
echo Step 2: Saving RevitExtension PT work to GitHub...
call "Run-Git-Daily-Save-RevitExtension-PT.cmd"
set "exit_code=%ERRORLEVEL%"

if not "%exit_code%"=="0" (
  echo.
  echo Step 2 failed with code %exit_code%.
  pause
  exit /b %exit_code%
)

echo Step 2 complete.
pause
exit /b 0
