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
