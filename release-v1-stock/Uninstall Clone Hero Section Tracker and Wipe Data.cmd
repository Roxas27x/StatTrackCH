@echo off
setlocal
title Clone Hero Section Tracker Full Cleanup
cd /d "%~dp0"

echo.
echo Clone Hero Section Tracker Full Cleanup
echo --------------------------------------
echo This removes the tracker files, restores the backed up Assembly-CSharp.dll,
echo and deletes %%LOCALAPPDATA%%\CloneHeroSectionTracker.
echo.
set /p CONFIRM=Type WIPE to continue: 
if /i not "%CONFIRM%"=="WIPE" (
    echo.
    echo Cancelled.
    echo.
    pause
    exit /b 0
)

call "%~dp0Uninstall Clone Hero Section Tracker.cmd" --wipe-data
exit /b %ERRORLEVEL%
