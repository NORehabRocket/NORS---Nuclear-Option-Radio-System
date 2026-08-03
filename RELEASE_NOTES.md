# NORS 0.7.4 — Release Notes

**Compatibility update for the current Nuclear Option build (0.34.x).**

The game changed how player names are read — for the second time in two weeks
(`Player.PlayerName` → `GetNameOrCensored()` → `GetDisplayName(context)`).
Without this update, callsigns break on the current game build.

Every name lookup now goes through a single helper, so the next time the game
moves it, it's a one-line fix rather than a hunt across the mod.

Nothing else changed: same radios, encryption, propagation, transports, panel
and relay as 0.7.3. Pairs with **DarkSkies ATC 0.7.4**, which gains live
glideslope readouts from the game's new approach API.

## Compatibility
Plugin-only — no relay/server update needed, no wire-format changes,
interoperates with every 0.7.x client. **Required on the current game build.**