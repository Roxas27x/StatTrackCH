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
 if defined ELEVATE_RELEASE_ROOT (
     set "RELEASE_ROOT=%ELEVATE_RELEASE_ROOT%"
 ) else (
     set "RELEASE_ROOT=%~dp0"
 )
set "DEFAULT_GAME_DIR=C:\Program Files\Clone Hero"
set "STOCK_DLL=%RELEASE_ROOT%StatTrack.dll"
set "PATCHER_EXE=%RELEASE_ROOT%V1StockAssemblyPatcher.exe"
set "DESKTOP_OVERLAY_EXE=%RELEASE_ROOT%StatTrackOverlay.exe"
set "RUNTIME_CHECKER_EXE=%RELEASE_ROOT%V1RuntimeCompatibilityChecker.exe"
set "CLEAN_ASSEMBLY_PATH=%RELEASE_ROOT%clean\Assembly-CSharp.dll"
set "CLEAN_SHARED_ASSETS_PATH=%RELEASE_ROOT%clean\sharedassets1.assets"
set "PATCHED_SHARED_ASSETS_PATH=%RELEASE_ROOT%patched\sharedassets1.assets"
for %%I in ("%DEFAULT_GAME_DIR%") do set "DEFAULT_GAME_DIR=%%~fI"

echo.
echo StatTrack Installer
echo -------------------
echo Choose the Clone Hero folder that contains "Clone Hero.exe".
echo.

for %%F in ("%STOCK_DLL%" "%PATCHER_EXE%" "%DESKTOP_OVERLAY_EXE%" "%RUNTIME_CHECKER_EXE%" "%CLEAN_ASSEMBLY_PATH%" "%CLEAN_SHARED_ASSETS_PATH%" "%PATCHED_SHARED_ASSETS_PATH%") do (
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
set "DATA_DIR=%GAME_DIR%\Clone Hero_Data"
set "ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.dll"
set "BACKUP_ASSEMBLY_PATH=%MANAGED_DIR%\Assembly-CSharp.sectiontracker-backup.dll"
set "SHARED_ASSETS_PATH=%DATA_DIR%\sharedassets1.assets"
set "BACKUP_SHARED_ASSETS_PATH=%DATA_DIR%\sharedassets1.assets.stattrack-backup"
set "TARGET_HOOK_DLL=%MANAGED_DIR%\StatTrack.dll"
set "TARGET_OVERLAY_EXE=%MANAGED_DIR%\StatTrackOverlay.exe"

call :moveExistingFileAside "%ASSEMBLY_PATH%" "%BACKUP_ASSEMBLY_PATH%" "Assembly-CSharp.dll" || goto fail
copy /y "%CLEAN_ASSEMBLY_PATH%" "%ASSEMBLY_PATH%" >nul
if errorlevel 1 (
    echo Failed to install clean Assembly-CSharp.dll:
    echo %ASSEMBLY_PATH%
    goto fail
)

call :moveExistingFileAside "%SHARED_ASSETS_PATH%" "%BACKUP_SHARED_ASSETS_PATH%" "sharedassets1.assets" || goto fail
copy /y "%CLEAN_SHARED_ASSETS_PATH%" "%SHARED_ASSETS_PATH%" >nul
if errorlevel 1 (
    echo Failed to install clean sharedassets1.assets:
    echo %SHARED_ASSETS_PATH%
    goto fail
)

"%RUNTIME_CHECKER_EXE%" "%STOCK_DLL%" "%MANAGED_DIR%"
if errorlevel 1 (
    echo Runtime compatibility check failed for:
    echo %GAME_DIR%
    goto fail
)

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

copy /y "%PATCHED_SHARED_ASSETS_PATH%" "%SHARED_ASSETS_PATH%" >nul
if errorlevel 1 (
    echo Failed to install patched sharedassets1.assets:
    echo %SHARED_ASSETS_PATH%
    goto fail
)

echo.
echo StatTrack installed successfully.
echo Game folder: %GAME_DIR%
echo Existing Assembly-CSharp.dll and sharedassets1.assets were renamed before install.
echo.
echo Launch "Clone Hero.exe" from that folder and use Home / F8 to open the overlay.
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
if not exist "%GAME_DIR%\Clone Hero_Data\sharedassets1.assets" (
    echo The selected folder does not contain "Clone Hero_Data\sharedassets1.assets".
    exit /b 1
)
exit /b 0

:moveExistingFileAside
set "MOVE_SOURCE=%~1"
set "MOVE_BACKUP=%~2"
set "MOVE_LABEL=%~3"
if not exist "%MOVE_SOURCE%" exit /b 0
powershell -NoProfile -ExecutionPolicy Bypass -Command "$source = $env:MOVE_SOURCE; $preferred = $env:MOVE_BACKUP; $label = $env:MOVE_LABEL; if(-not (Test-Path -LiteralPath $source)){ exit 0 }; $backup = $preferred; if(Test-Path -LiteralPath $backup){ $dir = Split-Path -Parent $source; $leaf = Split-Path -Leaf $source; $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'; $backup = Join-Path $dir ($leaf + '.pre-stattrack-' + $stamp + '.bak'); $counter = 1; while(Test-Path -LiteralPath $backup){ $backup = Join-Path $dir ($leaf + '.pre-stattrack-' + $stamp + '-' + $counter + '.bak'); $counter++ } }; Move-Item -LiteralPath $source -Destination $backup; Write-Host ('Renamed existing ' + $label + ' to: ' + $backup)"
exit /b %ERRORLEVEL%

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
 set "ELEVATE_RELEASE_ROOT=%RELEASE_ROOT%"
 powershell -NoProfile -ExecutionPolicy Bypass -Command "$scriptPath = $env:ELEVATE_SCRIPT; $releaseRoot = $env:ELEVATE_RELEASE_ROOT; $commandLine = 'call ' + [char]34 + $scriptPath + [char]34; Start-Process -FilePath $env:ComSpec -WorkingDirectory $releaseRoot -ArgumentList @('/k', $commandLine) -Verb RunAs"
 set "ELEVATE_SCRIPT="
 set "ELEVATE_GAME_DIR="
 set "ELEVATE_RELEASE_ROOT="
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
