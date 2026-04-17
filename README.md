# Clone Hero Section Tracker

Clone Hero Section Tracker turns a normal Clone Hero v1 install into an OBS-friendly tracking build.

It adds:
- an in-game overlay editor for choosing what to track
- optional desktop overlay widgets for pinned live stats
- opt-in OBS text exports
- saved tracker memory for completed runs, section progress, ghost counts, and layout settings

The tracker only writes OBS files for the exports the player actually enables.

## How It Works

The release uses three main files:

- `CloneHeroV1StockTracker.dll`
  The tracker itself. This handles gameplay reads, overlay state, OBS exports, and saved tracker memory.
- `V1StockAssemblyPatcher.exe`
  Patches `Assembly-CSharp.dll` so the tracker loads automatically in a normal Clone Hero v1 install.
- `CloneHeroDesktopOverlay.exe`
  The companion overlay process used when the desktop overlay takes over from the in-game editor.

## User Release Pack

The user release pack is the zip that contains the installer, uninstaller, tracker files, and release notes.

How to install:
1. Close Clone Hero.
2. Make a copy of your vanilla Clone Hero folder if you want to keep one untouched backup.
3. Extract the release zip somewhere convenient.
4. Double-click `Install Clone Hero Section Tracker.cmd`.
5. Type or paste the Clone Hero folder that contains `Clone Hero.exe`.
6. Launch Clone Hero from that folder.

What gets installed:
- `CloneHeroV1StockTracker.dll`
- `CloneHeroDesktopOverlay.exe`
- a patched `Assembly-CSharp.dll`
- a backup of the original `Assembly-CSharp.dll` created as `Assembly-CSharp.sectiontracker-backup.dll`

The release pack also includes:
- `Install Clone Hero Section Tracker.cmd`
- `Uninstall Clone Hero Section Tracker.cmd`
- `Uninstall Clone Hero Section Tracker and Wipe Data.cmd`
- `README.txt`
- `version.txt`
- `RELEASE_NOTES.txt`
- `CHANGELOG.txt`
- `V1StockAssemblyPatcher.exe`
- `V1RuntimeCompatibilityChecker.exe`
- `Mono.Cecil.dll`
- `Mono.Cecil.Rocks.dll`

## Data Files

The tracker writes to `%LOCALAPPDATA%\CloneHeroSectionTracker`.

That folder contains:
- `state.json`
- `memory.json`
- `config.json`
- `desktop-style.json`
- `obs\...`

## Notes

- OBS exports are opt-in from the overlay editor.
- Completed runs are always stored in `memory.json`, but the OBS `runs` folder is only written when that export is enabled.
- The desktop overlay works best in borderless or windowed mode.
- The uninstallers restore the backed up `Assembly-CSharp.dll` and remove the tracker files. The full cleanup option also deletes `%LOCALAPPDATA%\CloneHeroSectionTracker`.
