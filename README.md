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
  The injected gameplay tracker. It reads live Clone Hero state, tracks runs, sections, misses, overstrums, ghost notes, streaks, completed runs, and NoteSplit personal bests, then saves that state to `%LOCALAPPDATA%\CloneHeroSectionTracker` and writes the OBS exports you enable.
- `V1StockAssemblyPatcher.exe`
  The install-time patcher for `Assembly-CSharp.dll`. It adds the hooks that load the tracker on startup, wires the tracker into the game update and miss paths it needs, and applies the controller-unplug patch so Clone Hero does not auto-pause when an input device disconnects mid-song.
- `CloneHeroDesktopOverlay.exe`
  The separate desktop overlay process. It reads the tracker state and config files, draws pinned desktop widgets, and runs the movable/resizable `Clone Hero NoteSplit` window when `NoteSplit Mode` is enabled.

### GUI Settings

The in-game editor changes a little depending on whether a song is loaded. Export toggles are global. Section tracking and desktop widgets are saved for the song currently shown in the editor header.

These dropdowns use the same names as the live GUI so screenshots can be added under each one later without having to reorganize the README.

#### Exports / Desktop Modes

<details>
<summary><code>OBS EXPORT FOLDER</code></summary>

Opens `%LOCALAPPDATA%\CloneHeroSectionTracker`, which contains the live `obs` export folder plus `state.json`, `memory.json`, `config.json`, and `desktop-style.json`.
</details>

<details>
<summary><code>NoteSplit Mode</code></summary>

Enables the separate `Clone Hero NoteSplit` window. NoteSplit shows section-by-section miss splits, section personal best miss counts, total attempts, the current total miss counter, and the previous section result. It is a separate taskbar window for OBS capture and automatically hides during bot play, practice mode, and while the in-game editor is open.
</details>

<details>
<summary><code>Current Section</code> (Export)</summary>

Writes the current section name for the active song to the OBS export files.
</details>

<details>
<summary><code>Current Streak</code> (Export)</summary>

Writes the live combo streak from the current run to the OBS export files.
</details>

<details>
<summary><code>Best FC Streak</code> (Export)</summary>

Writes the saved highest streak reached before a miss on this song profile. This is persistent tracker memory, not just the current run.
</details>

<details>
<summary><code>Total Attempts</code> (Export)</summary>

Writes the saved total attempts for this song profile. This is the same attempts count used in NoteSplit.
</details>

<details>
<summary><code>Current Ghosted Notes</code> (Export)</summary>

Writes the live ghost-note count from the current run.
</details>

<details>
<summary><code>Current Overstrums</code> (Export)</summary>

Writes the live overstrum count from the current run.
</details>

<details>
<summary><code>Current Missed Notes</code> (Export)</summary>

Writes the live missed-note count from the current run.
</details>

<details>
<summary><code>Song Lifetime Ghosts</code> (Export)</summary>

Writes the saved lifetime ghost-note total for the current song profile across sessions.
</details>

<details>
<summary><code>Global Lifetime Ghosts</code> (Export)</summary>

Writes the saved lifetime ghost-note total across every tracked song.
</details>

<details>
<summary><code>FC Achieved</code> (Export)</summary>

Writes whether this song profile has ever been FC'd in saved tracker memory.
</details>

<details>
<summary><code>Completed Runs</code> (Export)</summary>

Writes the saved completed-run history for the current song profile into the OBS export folder. Completed runs are still kept in `memory.json` even if this export is off; this toggle only controls the OBS files.
</details>

<details>
<summary><code>DISABLE ALL EXPORTS</code></summary>

Turns off every checkbox in the `Exports / Desktop Modes` section at once, including `NoteSplit Mode`.
</details>

#### Song-Loaded Tracking Options

<details>
<summary><code>Live Section Export Select</code></summary>

Shows one checkbox per detected section in the current song. Checking a section tells the tracker to actively export that section's `Attempts`, `FCs Past`, and `Killed the Run` values to OBS while that song is loaded. When at least one section is checked, the tracker also writes the live `current_section_summary.txt` file automatically.
</details>

