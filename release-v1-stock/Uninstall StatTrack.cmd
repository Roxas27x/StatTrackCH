@echo off
setlocal EnableExtensions
title StatTrack Uninstall
cd /d "%~dp0"

set "GAME_DIR="
set "DEFAULT_GAME_DIR=C:\Program Files\Clone Hero"
set "NO_PAUSE="
set "WIPE_DATA="
set "EXITCODE=0"

if defined ELEVATE_RELEASE_ROOT (
    set "RELEASE_ROOT=%ELEVATE_RELEASE_ROOT%"
) else (
    set "RELEASE_ROOT=%~dp0"
)
for %%I in ("%DEFAULT_GAME_DIR%") do set "DEFAULT_GAME_DIR=%%~fI"

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
call :ensureAdminIfNeeded
if errorlevel 2 goto done
if errorlevel 1 goto fail

set "MANAGED_DIR=%GAME_DIR%\Clone Hero_Data\Managed"
set "ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.dll"
set "BACKUP_ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.sectiontracker-backup.dll"
set "TARGET_HOOK_DLL=%MANAGED_DIR%\StatTrack.dll"
set "TARGET_OVERLAY_EXE=%MANAGED_DIR%\StatTrackOverlay.exe"

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

if defined WIPE_DATA (
    call :removeDataDir "%LOCALAPPDATA%\StatTrack" || goto fail
)

echo.
echo StatTrack uninstall complete.
echo Game folder: %GAME_DIR%
echo.
goto done

:resolveGameDir
if not defined GAME_DIR (
    echo Default Clone Hero folder:
    echo   %DEFAULT_GAME_DIR%
    echo.
    echo Note: writing to the Program Files directory requires administrator permissions.
    echo.
    choice /c YN /n /m "Uninstall from this folder? [Y/N]: "
    if errorlevel 2 (
        call :browseForGameDir
        if not defined GAME_DIR (
            echo Folder selection was cancelled.
            set /p "GAME_DIR=Clone Hero folder: "
            if not defined GAME_DIR goto resolveGameDir
        )
    ) else (
        set "GAME_DIR=%DEFAULT_GAME_DIR%"
    )
)

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

:browseForGameDir
set "BROWSE_RESULT="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -STA -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; $dialog.Description = 'Select the Clone Hero folder that contains Clone Hero.exe'; $dialog.SelectedPath = $env:DEFAULT_GAME_DIR; $dialog.ShowNewFolderButton = $false; if($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){ [Console]::WriteLine($dialog.SelectedPath) }"`) do set "BROWSE_RESULT=%%I"
if defined BROWSE_RESULT set "GAME_DIR=%BROWSE_RESULT%"
exit /b 0

:ensureAdminIfNeeded
call :isProgramFilesPath
if errorlevel 1 exit /b 0
call :isAdministrator
if not errorlevel 1 exit /b 0
echo.
echo Administrator permissions are required to uninstall from:
echo %GAME_DIR%
echo.
echo Relaunching the uninstaller as administrator...
set "ELEVATE_SCRIPT=%~f0"
set "ELEVATE_RELEASE_ROOT=%RELEASE_ROOT%"
set "ELEVATE_WIPE_DATA=%WIPE_DATA%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$scriptPath = $env:ELEVATE_SCRIPT; $releaseRoot = $env:ELEVATE_RELEASE_ROOT; $wipeData = $env:ELEVATE_WIPE_DATA; $commandLine = 'call ' + [char]34 + $scriptPath + [char]34; if(-not [string]::IsNullOrWhiteSpace($wipeData)){ $commandLine += ' --wipe-data' }; Start-Process -FilePath $env:ComSpec -WorkingDirectory $releaseRoot -ArgumentList @('/k', $commandLine) -Verb RunAs"
set "ELEVATE_SCRIPT="
set "ELEVATE_RELEASE_ROOT="
set "ELEVATE_WIPE_DATA="
if errorlevel 1 (
    echo Administrator elevation was cancelled or failed.
    exit /b 1
)
set "NO_PAUSE=1"
exit /b 2

:isProgramFilesPath
set "CHECK_PATH=%GAME_DIR%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = [System.IO.Path]::GetFullPath($env:CHECK_PATH); $roots = @($env:ProgramW6432, $env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) | Where-Object { $_ } | Select-Object -Unique; foreach($root in $roots){ $full = [System.IO.Path]::GetFullPath($root); if($target.StartsWith($full, [System.StringComparison]::OrdinalIgnoreCase)){ exit 0 } }; exit 1"
exit /b %ERRORLEVEL%

:isAdministrator
powershell -NoProfile -ExecutionPolicy Bypass -Command "$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){ exit 0 } exit 1"
exit /b %ERRORLEVEL%

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
