# NORS 0.7.3 — Release Notes

**Linux/Proton install fix.** If NORS already works for you, this changes
nothing in game — the plugin code is identical to 0.7.2. What's fixed is the
**download itself**.

## The zip was malformed
Our release archives were built with a Windows tool that writes folder
separators as `\`, but the ZIP format requires `/`. Most Windows extractors
quietly corrected it, so it looked fine to us — **Linux/Proton did not.**
There, `\` is a legal filename character, so the archive unpacked as a single
file literally named `NORS\NORS.dll` instead of a `NORS` folder. `NORS.dll`
could then never find `NORS.Common.dll`, and the mod failed to load. Some
Windows users hit it too, depending on their unzip tool.

Packaging now writes entry names itself (always `/`) and **verifies every
archive before release** — if one ever regresses, the build fails instead of
shipping a broken download.

## Installing this one
Delete your old `BepInEx/plugins/NORS` folder, then extract fresh. If you
renamed files by hand to work around this, that leftover mess goes away — and
NOMM's manifest data comes through properly now.

Huge thanks to **Lomb(otomy)**, **Wheat**, **nat** and **Maelle** for chasing
this down — Maelle pinned the exact cause.

## Compatibility
Identical plugin to 0.7.2: no relay/server changes, no wire-format changes,
interoperates with every 0.7.x client. Still required on Nuclear Option 0.34 —
see the 0.7.2 notes for the game-update fixes, the chat/radio input lockout and
the redesigned radio panel.
