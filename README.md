# StatTrack

StatTrack is a Clone Hero v1 mod for stream-friendly run tracking, NoteSplit splits, and opt-in OBS text exports.

Current public release: **StatTrack v1.0.7**  
Download: [GitHub Releases](https://github.com/Roxas27x/StatTrackCH/releases)

## What It Adds

- Per-song tracking for attempts, current streak, best FC streak, missed notes, overstrums, ghosted notes, lifetime ghosts, FC achieved, section FCs Past, NoteSplit personal bests, and completed runs.
- A lightweight in-game editor opened with `Home` or `F8`.
- Opt-in OBS text files for only the exports you enable.
- NoteSplit Mode, a separate LiveSplit-style desktop window for section miss splits.
- Export Templates for customizing the text written to OBS files.
- Main-menu animated tint controls.
- A StatTrack main-menu News feed with the Discord card and update-history links.
- Clean-baseline installation so every user installs over the same clean `Assembly-CSharp.dll` and `sharedassets1.assets`.

## v1.0.7 Focus

v1.0.7 is a smoothness and install-safety release. The tracker now tries to do as little as possible during songs unless a setting needs that work.

- Exact miss tracking is active only for exports or modes that need exact misses.
- NoteSplit can update attempts and section misses without forcing OBS text writes.
- Section FC exports only keep checked sections active and only write each checked section's `fcs_past.txt`.
- Completed-run reflection stays near the end of a song instead of running constantly.
- Desktop overlay state is written for NoteSplit only when NoteSplit is enabled.
- The desktop overlay idles when NoteSplit is hidden.
- Legacy desktop/stat widgets are intentionally disabled for this patch while the smoothness pass is locked in.

## Installation

1. Close Clone Hero.
2. Download the latest `StatTrack-v1.0.7.zip` from [Releases](https://github.com/Roxas27x/StatTrackCH/releases/latest).
3. Extract the zip somewhere convenient.
4. Run `Install StatTrack.cmd`.
5. Choose `Y` for the default `C:\Program Files\Clone Hero` install, or choose `N` to browse to a portable Clone Hero folder.
6. If Windows asks for administrator access, approve it.
7. Launch `Clone Hero.exe` from that same folder.

The installer checks that the selected folder contains a Clone Hero v1 install. It then:

- Renames the existing `Clone Hero_Data\Managed\Assembly-CSharp.dll` instead of deleting it.
- Renames the existing `Clone Hero_Data\sharedassets1.assets` instead of deleting it.
- Installs bundled clean copies of both files.
- Copies in `StatTrack.dll` and `StatTrackOverlay.exe`.
- Patches the clean `Assembly-CSharp.dll` so StatTrack loads automatically.
- Installs the StatTrack-patched `sharedassets1.assets` used by animated menu tint support.

Preferred backup names are:

- `Clone Hero_Data\Managed\Assembly-CSharp.sectiontracker-backup.dll`
- `Clone Hero_Data\sharedassets1.assets.stattrack-backup`

If either backup already exists, the installer creates a timestamped `.pre-stattrack-...bak` file instead.

## Uninstall

Run `Uninstall StatTrack.cmd` from the extracted release folder and select the same Clone Hero folder you installed into.

The uninstaller restores the renamed `Assembly-CSharp.dll` and `sharedassets1.assets` backups when they exist, then removes the StatTrack runtime files. `Uninstall StatTrack and Wipe Data.cmd` also deletes `%LOCALAPPDATA%\StatTrack`.

## Editor Hotkeys

| Key | Action |
| --- | --- |
| `Home` | Open or close the StatTrack editor |
| `F8` | Open or close the StatTrack editor |
| `Esc` | Close the StatTrack editor |

The editor is intentionally light when closed. During gameplay, leave it closed unless you are changing settings.

## Main Menu News Controls

StatTrack replaces Clone Hero's main-menu News list with StatTrack update history. The latest card is `StatTrack Discord!`, which links to [the Discord server](https://discord.gg/VzNaZ3m4HC). Release cards link to their matching GitHub release pages.

| Key | Action |
| --- | --- |
| `Tab` | Toggle focus between the normal menu and the News list |
| `Right Arrow` | Enter the News list |
| `Left Arrow` | Exit the News list |
| `Up` / `Down` | Move through News items after the News list is focused |
| `Enter` | Open the selected News link |

## Data And Output Folders

StatTrack stores its files in:

```text
%LOCALAPPDATA%\StatTrack
```

Common files:

| File or folder | Purpose |
| --- | --- |
| `config.json` | Saved global settings, per-song export choices, and template overrides |
| `memory.json` | Saved song stats, section memory, NoteSplit bests, and completed runs |
| `state.json` | Live desktop overlay state, written only when a desktop mode needs it |
| `desktop-style.json` | Desktop overlay and NoteSplit style settings |
| `obs\` | OBS text output folder |
| `v1-stock.log`, `desktop-overlay.log`, `hitch-debug.log` | Troubleshooting logs |

Use the editor's `OBS EXPORT FOLDER` button to open this data folder.

## OBS Export Model

OBS exports are opt-in. Disabled exports do not keep writing files during gameplay.

Live metric files are written under:

```text
%LOCALAPPDATA%\StatTrack\obs\current
```

Song-scoped files are written under:

```text
%LOCALAPPDATA%\StatTrack\obs\songs\<song-key>
```

Checked section FC exports are written under:

```text
%LOCALAPPDATA%\StatTrack\obs\songs\<song-key>\sections\<section-name>\fcs_past.txt
```

Completed-run files are written only when `Completed Runs` is enabled:

```text
%LOCALAPPDATA%\StatTrack\obs\songs\<song-key>\runs\<run-folder>
```

Turning an export off also cleans up stale files for that export path.

## Export Toggles

| Toggle | What it does |
| --- | --- |
| `NoteSplit Mode` | Starts the separate `Clone Hero NoteSplit` desktop window. This is not an OBS text export by itself. |
| `Current Section` | Writes `current_section.txt`. |
| `Current Streak` | Writes `streak.txt`. |
| `Best FC Streak` | Writes `best_streak.txt`. |
| `Total Attempts` | Writes `attempts.txt`. NoteSplit attempts still update when NoteSplit is enabled, even if this OBS export is off. |
| `Current Ghosted Notes` | Writes `current_ghosted_notes.txt`. |
| `Current Overstrums` | Writes `current_overstrums.txt`. |
| `Current Missed Notes` | Writes `current_missed_notes.txt`. |
| `Song Lifetime Ghosts` | Writes `lifetime_ghosted_notes.txt` for the current song profile. |
| `Global Lifetime Ghosts` | Writes `global_lifetime_ghosted_notes.txt`. |
| `FC Achieved` | Writes `fc_achieved.txt`. |
| `Completed Runs` | Writes saved completed-run history under the current song's `runs` folder. |
| `DISABLE ALL EXPORTS` | Turns off every export toggle, including NoteSplit Mode. |

## NoteSplit Mode

`NoteSplit Mode` opens a separate desktop window intended for OBS window capture. It shows section-by-section miss splits, section personal best miss counts, total attempts, current total misses, and previous section results.

In v1.0.7 the NoteSplit window is lazy-created only when NoteSplit is enabled and visible. If NoteSplit is off, the desktop process should not launch just to idle.

Right-click the NoteSplit window for quick style options such as font selection and always-on-top behavior.

## Section FCs Past

When a song is loaded, the editor shows section checkboxes for the current song. Checked sections are the only sections included in active section export snapshots.

For v1.0.7, checked sections export only:

```text
fcs_past.txt
```

Older `name.txt`, `summary.txt`, `tracked.txt`, `start_time.txt`, `attempts.txt`, and `killed_the_run.txt` section files are cleaned up if they exist.

## Export Templates

Open `EXPORT TEMPLATES` in the editor to customize FCs Past counters. this will be updated with the previous section files in a later update.

## Main Menu Animated Tint

The editor includes `Main Menu Animated Tint` controls for the main menu background, canvas, and wisp colors. These settings are visual only and are saved with the rest of the StatTrack config.

Animated tint support uses the StatTrack-patched `sharedassets1.assets`, which is why the installer now starts from bundled clean assets before installing the patched version.

## Current Widget Status

Older StatTrack builds experimented with pinned desktop stat widgets and section widgets. Those legacy widget surfaces are out of scope for v1.0.7 and are intentionally hard-disabled while the gameplay smoothness pass ships.

Use NoteSplit Mode for the supported desktop window, and use opt-in OBS text exports for stream layouts.

## Performance Notes

- Turn on only the exports you actively use.
- Leave the in-game editor closed during runs.
- Use `DISABLE ALL EXPORTS` when testing raw gameplay smoothness.
- `Current Missed Notes` and `Current Overstrums` require exact miss tracking, so only enable them when you need those files.
- `Completed Runs` still saves run history in `memory.json`; the toggle only controls the OBS `runs` folder.

## Build Notes

This repository is built around Clone Hero v1 stock files. The source snapshot expects clean baseline copies of:

```text
Assembly-CSharp.dll
sharedassets1.assets
```

The release build scripts package those clean files into `clean\`, generate the patched asset in `patched\`, and produce the release zip.

Useful scripts:

| Script | Purpose |
| --- | --- |
| `build-v1-stock.ps1` | Builds the tracker, patcher, overlay, clean baseline package, and release folder |
| `install-v1-stock.ps1` | Local developer install helper |
| `package-v1-stock-release.ps1` | Packages the release folder into a zip |

## Links

- [Latest release](https://github.com/Roxas27x/StatTrackCH/releases/latest)
- [All releases](https://github.com/Roxas27x/StatTrackCH/releases)
- [StatTrack Discord](https://discord.gg/VzNaZ3m4HC)
