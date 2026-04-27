**StatTrack v1.0.8**

This release focuses on two things: better Song Speed control and steadier background export/write behavior during gameplay.

## Highlights
- Song Speed now defaults to 1% increments instead of Clone Hero's stock 5%.
- Pressing orange while focused on Song Speed cycles increments through 1%, 5%, 10%, and 50%, then back to 1%.
- Song Speed now supports 1% minimum and 10000% maximum.
- Active-song export work is now coalesced before the background worker writes files, so rapid main-thread update requests are batched instead of firing back-to-back.
- Active-song file writes are paced with a short cooldown between writes, which should reduce bursty disk activity and make gameplay more stable.
- Overlay input checks now run every 0.05 seconds for responsive editor/menu input without returning to the old ultra-aggressive polling.
- FCs Past exports now have a locked global fallback: `FCs UP TO {{section}}: {{fcs_past}}`.
- Each song section can optionally save one FCs Past override line.
- If a section has an override, that override replaces the locked fallback for that section only.
- If a section has no override, it stays on the global export path and does not create an extra redundant file.
- Old global `section.fcs_past` overrides from the previous attempt are ignored and scrubbed during template normalization.

## Stability Notes
- During songs, export work now waits briefly to collect any pending updates before writing.
- The worker also pauses briefly after an active-song export batch.
- Individual active-song text writes are spaced out instead of being allowed to hammer the filesystem in one tight burst.

## Install Notes
- Close Clone Hero before installing.
- The installer renames existing game files instead of deleting them.
- The release zip includes clean and patched sharedassets variants for the supported Clone Hero v1 baselines.

## Verification
- Build and runtime compatibility check passed locally.
- Local Fitz install passed before packaging.
- Release package generated as `StatTrack-v1.0.8.zip`.
