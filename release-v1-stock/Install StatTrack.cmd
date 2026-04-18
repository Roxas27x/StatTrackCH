@echo off
setlocal EnableExtensions
title StatTrack Installer
cd /d "%~dp0"

set "GAME_DIR="
set "NO_PAUSE="

:parseArgs
if "%~1"=="" goto argsDone
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
set "RELEASE_ROOT=%~dp0"
set "STOCK_DLL=%RELEASE_ROOT%StatTrack.dll"
set "PATCHER_EXE=%RELEASE_ROOT%V1StockAssemblyPatcher.exe"
set "DESKTOP_OVERLAY_EXE=%RELEASE_ROOT%StatTrackOverlay.exe"
set "RUNTIME_CHECKER_EXE=%RELEASE_ROOT%V1RuntimeCompatibilityChecker.exe"

echo.
echo StatTrack Installer
echo -------------------
echo Enter the Clone Hero folder that contains "Clone Hero.exe".
echo.

for %%F in ("%STOCK_DLL%" "%PATCHER_EXE%" "%DESKTOP_OVERLAY_EXE%" "%RUNTIME_CHECKER_EXE%") do (
    if not exist "%%~fF" (
        echo Missing release file: %%~fF
        goto fail
    )
)

call :resolveGameDir || goto fail

set "MANAGED_DIR=%GAME_DIR%\Clone Hero_Data\Managed"
set "ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.dll"
set "BACKUP_ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.sectiontracker-backup.dll"
set "TARGET_HOOK_DLL=%MANAGED_DIR%\StatTrack.dll"
set "TARGET_OVERLAY_EXE=%MANAGED_DIR%\StatTrackOverlay.exe"
set "LEGACY_HOOK_DLL=%MANAGED_DIR%\CloneHeroV1StockTracker.dll"
set "LEGACY_OVERLAY_EXE=%MANAGED_DIR%\CloneHeroDesktopOverlay.exe"

if not exist "%BACKUP_ASSEMBLY_PATH%" (
    copy /y "%ASSEMBLY_PATH%" "%BACKUP_ASSEMBLY_PATH%" >nul
    if errorlevel 1 (
        echo Failed to create backup:
        echo %BACKUP_ASSEMBLY_PATH%
        goto fail
    )
)

"%RUNTIME_CHECKER_EXE%" "%STOCK_DLL%" "%MANAGED_DIR%"
if errorlevel 1 (
    echo Runtime compatibility check failed for:
    echo %GAME_DIR%
    goto fail
)

if /i not "%TARGET_HOOK_DLL%"=="%LEGACY_HOOK_DLL%" if exist "%LEGACY_HOOK_DLL%" del /f /q "%LEGACY_HOOK_DLL%" >nul 2>&1
if /i not "%TARGET_OVERLAY_EXE%"=="%LEGACY_OVERLAY_EXE%" if exist "%LEGACY_OVERLAY_EXE%" del /f /q "%LEGACY_OVERLAY_EXE%" >nul 2>&1

copy /y "%STOCK_DLL%" "%TARGET_HOOK_DLL%" >nul
if errorlevel 1 (
    echo Failed to copy tracker DLL into:
    echo %TARGET_HOOK_DLL%
    goto fail
)

copy /y "%DESKTOP_OVERLAY_EXE%" "%TARGET_OVERLAY_EXE%" >nul
if errorlevel 1 (
    echo Failed to copy desktop overlay into:
    echo %TARGET_OVERLAY_EXE%
    goto fail
)

"%PATCHER_EXE%" "%ASSEMBLY_PATH%" "%TARGET_HOOK_DLL%"
if errorlevel 1 (
    echo Failed to patch Assembly-CSharp.dll
    goto fail
)

echo.
echo StatTrack installed successfully.
echo Game folder: %GAME_DIR%
echo Backup created at: %BACKUP_ASSEMBLY_PATH%
echo.
echo Launch "Clone Hero.exe" from that folder and use Home / Ctrl+O / F8 to open the overlay.
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
if not exist "%GAME_DIR%\Clone Hero_Data\Managed\Assembly-CSharp.dll" (
    echo The selected folder does not contain "Clone Hero_Data\Managed\Assembly-CSharp.dll".
    set "GAME_DIR="
    goto resolveGameDir
)
exit /b 0

:fail
echo.
echo Install failed.
echo.
set "EXITCODE=1"
goto done

:done
if not defined NO_PAUSE pause
exit /b %EXITCODE%
