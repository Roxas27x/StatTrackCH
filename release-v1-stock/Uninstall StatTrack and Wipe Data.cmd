@echo off
setlocal
title StatTrack Full Cleanup
cd /d "%~dp0"

echo.
echo StatTrack Full Cleanup
echo ----------------------
echo This removes the tracker files, restores backed up game files when available,
echo and deletes %%LOCALAPPDATA%%\StatTrack.
echo.
set /p CONFIRM=Type WIPE to continue: 
if /i not "%CONFIRM%"=="WIPE" (
    echo.
    echo Cancelled.
    echo.
    pause
    exit /b 0
)

call "%~dp0Uninstall StatTrack.cmd" --wipe-data
exit /b %ERRORLEVEL%
