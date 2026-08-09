# NORS — Changelog

## v0.7.7

**First public build since 0.7.4.** 0.7.5 and 0.7.6 were developed but never released on their
own, so everything in all three sections below is new if you are coming from 0.7.4.

Headline: voice now works on dedicated servers — properly, and without anyone configuring
anything — and a bug that made players randomly unable to hear each other is fixed.

### 🔀 Voice transport is chosen automatically
`Transport` now defaults to **Auto**: on joining a server NORS asks whether it hosts voice, and
uses direct Steam P2P if it doesn't. Nobody has to know which one their server supports.

- Prefers the **server relay** when both work — NORS sends uncompressed audio, and in P2P the
  person talking uploads one copy per listener, so relay is the difference between working and
  not on anything larger than a small lobby.
- Falls back to P2P after ~4 s if nothing answers, and **never interrupts working P2P** to go
  looking for a relay.
- Re-checks periodically if neither works, so a server that installs NORS later is picked up
  without a restart. Also recovers if the relay dies mid-session.
- The panel shows which one it chose. If neither works it says so plainly, with what to do about
  it — silence and success must not look the same.
- `Transport = P2P` or `Relay` still force the old behaviour exactly.

### 🔗 Shared bans between servers on one machine or network
Point several servers at one ban file (`Server/SharedBanFile`) and a voice ban on any of them
applies to all of them within ~3 seconds — no central service, nothing to authenticate, nothing
extra to expose. In Docker it's one bind mount shared between containers.

- Anyone already connected who gets banned elsewhere is **removed immediately**, not on their
  next reconnect.
- Concurrent bans from two servers both stick: writes take an exclusive lock and re-read before
  modifying, so nothing is silently lost. Verified with 50 simultaneous bans across two lists.
- The file can be **edited by hand** while servers run; malformed lines are skipped.
- Room-scoped bans stay scoped to their own server even though the file is shared.

### 🛡️ Voice moderation now works on dedicated servers
NORS's P2P mute/ban was host-authoritative on both ends — the host broadcast its ban set,
and clients only honoured a list arriving from the host's Steam id. On a **dedicated
server there is no host player**: `NetworkManagerNuclearOption` sets `IsHostPlayer` from
`networkPlayer.IsHost`, and the host connection there is the server process itself. So
nobody could send a ban list and nobody would have honoured one — voice moderation was
silently unavailable on exactly the servers that need it.

Authority is now a **trust list** rather than a network position:

- New `Moderation/Moderators` config — Steam ids, comma separated. Anyone listed can
  mute/ban for everyone, from the F7 panel, on any server type.
- **You only obey people on your own list.** Nobody can silence a server just by being
  in it; they have to already be trusted by the people who'd be affected. Communities
  ship their staff ids in the modpack and it's seamless for players.
- The **game host stays trusted automatically** in player-hosted lobbies, so those
  behave exactly as before with no configuration.
- Several moderators' lists are **unioned**, so two people moderating at once add up
  instead of overwriting each other. Revoking someone's trust drops their bans at once.
- A ban list from an untrusted sender is refused **and logged**, rather than ignored
  quietly.

## v0.7.6 (developed but never released on its own)

**Fixes "some people can hear each other and some can't" on secure channels.**

### 🔑 Faction-secure channels compared the wrong thing
With `FactionSecureByDefault` on (the default), *every* radio transmits as
faction-secure, and every receive was gated on this:

```csharp
if (v.FactionId != _local.FactionId) return;   // silent drop
```

…where `FactionId` was **the faction HQ's Mirage NetId**. A NetId is per-spawn runtime
state, not a content identity, so two clients could hold different values for the same
faction — after a mission reload, a respawn, or just a different join order. When that
happened the two players went permanently and *silently* deaf to each other, with no log
line and nothing in the panel. It also stamped `0` while your HQ was unresolved (spawn
menu), which nobody else matched. That is the "50-50 on dedicated, fine on player-hosted"
report: one authoritative host and a tight spawn window hides it; a long-running
dedicated server does not.

- **Now keyed on the faction's Encyclopedia lookup index** — the same content-defined
  value the game itself puts on the wire for a faction (`INetworkDefinition`). It is
  derived from an ordered asset list on every client, so it needs no replication to
  agree, and survives respawns, mission reloads and join order.
