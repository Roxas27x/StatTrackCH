Clone Hero Section Tracker

This mod turns a normal Clone Hero v1 install into an OBS-friendly tracker build.

What it adds
- an in-game editor for choosing what to track
- optional desktop overlay widgets
- opt-in OBS export files
- saved run history, section progress, overlay layout, and tracker settings

Recommended setup
1. Close Clone Hero.
2. Make a copy of your vanilla Clone Hero folder so you keep one untouched backup.
3. Extract this release somewhere convenient.
4. Run install.ps1.
5. Select the Clone Hero folder that contains "Clone Hero.exe".
6. Launch Clone Hero from that folder.

What the installer does
- checks that the selected folder is a real Clone Hero v1 install
- creates a backup of "Clone Hero_Data\Managed\Assembly-CSharp.dll"
- copies in the tracker files
- patches Assembly-CSharp.dll so the mod loads automatically

Overlay hotkeys
- Home
- Ctrl+O
- F8

Tracker data location
The tracker stores its files in:
%LOCALAPPDATA%\CloneHeroSectionTracker

That folder contains:
- state.json
- memory.json
- config.json
- desktop-style.json
- obs\...

A few quick notes
- OBS exports are off by default. Turn on only the ones you actually want.
- Completed runs are always saved in memory.json, but the OBS "runs" folder is only written when that export is enabled.
- The desktop overlay works best in borderless or windowed mode.

Uninstall
1. Close Clone Hero.
2. Run uninstall.ps1.
3. Select the same Clone Hero folder you installed into.

Optional full cleanup
- Run uninstall.ps1 -RemoveTrackerData to also delete %LOCALAPPDATA%\CloneHeroSectionTracker
