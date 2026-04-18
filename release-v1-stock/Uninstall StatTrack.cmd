@echo off
setlocal EnableExtensions
title StatTrack Uninstall
cd /d "%~dp0"

set "GAME_DIR="
set "NO_PAUSE="
set "WIPE_DATA="

:parseArgs
if "%~1"=="" goto argsDone
if /i "%~1"=="--wipe-data" (
    set "WIPE_DATA=1"
    shift
    goto parseArgs
)
if /i "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
    shift
    goto parseArgs
)
if not defined GAME_DIR (
    set "GAME_DIR=%~1"
    shift
    goto parseArgs
)
echo Unknown argument: %~1
goto fail

:argsDone

echo.
echo StatTrack Uninstall
echo -------------------
echo This removes the tracker files and restores the backed up Assembly-CSharp.dll.
echo.

call :resolveGameDir || goto fail

set "MANAGED_DIR=%GAME_DIR%\Clone Hero_Data\Managed"
set "ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.dll"
set "BACKUP_ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.sectiontracker-backup.dll"
set "TARGET_HOOK_DLL=%MANAGED_DIR%\StatTrack.dll"
set "TARGET_OVERLAY_EXE=%MANAGED_DIR%\StatTrackOverlay.exe"
set "LEGACY_HOOK_DLL=%MANAGED_DIR%\CloneHeroV1StockTracker.dll"
set "LEGACY_OVERLAY_EXE=%MANAGED_DIR%\CloneHeroDesktopOverlay.exe"

if exist "%BACKUP_ASSEMBLY_PATH%" (
    copy /y "%BACKUP_ASSEMBLY_PATH%" "%ASSEMBLY_PATH%" >nul
    if errorlevel 1 (
        echo Failed to restore the original Assembly-CSharp.dll backup.
        goto fail
    )
    del /f /q "%BACKUP_ASSEMBLY_PATH%" >nul 2>&1
    echo Restored original Assembly-CSharp.dll backup.
) else (
    echo No tracker backup assembly was found.
    echo The tracker files will still be removed.
)

if exist "%TARGET_HOOK_DLL%" del /f /q "%TARGET_HOOK_DLL%" >nul 2>&1
if exist "%TARGET_OVERLAY_EXE%" del /f /q "%TARGET_OVERLAY_EXE%" >nul 2>&1
if exist "%LEGACY_HOOK_DLL%" del /f /q "%LEGACY_HOOK_DLL%" >nul 2>&1
if exist "%LEGACY_OVERLAY_EXE%" del /f /q "%LEGACY_OVERLAY_EXE%" >nul 2>&1

if defined WIPE_DATA (
    call :removeDataDir "%LOCALAPPDATA%\StatTrack" || goto fail
    call :removeDataDir "%LOCALAPPDATA%\CloneHeroSectionTracker" || goto fail
)

echo.
echo StatTrack uninstall complete.
echo Game folder: %GAME_DIR%
echo.
goto done

:resolveGameDir
if defined GAME_DIR goto normalizeGameDir
set /p "GAME_DIR=Clone Hero folder: "
if not defined GAME_DIR goto resolveGameDir

:normalizeGameDir
for %%I in ("%GAME_DIR%") do set "GAME_DIR=%%~fI"
if not exist "%GAME_DIR%" (
    echo That folder does not exist.
    set "GAME_DIR="
    goto resolveGameDir
)
if not exist "%GAME_DIR%\Clone Hero.exe" (
    echo The selected folder does not contain "Clone Hero.exe".
    set "GAME_DIR="
    goto resolveGameDir
)
exit /b 0

:removeDataDir
set "TRACKER_DATA_DIR=%~1"
if not defined TRACKER_DATA_DIR exit /b 0
if exist "%TRACKER_DATA_DIR%" (
    rmdir /s /q "%TRACKER_DATA_DIR%"
    if errorlevel 1 (
        echo Failed to remove tracker data:
        echo %TRACKER_DATA_DIR%
        exit /b 1
    )
    echo Removed tracker data from %TRACKER_DATA_DIR%
)
exit /b 0

:fail
echo.
echo Uninstall failed.
echo.
set "EXITCODE=1"
goto done

:done
if not defined NO_PAUSE pause
exit /b %EXITCODE%