- **A mismatch is never silent again.** The panel names both ids and says whether it
  compared the reliable id or fell back to the legacy one.
- **An unknown faction id no longer drops audio.** It only drops when *both* sides are
  known and actually differ — being in the spawn menu can't mute you any more.
- **Fully backward compatible.** The stable id rides as a trailing field, so 0.7.5 and
  older clients ignore it and keep working off the legacy id exactly as before. No
  protocol bump, no flag day. Two 0.7.6 clients get the fix; a 0.7.6 and a 0.7.5 client
  behave as they do today. Verified by round-tripping packets through a byte-exact copy
  of the 0.7.5 reader.
- The F7 `diag` toggle now shows transport, faction id, Steam id, peer count and
  secure-by-default state — enough to diagnose this from one screenshot.
- Found from reports by **Zookers**, **Critzlez** and **Tarragon**.

## v0.7.5 (developed but never released on its own)

**Two silent failures fixed, and the real cause of "P2P is broken on dedicated
servers" identified — it's a one-flag server setting, not a NORS limitation.**

### 📡 "TX lights up but nobody hears me" — cause found
It was never "dedicated servers" as a category. The **server** decides how clients
authenticate based on its own socket factory:

- Started with **`-socket SteamGameServer`** → clients authenticate through Steam,
  `AuthData.FromSteam` sets `BasePlayer.SteamID`, it replicates to everyone, and
  **P2P voice works normally with no relay**.
- Started with the **default `-socket UDP`** → `AuthData.FromUdp`, `SteamID` is never
  set, so every remote `Player.CSteamID` is 0. Steam P2P has no address to send to, so
  TX lights and no audio moves. **No client-side mod can fix this** — the ID never
  crosses the wire in any form.

So the fix for a UDP server is one launch flag on the server, which also gets it Steam
authentication, ban-by-SteamID, and a Steam server-browser listing.

- NORS now **detects which socket the session is on** and says exactly that in the F7
  panel: either "ask the operator for `-socket SteamGameServer`" or "use Transport =
  Relay", instead of a vague "P2P can't reach anyone".
- Same explanation goes to the BepInEx log.
- Reported by Lomb(otomy), Zookers and Wheat.

### 🎙️ Push-to-talk can no longer be left unbound
**Fixes "nobody can hear each other".** Shipping PTT unbound (0.7.1) meant anyone who
skipped the first-launch popup was silently unable to transmit, with no indication.

- **PTT defaults to Caps Lock** — no chat-key conflict, works out of the box.
- The setup popup's skip option **binds Caps Lock instead of leaving it unbound**.
- **Red "PUSH-TO-TALK IS NOT BOUND" warning** in the F7 panel with one-click binds
  if it's ever None.
- **Auto-rescue**: a config with no PTT key re-arms the setup popup on next launch
  regardless of what was answered before, and logs a warning.

## v0.7.4

**Nuclear Option 0.34.x compatibility.** The game moved its player-name API again
(`GetNameOrCensored()` → `GetDisplayName(PlayerNameContext)`); NORS follows it, and
all name lookups now go through one helper so the next rename is a one-line fix.
No relay, wire-format or feature changes — pairs with DarkSkies ATC 0.7.4.
## v0.7.3

**Linux/Proton install fix.** Plugin code is unchanged from 0.7.2 — this release
exists purely because the *archive* was malformed.

### 📦 Fixed: the download itself was a malformed zip (Linux/Proton especially)
- Release archives were built by Windows PowerShell's `Compress-Archive`, which writes
  entry names with **backslashes** — the ZIP spec requires `/`. On Linux/Proton a
  backslash is a legal filename character, so the archive extracted as one file literally
  named `NORS\NORS.dll` instead of a `NORS` folder — no `NORS.Common.dll` where the
  plugin expects it, so the mod couldn't load. Windows extractors mostly papered over it,
  which is why it only showed up for some people.
- Packaging now writes entry names itself (always `/`) and **verifies** every archive
  before release (`tools\ZipHelper.ps1`). If a zip ever regresses, packaging fails loudly.
- Thanks **Lomb(otomy)**, **Wheat**, **nat** and **Maelle** — Maelle pinned the exact cause.

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
