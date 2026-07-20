# NORS — Changelog

## v0.7.0

Six radios, encrypted channels, a browser admin panel for the relay, and the
DarkSkies ATC integration (covers 0.6.0 + 0.7.0; 0.6.0 was never published).
**No protocol change** — old relays forward the new traffic, but **encrypted
nets need 0.7.0 on every client** (others hear scrambled noise, by design).

### 📻 Six radios
- R1–R6 (was three), each with frequency, AM/FM, monitor toggle and volume.
  New config: `Radio4–6` freq/mod. Panel and HUD readout scale automatically.

### 🔐 Encrypted channels (SRS-style passcodes)
- Per-radio passcode (`Radio1–6Crypto` or the ATC panel 🔒): the voice payload
  itself is scrambled — only same-frequency + same-passcode players hear it,
  own faction included. Independent of the existing faction-SECURE mode.

### 🖥️ Relay web admin
- The console relay now hosts a browser dashboard (default
  `http://localhost:8700/`): live roster with kick/ban, ban list with unban,
  live log, traffic stats. Password login (`--admin-pass` or auto-generated).
  New flags: `--web-port`, `--web-lan`, `--no-web`. Makes headless
  Linux/VPS relays fully manageable.

### 🗼 DarkSkies ATC integration (NorsApi, 0.6.0+)
- Public API the DarkSkies ATC mod auto-detects: radio stack on its web panel
  (retune / TX select / monitor / passcodes / one-click tower tuning),
  hold-to-talk from the browser, and live who's-transmitting glow on its scope.
  Soft dependency — NORS is unchanged without it.

### 🏛️ Open source
- Full source now public under the MIT license.
## v0.5.0

Audio overhaul + relay expansion, driven directly by server-test feedback. **Plugin‑only — no
protocol changes**: no relay-server update needed, and 0.5.0 players can talk to 0.4.0/0.3.0
players (the fixes work best when everyone updates).

### 🔊 Audio: much louder, clearer voice
- **Voice is far louder** — the radio filter was shedding most of the voice's volume with no makeup
  gain, so it came out way too quiet. Added strong makeup gain + a cleaner limiter.
- **Static no longer drowns the voice** — cut static level and rebalanced it to sit well under speech.
- New louder defaults (`MicGain` 2.5, `ReceiveVolume` 1.6, `StaticLevel` 0.25) with more headroom.
- **No more frame hitch on push‑to‑talk** — the mic used to restart on every key press, causing a
  momentary FPS drop (which threw off mouse/freelook sensitivity). The mic now stays open
  mid‑mission and transmission is just gated, so PTT is smooth.
- **FM is clearer than AM** (less static); AM still reaches further — a real trade‑off.

### 🖥️ HUD readout: attached to the fuel bar, compact, movable
- The readout now sits **on top of the FUEL bar and tracks it every frame** (the HUD floats with
  your head/camera — one‑time placement drifted).
- **Compact by default** — just the active radio + who's transmitting. `MFD → HudVerbose = true`
  for the full three‑radio list.
- **Move it anywhere** — `HudOffsetX` / `HudOffsetY` to slide it clear of other mods' overlays
  (NOAutopilot, Fuel Burn Endurance Timer); `HudAnchor = Throttle` to use the throttle bar instead.
- **Fixed:** no longer attaches to other mods' HUD elements (e.g. autopilot GCAS arrows) and no
  longer lingers after switching/exiting aircraft.

### 📻 New: ground & naval relays, and radio while dead
- **Radar stations, carriers, and airbase towers now act as relays** (`RelayGroundStations`). They
  sit low, so they only cover their local horizon — around‑base comms, and a carrier keeps its
  strike group connected over water.
- **Dead / respawn‑screen players talk from their airbase** (`DeadTalkFromBase`): while waiting to
  respawn, your radio transmits and receives from your faction's base, so you can still coordinate —
  but only with people the base can actually reach. (Previously a dead player's radio transmitted
  from the world origin — effectively from nowhere.)

---

## v0.4.0

Quality‑of‑life and realism update. **Plugin‑only — no protocol changes**, fully compatible
with 0.3.0.

### ✈️ New: in‑cockpit radio readout (HUD)
- Your radios show **on the HUD** near the FUEL gauge — no need to open the F7 panel.
- Shows each radio's **frequency + AM/FM + secure (`S`) flag**, a red **`TX`** when you're
  transmitting, **`JAM`** when you're being jammed, and **who you're receiving**.
- **It only pops up when you're using the radio** — transmit (PTT), change frequency/band, switch
  radios, or receive — then fades out a few seconds later.
- Tunable in the config **MFD** section: `HudAnchor`, `FontScale`, `AutoHide`, `ShowSeconds`,
  `ToggleKey`.

### 📡 New: airborne radio relay (Radome / AWACS)
- An aircraft carrying a **Radome** (e.g. the **EW‑1 Medusa**) flying **up high** acts as a
  **radio relay** for its faction — transmissions hop over mountains and past normal range.
- Friendly relays only. Tunable in **Propagation**: `RelayViaRadome`, `RelayMinAltitudeMeters`,
  `RelayQualityFactor`.

### 🛠️ Fixed: jamming now garbles the jammed transmitter
- Previously, jamming only added static to what the *jammed* player **heard** — a jammed pilot still
  came through **crystal clear** to everyone else.
- Now a jammed transmitter's signal is **garbled for all receivers**. Jamming bites if **either end**
  is jammed.

### Notes
- The **Overlay** (cockpit MFD panel) and **BezelPage** (map menu) display modes are **temporarily
  disabled** while their per‑aircraft placement is reworked — the HUD readout is the active display.
- No changes to the relay server; existing 0.3.0 relays work unchanged.
