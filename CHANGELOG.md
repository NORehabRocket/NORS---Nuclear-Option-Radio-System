# NORS — Changelog

## v0.7.2

**Nuclear Option 0.34 compatibility + the chat/radio input fix.** Plugin-only; no
relay or wire-format changes.

### 🎨 Radio panel redesign + in-game passcode control
- **DarkSkies look** — the F7 panel now matches the web panels (dark navy, cyan accents),
  with the transmit radio highlighted green and encrypted radios tinted amber.
- **One compact line per radio** instead of two rows — the six-radio stack is about half
  as tall. Set `UI/CompactRadios = false` for the old roomier two-row layout.
- **Passcodes are now visible and editable in game.** Each radio has a **LOCK** button:
  it lights amber when that channel is encrypted, and opens an inline field to set or
  clear the passcode. Previously a passcode could only be set from the config file or
  the DarkSkies ATC web panel — so a radio could be silently encrypted with no way to
  see it or undo it in game, which looked exactly like "the radio is broken".
  **An empty passcode is an open channel** — everyone on that frequency hears you.
- **"Encrypted traffic you can't decode" warning** — if someone transmits on your
  frequency with a passcode you don't have (or a different one), the panel now says so
  instead of leaving you in silence.
### ⌨️ Chat no longer touches the radio (community-reported)
- **All NORS input stands down while the game's chat box is open** — PTT, panel key,
  cycle-TX, tune keys and the MFD toggle. Typing a `,` or `.` used to retune your
  transmit radio (and `y` switched which radio it was), so after a few chat messages
  you and your wingman were on different frequencies — the classic "only one of the
  default frequencies works" report. Hotkey gating from **@Appulcake**'s PR; extended
  here to the MFD toggle and the radio UI.
- **The radio panel greys out while chat is open** (with a "chat open" note) so stray
  clicks and the admin password field can't eat keystrokes mid-message.
- **New "Reset to defaults" button** on the F7 panel — one click back to your
  configured frequencies if a radio ends up somewhere unexpected.

- **Player names** — 0.34 replaced the `Player.PlayerName` string with a resolved
  name object (`GetNameOrCensored()`); NORS now uses the game's lookup, restoring
  callsigns on the panel, roster and HUD readout.
- **MFD bezel page** — the game's `MFDScreen.label` moved from UGUI `Text` to
  `TextMeshProUGUI`; the optional RADIO page builds against the new type (body text
  now uses a built-in font since a TMP font asset can't feed a UGUI Text).
- Required on game 0.34; older NORS builds error when reading player names there.
## v0.7.1

### ⌨️ Input & first-run (community!)
- **PTT is unbound by default + inputs ignored while chat is open** — T is the game's
  chat key, so the old default keyed the radio on every chat message and typing hit
  the radio hotkeys. Thanks **@Appulcake** for NORS's first community PR!
- **First-launch setup popup** — at the main menu, NORS asks you to pick your PTT key
  (press any key, or quick-pick Caps Lock / tilde / Mouse 4/5). Existing users keep
  their configured key and never see it.
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
