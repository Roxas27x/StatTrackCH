@echo off
setlocal EnableExtensions
title StatTrack Installer
set "SCRIPT_PATH=%~f0"
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

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
     set "RELEASE_ROOT=%SCRIPT_DIR%"
 )
set "DEFAULT_GAME_DIR=C:\Program Files\Clone Hero"
set "STOCK_DLL=%RELEASE_ROOT%StatTrack.dll"
set "PATCHER_EXE=%RELEASE_ROOT%V1StockAssemblyPatcher.exe"
set "DESKTOP_OVERLAY_EXE=%RELEASE_ROOT%StatTrackOverlay.exe"
set "RUNTIME_CHECKER_EXE=%RELEASE_ROOT%V1RuntimeCompatibilityChecker.exe"
set "CLEAN_ASSEMBLY_PATH=%RELEASE_ROOT%clean\Assembly-CSharp.dll"
set "PATCHED_SHARED_ASSETS_DIR=%RELEASE_ROOT%patched"
set "PATCHED_SHARED_ASSETS_MANIFEST=%PATCHED_SHARED_ASSETS_DIR%\sharedassets1-manifest.txt"
for %%I in ("%DEFAULT_GAME_DIR%") do set "DEFAULT_GAME_DIR=%%~fI"

echo.
echo StatTrack Installer
echo -------------------
echo Choose the Clone Hero folder that contains "Clone Hero.exe".
echo.

for %%F in ("%STOCK_DLL%" "%PATCHER_EXE%" "%DESKTOP_OVERLAY_EXE%" "%RUNTIME_CHECKER_EXE%" "%CLEAN_ASSEMBLY_PATH%" "%PATCHED_SHARED_ASSETS_MANIFEST%") do (
    if not exist "%%~fF" (
        echo Missing release file: %%~fF
        goto fail
    )
)
if not exist "%PATCHED_SHARED_ASSETS_DIR%\" (
    echo Missing release folder: %PATCHED_SHARED_ASSETS_DIR%
    goto fail
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

call :prepareBaselineFile "%ASSEMBLY_PATH%" "%BACKUP_ASSEMBLY_PATH%" "Assembly-CSharp.dll" || goto fail
call :prepareBaselineFile "%SHARED_ASSETS_PATH%" "%BACKUP_SHARED_ASSETS_PATH%" "sharedassets1.assets" || goto fail

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

call :installPatchedSharedAssets || goto fail

echo.
echo StatTrack installed successfully.
echo Game folder: %GAME_DIR%
echo Existing Assembly-CSharp.dll and sharedassets1.assets baselines were backed up before install.
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

:prepareBaselineFile
set "MOVE_SOURCE=%~1"
set "MOVE_BACKUP=%~2"
set "MOVE_LABEL=%~3"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$source = $env:MOVE_SOURCE; $backup = $env:MOVE_BACKUP; $label = $env:MOVE_LABEL; if(Test-Path -LiteralPath $backup){ Copy-Item -LiteralPath $backup -Destination $source -Force; Write-Host ('Restored ' + $label + ' baseline from: ' + $backup); exit 0 }; if(-not (Test-Path -LiteralPath $source)){ Write-Error ('Missing ' + $label + ': ' + $source); exit 1 }; Move-Item -LiteralPath $source -Destination $backup; Copy-Item -LiteralPath $backup -Destination $source -Force; Write-Host ('Renamed existing ' + $label + ' to: ' + $backup); Write-Host ('Restored ' + $label + ' baseline for patching.')"
exit /b %ERRORLEVEL%

:installPatchedSharedAssets
set "LOCAL_SHARED_ASSETS_HASH="
set "LOCAL_SHARED_ASSETS_VERSION="
set "UNITY_PLAYER_VERSION="
set "PATCHED_SHARED_ASSETS_PATH="
set "PATCHED_SHARED_ASSETS_MATCH="
for /f "usebackq tokens=1,2,3 delims=|" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$path = $env:SHARED_ASSETS_PATH; $bytes = [System.IO.File]::ReadAllBytes($path); $count = [Math]::Min($bytes.Length, 4096); $text = [System.Text.Encoding]::ASCII.GetString($bytes, 0, $count); $assetVersion = 'unknown'; if($text -match '20\d{2}\.\d+\.\d+f\d+'){ $assetVersion = $Matches[0] }; $engineVersion = 'unknown'; $unityPlayer = Join-Path $env:GAME_DIR 'UnityPlayer.dll'; if(Test-Path -LiteralPath $unityPlayer){ $productVersion = (Get-Item -LiteralPath $unityPlayer).VersionInfo.ProductVersion; if($productVersion -match '20\d{2}\.\d+\.\d+f\d+'){ $engineVersion = $Matches[0] } }; $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant(); [Console]::WriteLine($hash + '|' + $assetVersion + '|' + $engineVersion)"`) do (
    set "LOCAL_SHARED_ASSETS_HASH=%%I"
    set "LOCAL_SHARED_ASSETS_VERSION=%%J"
    set "UNITY_PLAYER_VERSION=%%K"
)
if not defined LOCAL_SHARED_ASSETS_HASH (
    echo Failed to inspect sharedassets1.assets:
    echo %SHARED_ASSETS_PATH%
    exit /b 1
)

