StatTrack sharedassets1.assets baselines

Place clean, untouched Clone Hero v1 sharedassets1.assets variants here when a supported game build needs a different Unity serialized asset version.

Build output creates:
- clean\sharedassets1.<clean-sha256>.assets
- patched\sharedassets1.<clean-sha256>.assets
- patched\sharedassets1-manifest.txt

The installer chooses an exact hash match first. If the live asset version and UnityPlayer version disagree, it uses the UnityPlayer version so older installs with mismatched assets can be repaired.

Current extra baseline:
- sharedassets1-2021.3.14f1.assets
