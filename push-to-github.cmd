@echo off
setlocal

set /p COMMIT_MSG=Enter commit message: 
if "%COMMIT_MSG%"=="" set "COMMIT_MSG=Update project files"

echo.
echo Staging all local changes...
git add .
if errorlevel 1 goto :error

echo.
echo Files staged for commit:
git status --short
if errorlevel 1 goto :error

echo.
echo Creating commit...
git commit -m "%COMMIT_MSG%"
if errorlevel 1 goto :error

for /f "delims=" %%b in ('git branch --show-current') do set "BRANCH=%%b"
if "%BRANCH%"=="" set "BRANCH=main"

git push -u origin %BRANCH%
if errorlevel 1 goto :error

echo.
echo Push completed successfully.
pause
exit /b 0

:error
echo.
echo Something failed. Please check the message above.
pause
exit /b 1
