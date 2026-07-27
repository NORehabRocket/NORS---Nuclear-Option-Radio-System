# NORS 0.7.2 — Release Notes

**Compatibility update for Nuclear Option 0.34.** Update to this version if
you're on the current game build.

## Fixed for game 0.34
- **Player names** — 0.34 replaced the plain `PlayerName` string with a
  resolved/censored name object. NORS now uses the game's own name lookup, so
  callsigns show correctly again on the radio panel, roster and HUD readout.
- **MFD bezel page** — the game's MFD labels moved from Unity UI text to
  TextMeshPro; the optional NORS "RADIO" page follows suit and builds against
  the new type.

Nothing else changed — radios, encryption, propagation, transports and the
relay all behave exactly as in 0.7.1.

## Compatibility
- Plugin-only update — **no relay/server update needed**, no wire-format
  changes. 0.7.2 clients interoperate with 0.7.0/0.7.1 clients and any relay.
- **On game 0.34 you need this version** — 0.7.1 and older will throw errors
  when reading player names on the new build.
