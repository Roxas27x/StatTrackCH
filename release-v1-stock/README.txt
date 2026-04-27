StatTrack

This mod turns a normal Clone Hero v1 install into an OBS-friendly stat tracking build.

What it adds
- an in-game editor for choosing what to track
- optional desktop overlay widgets
- opt-in OBS export files
- saved run history, section progress, overlay layout, and tracker settings

Recommended setup
1. Close Clone Hero.
2. Extract this release somewhere convenient.
3. Double-click "Install StatTrack.cmd".
4. Choose Y to install into "C:\Program Files\Clone Hero", or choose N to browse to a different Clone Hero folder.
5. If you install into Program Files, approve the administrator prompt when Windows asks.
6. Launch Clone Hero from that folder.

What the installer does
- checks that the selected folder is a real Clone Hero v1 install
- backs up the existing "Clone Hero_Data\Managed\Assembly-CSharp.dll" and "Clone Hero_Data\sharedassets1.assets" instead of deleting them
- restores those local baselines on reinstall so repeated installs do not stack patches
- copies in the tracker files
- patches Assembly-CSharp.dll so the mod loads automatically
- detects your sharedassets1.assets hash, asset serialized version, and UnityPlayer version
- installs the StatTrack-patched sharedassets1.assets variant that matches that clean asset hash or Unity serialized version
- keeps your original sharedassets1.assets when no supported asset baseline matches, which avoids black menus and missing textures on other Clone Hero builds

Overlay hotkeys
- Home
- F8

Tracker data location
The tracker stores its files in:
%LOCALAPPDATA%\StatTrack

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
2. Double-click "Uninstall StatTrack.cmd".
3. Type or paste the same Clone Hero folder you installed into.

Optional full cleanup
- Double-click "Uninstall StatTrack and Wipe Data.cmd" to also delete %LOCALAPPDATA%\StatTrack
