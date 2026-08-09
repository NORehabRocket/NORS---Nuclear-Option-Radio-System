# NORS — Nuclear Option Radio System

DCS-SRS–style **positional voice radio** for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/).
The game has no voice comms, so NORS builds the whole stack: a multi-radio cockpit with
AM/FM frequencies, **faction-secure nets driven by the in-game datalink**, realistic
**range falloff, radio horizon, and terrain line-of-sight occlusion**, and live voice.

## Transport: P2P by default, relay optional
- **Steam P2P (default)** — voice goes **directly between players over Steam** (the plugin uses
  Steam's own P2P networking, separate from the game's netcode). Steam handles NAT traversal/relay,
  so there's **no server, no port forwarding, and nothing to configure** — if both players have the
  mod and are in the same game, it just works. Moderation: the **game host** can mute/ban players for
  everyone (and the game's own kick removes them entirely). On a **dedicated server** this needs the
  server to be started with `-socket SteamGameServer`; with the default `-socket UDP` the game never
  sends anyone's Steam ID, so P2P has no address to send to and the relay must be used instead.
- **Relay (optional)** — set `Transport = Relay` to route everyone through a standalone NORS relay
  host instead (central admin GUI + persistent bans + rooms). Better for large/community servers.
  Both ends must use the same transport.

> Status: **v0.7.5**. Verified in-game: voice, terrain/range, faction-secure separation, kick/ban,
> P2P transport, the HUD radio readout, transmitter-side jamming, and Radome relay. See
> [CHANGELOG.md](CHANGELOG.md) for what's new, and [RELEASING.md](RELEASING.md) for the release
> process (NOMNOM rules).

---

## Why it's built this way

The game's networking is **Mirage** (a Mirror fork) and you can't add new RPCs from a mod, so
voice can't ride the game's netcode — exactly why DCS-SRS is "Standalone." But unlike DCS (whose
Lua can't touch audio), a Nuclear Option BepInEx mod runs inside Unity with full .NET. That lets
the plugin do *everything* client-side — mic capture, Opus, its own UDP, and 3D playback — so the
only extra piece is a tiny **relay server** one player hosts (like an SRS server).

```
   ┌─────────────── each player's game ───────────────┐
   │  NORS plugin (BepInEx, in Unity)                  │
   │   • reads faction datalink: who's on your team,   │
   │     everyone's GlobalPosition, your faction id    │
   │   • radios: freq / AM-FM / RX / SECURE / volume   │
   │   • mic ─▶ Opus ─▶ UDP                            │        UDP
   │   • UDP ─▶ Opus ─▶ propagation model ─▶ 3D audio  │◀──────────────▶  NORS relay server
   │   • terrain LOS via Physics.Linecast (game terrain)│   (routes voice by frequency;
   └───────────────────────────────────────────────────┘    one player / VPS hosts it)
```

The relay is deliberately dumb: it routes a transmission to everyone monitoring that frequency
and forwards the datagram verbatim. **All** range/terrain/crypto attenuation is computed
client-side, where the terrain and positions actually live.

---

## Repository layout

| Project | Target | What it is |
|---|---|---|
| `src/NORS.Common` | netstandard2.1 | Wire protocol + packet (en/de)coding, shared by plugin & server |
| `src/NORS.Server.Core` | net8.0 | Relay engine: routing, sessions, ban list, kick/ban API |
| `src/NORS.Server` | net8.0 | Console relay with an interactive admin command line **and a browser admin panel** (default `http://localhost:8700/` — roster, kick/ban/unban, bans list, live log & traffic stats; password login via `--admin-pass` or an auto-generated code printed at startup; `--web-lan` to reach it from other machines, `--web-port`/`--no-web` to tune). Works on headless Linux hosts where the WinForms GUI can't. |
| `src/NORS.ServerGUI` | net8.0-windows | Windowed admin app: live roster + click-to-kick/ban |
| `src/NORS.Plugin` | netstandard2.1 | The BepInEx plugin (radios, audio, networking, UI, propagation) |
| `tools/SmokeTest` | net8.0 | In-process test of routing + kick/ban/unban |

Key plugin modules: `Game/LocalState` (datalink reader), `Comms/RadioSet` + `Comms/RadioPropagation`
(radio model), `Audio/VoiceCapture` + `Audio/VoicePlayback` + `Audio/RadioFx` (audio engine),
`Net/VoiceClient` (UDP), `UI/RadioPanel` (IMGUI), `NorsHub` (orchestrator).

---

## Build & deploy

Requires .NET SDK 8/9 and the game installed (the plugin references the game's managed DLLs).

```powershell
# 1) Deploy the plugin into BepInEx\plugins\NORS  (copies NORS.dll, NORS.Common.dll, Concentus.dll)
.\deploy.ps1                       # or:  .\deploy.ps1 -GameDir "X:\path\to\Nuclear Option"

# 2) Publish the relay server (self-contained; no runtime needed on the host)
.\publish-server.ps1               # Windows: builds GUI + console for win/linux/osx
#   -> dist\server-gui\NORS.ServerGUI.exe        (Windows admin GUI)
#   -> dist\server\<rid>\NORS.Server(.exe)       (console relay, per platform)
# On Linux/macOS hosts, build the console relay natively instead:
#   ./publish-server.sh linux-x64    (or osx-arm64 / osx-x64)
```

> Only `NORS.dll` and `NORS.Common.dll` belong in the plugin folder; `deploy.ps1` copies exactly those.

### Platforms
- **Plugin:** one platform-agnostic managed DLL — it runs wherever the game + BepInEx run: Windows
  natively, and Linux/macOS via Proton/Wine (the same `NORS.dll`, no per-OS build).
- **Relay console (`NORS.Server`):** Windows, Linux, and macOS (x64 + Apple Silicon).
- **Relay GUI (`NORS.ServerGUI`):** Windows only (WinForms). On Linux/macOS, host with the console
  relay — interactively or `--headless` (e.g. under systemd / launchd).

### Host the relay

Two interchangeable front-ends over the same engine — pick one:

**Windowed admin app** (`dist\server-gui\NORS.ServerGUI.exe`): set the port/name, click **Start**,
and you get a live roster (callsign, faction, Steam id, IP, monitored-freq count, idle time), a log
pane, and click-to-**Kick**/**Ban** with an optional reason, plus a **Bans** tab with **Unban**.

**Console** (cross-platform — Windows `dist\server\win-x64\NORS.Server.exe`, Linux/macOS
`./dist/server/<rid>/NORS.Server`, e.g. `--port 5555 --name "My Squadron NORS"`): same engine with
an interactive command line —

```
> list                  show connected clients (with IDs)
> kick  <id|name> [r]    disconnect a client (can rejoin)
> ban   <id|name> [r]    disconnect + ban (persistent)
> unban <steamId|ip>     lift a ban
> bans                   list bans
> stats                  traffic counters
```
Server flags: `--port <n>`, `--name <s>`, `--admin-pass <pw>` (enable remote admin), `--no-votekick`,
`--headless` (run with no console, e.g. as a service). Open **UDP 5555** on the host's
firewall/router. Everyone sets `ServerHost` to the host's IP.

### Multi-room ("one relay, many servers")
The relay is a single **master** server everyone connects to, but it's partitioned into **rooms**: a
room = one game host's session (keyed by the **host's SteamID**). Voice, the player roster, vote-kick
and notices are **scoped to your room**, so people in different game lobbies never hear or moderate
each other even though they share the relay. Whoever **hosts the game is automatically that room's
moderator** — "their own server." (This needs no setup; clients send their room + host flag
automatically.)

### Moderation model
There are three ways to remove a player:

1. **Master operator** — the person running the relay, via the GUI roster or console (`kick`/`ban`).
   This is **global** (across all rooms). Kick disconnects; ban adds to `nors-bans.txt` and refuses
   future joins. There's also optional password remote-admin (`--admin-pass`) for a trusted player
   to do this from in-game.
1b. **Game host (room moderator)** — whoever hosts a game session automatically gets **Kick/Ban
   buttons for the players in their own room** (no password). A host's ban is **room-scoped** (that
   player is blocked from *that* host's sessions, not globally).
2. **Player vote-kick (faction-scoped)** — players can **Vote** next to a *teammate's* name in the
   radio panel (the button only shows for your own faction). When a strict majority of the *other
   same-faction* players have voted (min 2), the target is kicked. Cross-faction votes are refused.
   Votes expire after 45 s, and only your faction sees the vote notices. Disable with `--no-votekick`
   (console) or the GUI checkbox.
3. **Remote admin** — start the relay with an admin password (`--admin-pass <pw>` / GUI field).
   A trusted player puts the same password in `AdminPassword` in their config, hits **Login** in the
   panel, and then gets **Kick/Ban** buttons next to every name — so they can moderate from inside
   the game without alt-tabbing to the server box.

Bans key off the player's **Steam id** (NAT-proof, survives IP changes); if the Steam id isn't
known, they fall back to IP. Unban by Steam id or IP.

---

## In-game usage

| Key (default) | Action |
|---|---|
| `F7` | Toggle the radio panel |
| `T`  | Push-to-talk on the selected radio |
| `Y`  | Cycle which radio you transmit on |
| `,` / `.` | Tune selected radio down / up (25 kHz steps) |

In the panel you set each radio's **frequency**, **AM/FM**, **RX** (monitor) and **SECURE/CLEAR**,
plus per-radio volume, and pick the **TX** radio. The panel also shows connection status, your
mic level, and who you're currently receiving (with signal % and distance).

Config lives in `BepInEx/config/com.dsr.nors.cfg` (editable in-game with ConfigurationManager).
Set `ServerHost`/`ServerPort` there. `AutoConnect` joins the relay when you spawn into a mission.

---

## How the radio model works

- **Faction nets via the datalink.** Your faction is read from the game (`FactionHQ.faction`) and
  hashed into a stable id all teammates share. A **SECURE** transmission is only intelligible to your
  own faction; **CLEAR** is heard by anyone tuned to the frequency (e.g. a guard channel).
- **Positions are cross-client-safe.** Voice carries the transmitter's `GlobalPosition` (the game's
  origin-independent frame), so range/LOS are correct despite each client's floating-origin shifts.
- **Range & radio horizon.** Signal falls off with distance up to the radio's max range, capped by the
  radio horizon `4.12·(√h_tx + √h_rx) km` — so altitude dramatically extends reach. AM reaches farther;
  FM tolerates some blockage and diffracts slightly past the horizon.
- **Terrain line-of-sight.** A `Physics.Linecast` on the game's terrain layers (mask `8256`, the same
  one the game uses for ground-collision warnings) attenuates and adds static when a mountain is in the
  way — heavily for AM/UHF, less for FM.
- **Modulation mismatch** (AM vs FM on the same freq) comes through as garble.
- Signal quality drives the **radio FX**: band-passed voice with additive static that grows as the
  signal weakens, plus soft-clip "crunch."

All thresholds (ranges, horizon factor, terrain mask, static level, secure-by-default) are config knobs.

---

## Testing notes

Verified here: full solution builds; the relay + wire protocol + frequency routing pass an
automated two-client test (`tools/SmokeTest`). Not yet exercised: live microphone capture and
3D playback inside the running game — that path is conventional Unity audio code but needs a real
session to tune jitter-buffer depth, mic latency, and FX levels. Start two clients against one relay
on a LAN to validate, then adjust `OpusBitrate`, `StaticLevel`, and the propagation ranges to taste.

## License

NORS is open source under the [MIT License](LICENSE). Fork it, learn from it,
build on it — a credit back to the DarkSkies project is appreciated. The relay
protocol and the `NorsApi` integration surface are considered stable; PRs welcome.
