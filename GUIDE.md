# NORS — Player & Host Guide (v0.7.0)

**NORS (Nuclear Option Radio System)** is DCS-SRS-style voice radio for
Nuclear Option: positional, frequency-based voice with realistic range,
terrain shadowing, jamming and relays. Part of the **DarkSkies ATC & Radio**
pack; also works standalone.

---

## TL;DR

- **Talk:** hold your **PTT key** — you pick it in a popup on first launch. Radio panel: **F7**. Switch TX radio: **Y**.
  Tune: **, / .**
- It works out of the box over **Steam P2P** — no server, no setup.
- Six radios (R1–R6). Match **frequency + modulation** with whoever you want
  to talk to. Distance, terrain and jamming all matter.
- **Encrypted net:** give a radio a passcode (F1 config or the ATC panel 🔒).
  Only same-passcode players hear that net.

---

## 1. Install

Extract the DarkSkies pack into your game folder →
`BepInEx\plugins\NORS\NORS.dll` (+ `NORS.Common.dll`). Launch. The log shows
`Nuclear Option Radio System v0.7.0 online`.

## 2. Using the radio (pilots)

| Key (rebindable, F1 → NORS → Input) | Does |
|---|---|
| **Your PTT key** (hold) | Transmit — chosen in the first-launch popup (unbound out of the box; avoid T, that's the game's chat key) |
| **F7** | Radio panel (frequencies, volumes, connect state, players) |
| **Y** | Cycle which radio you transmit on |
| **, / .** | Tune the TX radio down / up one step (25 kHz) |

- **Six radios**, all monitored at once (any can be muted). You transmit on
  one — the panel and the HUD readout show which.
- Defaults: R1 251.0 (UHF) · R2 124.0 (VHF air) · R3 40.0 (FM tac) ·
  R4 68.0 (FM ground) · R5 143.5 · R6 243.0 (GUARD).
- A compact **HUD readout** by the fuel gauge shows your TX radio and who's
  talking; it auto-appears on radio activity.
- **You hear what physics says you hear**: range (FM shorter than AM), radio
  horizon (altitude helps), terrain blocks, enemy jamming garbles — and
  friendly relays (AWACS Radomes, radar ground units, ships, airbases) can
  carry a transmission over the horizon. If a teammate is scratchy: climb.
- **SECURE** (per radio, default on) keeps voice unintelligible to the enemy
  faction. **Died?** You keep talking/hearing from your side's nearest base.

### Encrypted channels (passcodes)

Any radio can carry a passcode — then its net is scrambled for everyone who
doesn't have the same code (your own faction included):

- Set it in **F1 → NORS → Radios → RadioNCrypto**, or from the DarkSkies ATC
  panel (**🔒** next to the radio, amber = active).
- Everyone on the net enters the **same passcode** and the **same frequency**.
- Clear the passcode to go back to a normal channel.
- Use it for command nets, zeus/referee coordination, or flight-internal chat.

## 3. Transports: P2P (default) vs relay

- **P2P (default):** voice goes over Steam's own networking. Nothing to host,
  no port forwarding — if you can play together, you can talk.
  Moderation: the game host can mute/ban by Steam id from the F7 panel.
- **Relay:** set `Transport = Relay` + `ServerHost` in F1 to route through a
  standalone NORS relay server. Use it for large community servers (central
  admin, persistent bans, rooms per game session). Everything else behaves
  identically.

## 4. Hosting a relay (optional)

- **On your PC:** double-click `start-relay.bat` — starts the relay and opens
  the **web admin panel** (`http://localhost:8700`). Edit the password at the
  top of the bat.
- **Web admin:** live roster with **KICK/BAN**, ban list with **UNBAN**, live
  log, traffic stats. Login = `--admin-pass` (or the auto-generated code
  printed in the console). `--web-lan` exposes it to your network;
  `--web-port` changes the port; `--no-web` disables it.
- **On a VPS:** `publish-server.ps1` builds self-contained relays for
  Windows/Linux/macOS (`dist\server\<platform>`). Run with `--headless`;
  administer entirely from the browser. Default voice port UDP 5555.
- Vote-kick is on by default (faction-scoped, majority); `--no-votekick`
  disables it.

## 5. Troubleshooting

- **Nobody hears me** — is PTT even bound? (First-launch popup, or F1 > NORS > Input.) Mic plugged in before launch? F7 shows mic state and
  a level meter (enable `MicMonitor` to hear yourself). Check you're
  transmitting on the radio you think (**Y** / the HUD readout).
- **I can't hear them** — same **frequency AND modulation**? In range /
  line-of-sight? On an encrypted net without the passcode you hear nothing.
- **Voice too quiet / static too loud** — F1 → NORS → Audio: `MicGain`,
  `ReceiveVolume`, `StaticLevel`.
- **Scratchy at long range is normal** — climb, or get near a relay
  (AWACS/radar unit/airbase).
- **Encrypted net sounds like noise** — your passcode doesn't match, or the
  transmitter is on a newer/older version. Everyone updates together.
