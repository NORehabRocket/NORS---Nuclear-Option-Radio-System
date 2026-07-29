# NORS 0.7.2 — Release Notes

**Required for Nuclear Option 0.34**, and it fixes the "some frequencies just
don't work" problem people have been hitting in multiplayer.

## Your radios no longer change while you're typing
If you sent a chat message containing a `,` or a `.`, those keystrokes were
also reaching the radio — silently retuning whichever radio you transmit on
(and `y` switched which radio that was). After a few messages you and your
wingman were on different frequencies with no idea why, so most channels
"stopped working" while the one you'd never touched still did.

Now **nothing radio-related responds while the game's chat box is open** —
push-to-talk, the panel key, cycle-TX, tuning and the MFD toggle all stand
down, and the radio panel itself greys out with a "chat open" note until you
close chat. (The hotkey half of this came from **@Appulcake**'s pull request —
thanks!)

**Also new: a "Reset to defaults" button** on the radio panel (F7), so if your
radios ever end up somewhere strange you're one click from the defaults
instead of restarting the game.

> Already stuck? Restart the game — frequencies aren't saved, so they come back
> as defaults. Then check the F7 panel: you and your friends need the **same
> frequency and the same AM/FM** on the radio you're using, and SECURE (on by
> default) means you'll only hear your **own faction**.

## The download itself is fixed (Linux/Proton in particular)
Previous release zips were built with a Windows tool that writes folder separators as
`\` instead of the `/` the ZIP format requires. Most Windows extractors quietly fixed
it; **Linux/Proton did not** — the archive unpacked as a single file literally named
`NORS\NORS.dll` instead of a `NORS` folder, so `NORS.Common.dll` was never where the
plugin looks and the mod failed to load. Some Windows setups hit it too, depending on the
unzip tool.

This release is packaged correctly and the build now verifies every archive before it
ships. If you previously had to rename files by hand, you don't any more — delete the old
`NORS` folder and extract this one fresh. Huge thanks to **Lomb(otomy)**, **Wheat**,
**nat** and **Maelle** for tracking it down.

## Redesigned radio panel — and passcodes you can actually see
The F7 panel got the DarkSkies treatment: dark navy and cyan to match the web panels,
the transmit radio highlighted, and **one compact line per radio** so six radios no
longer fill your screen (old layout still available: `UI/CompactRadios = false`).

More importantly, **every radio now has a LOCK button** — it lights up when that
channel is encrypted, and opens a field to set or clear the passcode right there.
Until now a passcode could only be set in the config file or from the ATC web panel,
so a radio could end up encrypted with no way to tell in game — you'd just hear
nothing and assume the mod was broken. **An empty passcode means an open channel:
anyone on that frequency can hear you.** And if someone transmits with a passcode you
don't have, the panel now tells you that's what happened instead of staying silent.

## Fixed for game 0.34
- **Player names** — 0.34 replaced the plain `PlayerName` string with a
  resolved/censored name object. NORS now uses the game's own lookup, so
  callsigns show correctly on the panel, roster and HUD readout.
- **MFD bezel page** — the game's MFD labels moved to TextMeshPro; the optional
  NORS "RADIO" page builds against the new type.

## Compatibility
- Plugin-only update — **no relay/server update needed**, no wire-format
  changes. 0.7.2 interoperates with 0.7.0/0.7.1 clients and any relay.
- **On game 0.34 you need this version** — older builds error when reading
  player names on the new game build.
