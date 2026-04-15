# Clone Hero Section Tracker

This project turns a stock Clone Hero v1 install into an OBS-friendly tracker build.

It adds:
- an in-game overlay editor for choosing what to track
- an optional desktop overlay for pinned widgets
- opt-in OBS text exports
- persistent song memory for things like completed runs, section progress, ghosts, and layout settings

The tracker keeps its live data in `%LOCALAPPDATA%\CloneHeroSectionTracker`, and only writes OBS files for the exports the player actually enables.

## How It Works

The stock-v1 release is made of three moving parts:

- `CloneHeroV1StockTracker.dll`
  The tracker itself. This is where the gameplay reads, overlay state, OBS exports, and saved memory live.
- `V1StockAssemblyPatcher.exe`
  Patches `Assembly-CSharp.dll` so the tracker loads automatically in a normal Clone Hero v1 install.
- `CloneHeroDesktopOverlay.exe`
  The companion overlay process used when the desktop overlay takes over from the in-game editor.

## Local Dev Install

This repo is set up around a writable stock-v1 Clone Hero copy for development.

1. Close Clone Hero.
2. Run `build-v1-stock.ps1`.
3. Run `install-v1-stock.ps1`.
4. Launch `C:\Users\Roxas\Documents\GDBOT\clone-hero-v1-writable\Clone Hero.exe`.

The build outputs the stock helper DLL, patcher, desktop overlay, and runtime checker in `dist-v1-stock`.

## Player Release Pack

After running `build-v1-stock.ps1`, a player-ready release folder is created in `dist-v1-stock\release`.

If you want a zip to hand out directly, run:

`package-v1-stock-release.ps1`

That generates:

`dist-v1-stock\CloneHeroSectionTracker-v1.0.0.4080-obs-friendly.zip`

The release pack includes:
- `install.ps1`
- `uninstall.ps1`
- `README.txt`
- `version.txt`
- `RELEASE_NOTES.txt`
- `CHANGELOG.txt`
- `CloneHeroV1StockTracker.dll`
- `V1StockAssemblyPatcher.exe`
- `CloneHeroDesktopOverlay.exe`
- `V1RuntimeCompatibilityChecker.exe`
- `Mono.Cecil.dll`
- `Mono.Cecil.Rocks.dll`

The player installer asks for a Clone Hero folder, backs up `Assembly-CSharp.dll`, copies the tracker files in, and patches the game in place.

## Data Files

The tracker writes to `%LOCALAPPDATA%\CloneHeroSectionTracker`:

- `state.json`
- `memory.json`
- `config.json`
- `desktop-style.json`
- `obs\...`

## Notes

- OBS exports are opt-in from the overlay editor.
- Completed runs are always stored in `memory.json`, but the OBS `runs` folder is only written when that export is enabled.
- The desktop overlay is intended for borderless or windowed play.
