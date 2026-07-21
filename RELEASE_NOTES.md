# NORS 0.7.0 — Release Notes

Everything new since 0.5.0 (the previous public release).

## Six radios (was three)
R1–R6, each with its own frequency, AM/FM modulation, monitoring toggle and
volume. Defaults: 251.0 UHF · 124.0 VHF air · 40.0 FM tac · 68.0 FM ground ·
143.5 · 243.0 GUARD. Enough to monitor tower, tactical, guard and a command
net at the same time. The F7 panel and HUD readout scale automatically.

## Encrypted channels (SRS-style passcodes)
Give any radio a passcode and its net is scrambled end-to-end: only players
tuned to the same frequency **with the same passcode** hear voice — everyone
else, same faction included, gets nothing. The audio payload itself is
scrambled, so older clients can't accidentally eavesdrop either. Set it in
`F1 → NORS → Radios → RadioNCrypto`, or from the DarkSkies ATC panel (🔒 on
each radio). Perfect for command/zeus nets. Works alongside the existing
faction-SECURE mode.

## Relay server: browser admin panel
The relay now hosts a web dashboard (default `http://localhost:8700/`):
live roster with kick/ban buttons, ban list with unban, live server log and
traffic stats. Password login (`--admin-pass`, or an auto-generated code is
printed at startup). New flags: `--web-port`, `--web-lan`, `--no-web`.
Finally makes headless Linux/VPS relays fully manageable from any browser —
no more WinForms-or-SSH.

## DarkSkies ATC integration (NorsApi)
A public API that the DarkSkies ATC mod detects automatically. Through the
ATC web panel you can now: see your whole radio stack, retune any radio, pick
the TX radio, toggle monitoring, set channel passcodes, one-click tune to a
base's tower frequency, transmit with a hold-to-talk button, and see who's
transmitting as a live glow on the radar scope. (No hard dependency either
way — NORS works exactly as before without the ATC mod.)

## Smarter defaults: pick your PTT on first launch
Push-to-talk now ships **unbound** — the old default (T) is the game's chat key, so
every chat message keyed the radio. On first launch NORS pops a setup window at the
main menu: press any key (or quick-pick Caps Lock / tilde / Mouse 4/5) and you're
set. Existing players keep their configured key and never see the popup. NORS inputs
are also ignored while the chat box is open. (Thanks **@Appulcake** for the first
community pull request!)

## Compatibility

- **Wire protocol unchanged (v2).** Existing relays forward the new traffic;
  the 0.7.0 relay accepts older clients.
- **Encrypted nets need 0.7.0 on every ear** — older clients hear noise
  bursts on passcoded frequencies (by design: the audio is genuinely
  scrambled). Update the whole squadron together.
- Config gains `Radio4–6` frequency/modulation and `Radio1–6Crypto` entries;
  all existing settings carry over unchanged.