<details>
<summary><code>Section Widgets</code></summary>

Shows one checkbox per detected section in the current song. Checking a section creates a draggable desktop widget for that section's `Attempts`, `FCs Past`, and `Killed the Run` panel.
</details>

<details>
<summary><code>Stat Widgets</code></summary>

This group controls the pinned desktop widgets for the built-in tracker stats listed below. Enabled widgets stay draggable after you close the editor.
</details>

<details>
<summary><code>Current Streak</code> (Stat Widget)</summary>

Creates a desktop widget that shows the live combo streak from the current run.
</details>

<details>
<summary><code>Best FC Streak</code> (Stat Widget)</summary>

Creates a desktop widget that shows the saved highest streak reached before a miss on the current song profile.
</details>

<details>
<summary><code>Current Missed Notes</code> (Stat Widget)</summary>

Creates a desktop widget that shows the live missed-note count from the current run.
</details>

<details>
<summary><code>Current Overstrums</code> (Stat Widget)</summary>

Creates a desktop widget that shows the live overstrum count from the current run.
</details>

<details>
<summary><code>Current Ghosted Notes</code> (Stat Widget)</summary>

Creates a desktop widget that shows the live ghost-note count from the current run.
</details>

<details>
<summary><code>Song Lifetime Ghosts</code> (Stat Widget)</summary>

Creates a desktop widget that shows the saved lifetime ghost-note total for the current song profile.
</details>

<details>
<summary><code>Global Lifetime Ghosts</code> (Stat Widget)</summary>

Creates a desktop widget that shows the saved lifetime ghost-note total across all tracked songs.
</details>

<details>
<summary><code>Total Attempts</code> (Stat Widget)</summary>

Creates a desktop widget that shows the saved total attempts for the current song profile.
</details>

<details>
<summary><code>FC Achieved</code> (Stat Widget)</summary>

Creates a desktop widget that shows whether this song profile has ever been FC'd.
</details>

<details>
<summary><code>WIDGET BORDER COLOR</code></summary>

Opens the shared desktop-overlay border color picker used by the widget frames.
</details>

<details>
<summary><code>Overlay Transparency</code></summary>

Changes the in-game editor background opacity with the `Background Opacity` slider.
</details>

<details>
<summary><code>RESET OVERLAY</code></summary>

Clears the saved section-selection and desktop-widget layout for the current song without deleting the song's tracked stats.
</details>

<details>
<summary><code>RESET SONG STATS</code></summary>

Deletes the saved tracker memory for the current song profile, including attempts, streak records, section progress, completed runs, and NoteSplit personal best miss counts for that song.
</details>

<details>
<summary><code>WIPE ALL MOD DATA</code></summary>

Deletes all saved tracker data and settings, including `state.json`, `memory.json`, `config.json`, `desktop-style.json`, and the current OBS exports, then starts the mod fresh.
</details>

#### NoteSplit Window Menu

<details>
<summary><code>Right-Click NoteSplit</code></summary>

Opens the NoteSplit context menu, which gives quick access to `Settings...`, `Reset Position`, and `Keep NoteSplit above other windows`.
</details>

<details>
<summary><code>Settings...</code></summary>

Opens the NoteSplit settings dialog. While this dialog is open, the window temporarily gives priority to the settings dialog so it is easier to edit without focus fights.
</details>

<details>
<summary><code>Choose Font...</code></summary>

Lets you change the NoteSplit font family and effective font scale.
</details>

<details>
<summary><code>Keep NoteSplit above other windows</code></summary>

Controls whether the NoteSplit window stays topmost after the settings dialog is closed.
</details>

<details>
<summary><code>Reset Position</code></summary>

Moves NoteSplit back to its default placement near the game window and clears the saved custom position.
</details>

<details>
<summary><code>Resize NoteSplit by dragging the lower-right corner of the window.</code></summary>

NoteSplit can be resized directly with the mouse instead of typing width and height values. The new size is saved automatically.
</details>

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