echo.
echo Detected sharedassets1.assets baseline:
echo   Asset serialized version: %LOCAL_SHARED_ASSETS_VERSION%
echo   UnityPlayer version: %UNITY_PLAYER_VERSION%
echo   SHA256: %LOCAL_SHARED_ASSETS_HASH%

set "PREFER_UNITY_PLAYER_VERSION="
if /i not "%UNITY_PLAYER_VERSION%"=="unknown" (
    if /i not "%LOCAL_SHARED_ASSETS_VERSION%"=="unknown" (
        if /i not "%UNITY_PLAYER_VERSION%"=="%LOCAL_SHARED_ASSETS_VERSION%" set "PREFER_UNITY_PLAYER_VERSION=1"
    )
)

set "PATCHED_SHARED_ASSETS_HASH_PATH=%PATCHED_SHARED_ASSETS_DIR%\sharedassets1.%LOCAL_SHARED_ASSETS_HASH%.assets"
if defined PREFER_UNITY_PLAYER_VERSION (
    echo   Asset version mismatch detected; selecting the patch for the UnityPlayer version.
    call :findVersionMatchedSharedAssets
) else (
    if exist "%PATCHED_SHARED_ASSETS_HASH_PATH%" (
        set "PATCHED_SHARED_ASSETS_PATH=%PATCHED_SHARED_ASSETS_HASH_PATH%"
        set "PATCHED_SHARED_ASSETS_MATCH=exact baseline hash"
    ) else (
        call :findVersionMatchedSharedAssets
    )
)

if not defined PATCHED_SHARED_ASSETS_PATH (
    echo.
    echo Warning: this Clone Hero build uses an unsupported sharedassets1.assets baseline.
    echo Keeping the user's current sharedassets1.assets to avoid black menus or missing textures.
    echo Animated menu shader support will be skipped for this install.
    echo.
    exit /b 0
)
copy /y "%PATCHED_SHARED_ASSETS_PATH%" "%SHARED_ASSETS_PATH%" >nul
if errorlevel 1 (
    echo Failed to install patched sharedassets1.assets:
    echo %SHARED_ASSETS_PATH%
    exit /b 1
)
echo Installed StatTrack animated menu asset patch using %PATCHED_SHARED_ASSETS_MATCH%.
exit /b %ERRORLEVEL%

:findVersionMatchedSharedAssets
set "MATCH_FILE="
set "MATCH_REASON="
for /f "usebackq tokens=1,2 delims=|" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$manifest = $env:PATCHED_SHARED_ASSETS_MANIFEST; $preferred = @(); if($env:UNITY_PLAYER_VERSION -and $env:UNITY_PLAYER_VERSION -ne 'unknown'){ $preferred += @($env:UNITY_PLAYER_VERSION, 'UnityPlayer version') }; if($env:LOCAL_SHARED_ASSETS_VERSION -and $env:LOCAL_SHARED_ASSETS_VERSION -ne 'unknown'){ $preferred += @($env:LOCAL_SHARED_ASSETS_VERSION, 'asset serialized version') }; $entries = @(Get-Content -LiteralPath $manifest | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $parts = $_ -split '\|'; if($parts.Count -ge 3){ [pscustomobject]@{ Hash = $parts[0]; Version = $parts[1]; File = $parts[2] } } }); for($i = 0; $i -lt $preferred.Count; $i += 2){ $version = $preferred[$i]; $reason = $preferred[$i + 1]; $matches = @($entries | Where-Object { $_.Version -eq $version }); if($matches.Count -eq 1){ [Console]::WriteLine($matches[0].File + '|' + $reason); exit 0 } }; exit 1"`) do (
    set "MATCH_FILE=%%I"
    set "MATCH_REASON=%%J"
)
if not defined MATCH_FILE exit /b 1
set "PATCHED_SHARED_ASSETS_PATH=%PATCHED_SHARED_ASSETS_DIR%\%MATCH_FILE%"
if not exist "%PATCHED_SHARED_ASSETS_PATH%" exit /b 1
set "PATCHED_SHARED_ASSETS_MATCH=%MATCH_REASON%"
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
 set "ELEVATE_SCRIPT=%SCRIPT_PATH%"
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
