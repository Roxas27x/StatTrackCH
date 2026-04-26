**StatTrack v1.0.7**

This release is a smoothness and install-safety pass for Clone Hero v1. The goal is simple: less work during gameplay, fewer unnecessary exports/redraws, and a repeatable clean install path for every user.

## Highlights
- Reduced in-song tracking work so StatTrack only reads stats required by active exports or NoteSplit.
- Optimized NoteSplit and the desktop overlay to skip unnecessary redraws and state writes while a song is running.
- Restored NoteSplit attempt updates without forcing unrelated OBS export writes.
- Kept section FC exports lightweight: only checked current-song sections stay in the active export path.
- Added clean-baseline installation. The installer renames the user's existing `Assembly-CSharp.dll` and `sharedassets1.assets`, installs bundled clean copies, then applies StatTrack on top.
- Replaced the stock main-menu News feed with StatTrack update history entries that link to GitHub releases.
- Added a StatTrack Discord card to the top of the News feed.
- Added keyboard focus controls for the News feed: Tab toggles focus, Right Arrow enters, and Left Arrow exits.
- Removed stale Ctrl+O help text. Overlay hotkeys are now Home and F8.

## Install Notes
- Close Clone Hero before installing.
- The installer renames existing game files instead of deleting them.
- The release zip now includes `clean\Assembly-CSharp.dll`, `clean\sharedassets1.assets`, and `patched\sharedassets1.assets` so every install starts from the same baseline.

## Verification
- Build and runtime compatibility check passed locally.
- Release package generated as `StatTrack-v1.0.7.zip`.
