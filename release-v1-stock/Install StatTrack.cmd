@echo off
setlocal EnableExtensions
title StatTrack Installer
cd /d "%~dp0"

set "GAME_DIR="
set "NO_PAUSE="
set "EXITCODE=0"

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
set "DEFAULT_GAME_DIR=C:\Program Files\Clone Hero"
set "STOCK_DLL=%RELEASE_ROOT%StatTrack.dll"
set "PATCHER_EXE=%RELEASE_ROOT%V1StockAssemblyPatcher.exe"
set "DESKTOP_OVERLAY_EXE=%RELEASE_ROOT%StatTrackOverlay.exe"
set "RUNTIME_CHECKER_EXE=%RELEASE_ROOT%V1RuntimeCompatibilityChecker.exe"
for %%I in ("%DEFAULT_GAME_DIR%") do set "DEFAULT_GAME_DIR=%%~fI"

echo.
echo StatTrack Installer
echo -------------------
echo Choose the Clone Hero folder that contains "Clone Hero.exe".
echo.

for %%F in ("%STOCK_DLL%" "%PATCHER_EXE%" "%DESKTOP_OVERLAY_EXE%" "%RUNTIME_CHECKER_EXE%") do (
    if not exist "%%~fF" (
        echo Missing release file: %%~fF
        goto fail
    )
)

call :resolveGameDir || goto fail
call :ensureAdminIfNeeded
if errorlevel 2 goto done
if errorlevel 1 goto fail

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
if not defined GAME_DIR (
    echo Default Clone Hero folder:
    echo   %DEFAULT_GAME_DIR%
    echo.
    echo Note: writing to the Program Files directory requires administrator permissions.
    echo.
    choice /c YN /n /m "Install to this folder? [Y/N]: "
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

call :normalizeGameDir
if errorlevel 1 (
    set "GAME_DIR="
    goto resolveGameDir
)
exit /b 0

:normalizeGameDir
for %%I in ("%GAME_DIR%") do set "GAME_DIR=%%~fI"
if not exist "%GAME_DIR%" (
    echo That folder does not exist.
    exit /b 1
)
if not exist "%GAME_DIR%\Clone Hero.exe" (
    echo The selected folder does not contain "Clone Hero.exe".
    exit /b 1
)
if not exist "%GAME_DIR%\Clone Hero_Data\Managed\Assembly-CSharp.dll" (
    echo The selected folder does not contain "Clone Hero_Data\Managed\Assembly-CSharp.dll".
    exit /b 1
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
echo Administrator permissions are required to install into:
echo %GAME_DIR%
echo.
echo Relaunching the installer as administrator...
set "ELEVATE_SCRIPT=%~f0"
set "ELEVATE_GAME_DIR=%GAME_DIR%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath $env:ELEVATE_SCRIPT -ArgumentList @($env:ELEVATE_GAME_DIR) -Verb RunAs"
set "ELEVATE_SCRIPT="
set "ELEVATE_GAME_DIR="
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

:fail
echo.
echo Install failed.
echo.
set "EXITCODE=1"
goto done

:done
if not defined NO_PAUSE pause
exit /b %EXITCODE%
