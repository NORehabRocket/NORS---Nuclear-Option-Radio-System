# NORS — Dedicated Server Guide

**For server operators.** How to make a Nuclear Option dedicated server carry its own players'
voice, with nothing for players to configure and no extra service to deploy.

Requires **NORS 0.7.7 or newer** on the server and on players' clients. Players need no
configuration at all — the client works out whether to use your server's voice or direct P2P.

---

## TL;DR

1. Install BepInEx on the dedicated server, same as a client.
2. Drop the `NORS` folder into `BepInEx/plugins/`.
3. Start the server once, then stop it (this writes the config file).
4. In `BepInEx/config/com.dsr.nors.cfg`, set **`HostVoiceRelay = true`** under `[Server]`.
   It ships **off** — hosting voice is something you opt into, not something that happens to you.
5. Open **UDP `<your game port> + 1000`** in the firewall. Game on `7777` → open `8777`.
6. Start the server. That's it — players running 0.7.7+ find it automatically.

Nothing to install per-player, no IP for anyone to type, no separate relay container.

---

## How it works

### The problem it solves

Steam peer-to-peer voice needs every player's Steam ID. The game only shares those when the
server authenticates over Steam — that is, when it was launched with `-socket SteamGameServer`.
On the **default `-socket UDP`**, players' Steam IDs are never transmitted at all, so P2P voice
has no address to send to: the transmit indicator lights up and nobody hears anything. No
client-side mod can fix that, because the information never crosses the wire.

Even where P2P *does* work, it scales badly on a busy server. NORS sends uncompressed 16 kHz
audio, roughly **280 kbit/s per listener**, and in P2P the person talking uploads one copy to
every single listener:

| Players | Upstream from whoever keys the mic (P2P) | Through the server |
|---|---|---|
| 5 | ~1.1 Mbit/s | 280 kbit/s |
| 10 | ~2.5 Mbit/s | 280 kbit/s |
| 20 | ~5.3 Mbit/s | 280 kbit/s |
| 40 | ~11 Mbit/s | 280 kbit/s |

On a 30-slot public server that asks a pilot's home connection for more upload than most people
have, and the person who suffers is the one talking.

### What NORS does instead

When the plugin loads and sees `-DedicatedServer` on the command line, it skips its entire
client side — no microphone, no radio panel, no local player, no Steam client — and instead
starts a **voice relay inside the game server process**. Players send one copy of their audio to
the server, and the server, which has the bandwidth, fans it out to whoever is listening on that
frequency.

Clients find it with no configuration: they already know the address and port of the game server
they're playing on, so they look for voice at that same address on **game port + 1000**. If a
relay answers, they connect. If nothing answers, they fall back to whatever they had configured.

Everything else about NORS is unchanged — frequencies, ranges, terrain blocking, encryption.
Only the path the audio takes is different.

---

## Installing

### 1. BepInEx on the server

The server needs BepInEx installed exactly like a client does — same version, same doorstop
setup. NORS is a normal BepInEx plugin; it just behaves differently when it detects a dedicated
server.

### 2. The plugin

Extract the release so you end up with:

```
<server install>/BepInEx/plugins/NORS/
    NORS.dll
    NORS.Common.dll
    NORS.Server.Core.dll     <-- the relay; the server will not host voice without it
```

All three DLLs must be present.

### 3. First run

Start the server once and stop it. BepInEx writes:

```
BepInEx/config/com.dsr.nors.cfg
```

### 4. Firewall

Open **UDP** on your game port + 1000:

| Game server port | Open this UDP port |
|---|---|
| 7777 (default) | 8777 |
| 7778 | 8778 |
| 27015 | 28015 |

The offset is 1000 rather than 1 deliberately, so that operators running servers on consecutive
ports don't end up with one server's voice port colliding with the next server's game port.

---

## Configuration

Everything below is in `BepInEx/config/com.dsr.nors.cfg`. The whole `[Server]` section is
**ignored unless the process was launched with `-DedicatedServer`**, so the same config file is
harmless on a client.

### `[Server]`

| Setting | Default | What it does |
|---|---|---|
| `HostVoiceRelay` | **`false`** | **Set to `true` to host voice on this server.** Off by default so no port is ever opened without you asking. Ignored completely on a normal client. |
| `RelayPort` | `0` | `0` derives the port from the game port (+1000). **Leave it at 0** — a fixed port means every player has to set `ServerPort` by hand. |
| `RelayName` | *(blank)* | Name players see when they connect. |
| `AdminPassword` | *(blank)* | Lets a trusted player unlock voice kick/ban from their radio panel. Blank disables remote moderation. |
| `VoteKick` | `true` | Let players vote someone off voice. |
| `SharedBanFile` | *(blank)* | Path to a ban list shared with your other servers, so a ban on one applies to all. See [Shared bans](#shared-bans-across-several-servers). |

### `[Moderation]`

| Setting | Default | What it does |
|---|---|---|
| `Moderators` | *(blank)* | Steam IDs, comma separated, who may mute/ban for everyone. |

Example:

```ini
[Server]
HostVoiceRelay = true      # <-- the one you must change
RelayPort = 0
RelayName = CritzOS #1
AdminPassword = something-long-and-not-shared

[Moderation]
Moderators = 76561198000000001, 76561198000000002
```

---

## Running several servers on one machine

This is handled automatically. Each server derives its own voice port from its own game port, so
they never collide, and a player only ever reaches the relay belonging to the server they are
actually on:

```
Server A   -port 7777   →  voice on 8777
Server B   -port 7778   →  voice on 8778
Server C   -port 7779   →  voice on 8779
```

Players on Server B cannot hear players on Server A. On top of the separate ports, each server's
players are also placed in a distinct voice "room" derived from the server's address and port —
so even if you deliberately point several game servers at one shared relay, their audio stays
separate.

Open each derived port in the firewall.

---

## Docker

The relay binds `0.0.0.0` inside the container, so it works normally — but there are three
things to get right.

### 1. Publish the voice port, as UDP

The port a client looks for is derived from the game port **as the player sees it** — that is,
the *published* port. The port the relay binds is derived from the game port **inside the
container**. Keep those the same and there's nothing to think about:

```yaml
ports:
  - "7777:7777/udp"   # game
  - "8777:8777/udp"   # voice = game port + 1000
```

If you remap (container listens on 7777 but you publish 7778), you must remap voice the same
way — published `7778+1000` to container `7777+1000`:

```yaml
ports:
  - "7778:7777/udp"   # game   (published : container)
  - "8778:8777/udp"   # voice  (published+1000 : container+1000)
```

**Simplest rule: give each container a `-port` equal to the port you publish it on.** Then every
mapping is `X:X` and `X+1000:X+1000`, and there's no arithmetic to get wrong.

Note that because each container has its own network namespace, internal ports never collide
between containers — only the *published* ones have to be unique, and the +1000 derivation
guarantees that as long as your game ports differ.

### 2. Persist `BepInEx/config` — or you will lose your bans

The ban list and moderator list live in `BepInEx/config`. If that isn't on a volume, **every
container recreate wipes them.** Mount it:

```yaml
volumes:
  - ./server1/config:/game/BepInEx/config
  - ./server1/plugins:/game/BepInEx/plugins
```

### 3. Make sure the entrypoint actually starts BepInEx

On Linux, BepInEx needs its doorstop launcher (`run_bepinex.sh`, or the equivalent
`DOORSTOP_ENABLE` / `LD_PRELOAD` environment). If your image runs the game binary directly, the
plugin never loads and NORS will be silently absent. The give-away is no `[Info : NORS]` lines at
all in the container log.

### 4. Share one folder if you want shared bans

See [Shared bans](#shared-bans-across-several-servers) below. In Docker it's one extra bind mount
pointing every container at the same host directory.

### Example: two servers, two containers, shared bans

```yaml
services:
  no-1:
    image: yourorg/nuclear-option-server:latest
    restart: unless-stopped
    command: ["-DedicatedServer", "-port", "7777", "-mission", "Free Flight"]
    ports:
      - "7777:7777/udp"    # game
      - "8777:8777/udp"    # NORS voice = game + 1000
    volumes:
      - ./no-1/config:/game/BepInEx/config
      - ./no-1/plugins:/game/BepInEx/plugins
      - ./shared:/shared              # <-- same host folder in both containers

  no-2:
    image: yourorg/nuclear-option-server:latest
    restart: unless-stopped
    command: ["-DedicatedServer", "-port", "7778", "-mission", "Free Flight"]
    ports:
      - "7778:7778/udp"
      - "8778:8778/udp"
    volumes:
      - ./no-2/config:/game/BepInEx/config
      - ./no-2/plugins:/game/BepInEx/plugins
      - ./shared:/shared
```

With, in **both** containers' `com.dsr.nors.cfg`:

```ini
[Server]
SharedBanFile = /shared/nors-bans.txt
```

Players on `no-1` and `no-2` cannot hear each other — separate ports and separate voice rooms —
but a ban on either applies to both.

---

## Shared bans across several servers

Point every server at **one ban file** and bans sync between them. No central service, nothing to
authenticate, nothing to expose:

```ini
[Server]
SharedBanFile = D:\NuclearOption\shared\nors-bans.txt     # Windows
SharedBanFile = /srv/nuclear-option/shared/nors-bans.txt  # Linux
SharedBanFile = /shared/nors-bans.txt                     # Docker bind mount
```

Leave it blank and each server keeps its own private list in
`BepInEx/config/nors-server-bans.txt`.

**How it behaves**

- A ban on any server applies on all of them **within about three seconds**.
- Anyone already connected who becomes banned elsewhere is **removed immediately** — a ban that
  only takes effect on their next reconnect isn't much of a ban.
- Unbans propagate the same way.
- Two moderators banning on different servers at the same moment both stick. Writes take an
  exclusive lock and re-read before modifying, so nothing is silently lost.
- You can **edit the file by hand** while servers are running; changes are picked up on the next
  poll. Malformed lines are skipped rather than breaking the list.
- **Room-scoped bans stay scoped.** A ban issued for one server's room only applies there, even
  though the file is shared. Global bans (room `0`) apply everywhere — that's what you get from
  an operator ban with the admin password.

**Where it works**

- Several servers on **one machine** — fully supported.
- Several **Docker containers** on one host, via the same bind mount — fully supported, this is
  the same filesystem.
- A **network share** (SMB/NFS) — works, but file locking over a share is less dependable than
  local. If two servers on different machines ban at the exact same instant, one write can fall
  back to applying locally only. Fine in practice; if you want strictness, keep the file local to
  one host.

**What it does not do**

This syncs bans between servers that can see the same filesystem. Servers in different
datacentres need the central service, which is separate work.

---

## Moderation

Voice bans are **voice-only**. A banned player keeps flying and keeps using text chat; they just
can't be heard. Nothing NORS does can kick anyone from the game itself.

There are two ways to grant moderator rights, and you can use both:

**Steam ID list (preferred).** Put your staff's Steam IDs in `Moderation/Moderators`. They get
Ban/Unban buttons next to every name in their radio panel. This is the stronger option: a Steam
ID can't be leaked or shared, and only whoever edits the config can change the list.

**Admin password.** Set `Server/AdminPassword`, and a trusted player types it into their radio
panel to unlock the same controls. Convenient, but passwords get pasted into Discord and outlive
the person you gave them to — prefer the ID list where you can.

> Note on the ID list: it works by each client deciding whose bans it honours. That means players
> should have your staff's IDs in *their* config too — normally you'd ship that in your modpack.
> The server's own `AdminPassword` path works regardless of what players have configured.

### Ban storage

Bans live in `BepInEx/config/nors-server-bans.txt`, one per line:

```
# NORS ban list  —  steamId|ip|name|reason|utc|room (room 0 = global)
76561198000000009|203.0.113.7|Loudmouth|mic spam|2026-08-09T21:04:00.0000000Z|0
```

Bans prefer the **Steam ID** because it survives reconnects and IP changes, and fall back to IP
when no Steam ID is known. You can edit or seed this file by hand while the server is stopped.

> **Important:** on a server running the default `-socket UDP`, the game never gives anyone a
> Steam ID, so bans there can only match on **IP** — which a player defeats by reconnecting. If
> you want durable, portable bans, launch with `-socket SteamGameServer` (see below).

---

## Should I also use `-socket SteamGameServer`?

It's optional, and independent of everything above. It changes how the game authenticates
players:

| | Default `-socket UDP` | `-socket SteamGameServer` |
|---|---|---|
| Voice via the in-process relay | ✅ works | ✅ works |
| Auto-discovery (no player config) | ✅ works | ❌ clients can't see the address, so they need `ServerHost` set |
| Steam-ID bans | ❌ IP only | ✅ durable |
| Steam auth on join | ❌ | ✅ |
| In-game server browser listing | ❌ | ✅ |

So: **keep the UDP socket** if you want the zero-configuration experience for players. **Switch
to Steam** if enforceable bans and Steam authentication matter more. You don't need it for voice
either way.

---

## Checking that it works

### On the server

Look in the BepInEx console or `BepInEx/LogOutput.log` for:

```
[Info : NORS] NORS is hosting voice for this server on UDP 8777 (game port 7777).
              Clients running NORS 0.7.7+ find it automatically — nobody needs to type an address.
[Info : NORS] NORS relay: Relay 'CritzOS #1' listening on UDP 8777.
```

As players join you'll see:

```
[Info : NORS] NORS relay: + Nomad joined (203.0.113.7:51234) room 14235... Clients: 3
```

If you see nothing from NORS at all, BepInEx isn't loading the plugin. If you see the plugin load
but no relay line, check `HostVoiceRelay` and that `NORS.Server.Core.dll` is present.

### On a client

Press **F7**, then the **diag** toggle at the bottom of the panel. It shows the transport, the
port it connected to, and the peer count.

---

## Troubleshooting

**NORS loads but never mentions the relay.** `HostVoiceRelay` is still `false` — that's the
default. The log tells you so on startup. Set it to `true` under `[Server]` and restart.

**Nothing from NORS in the server log at all.** BepInEx isn't loading. Check the doorstop install
and that `BepInEx/plugins/NORS/` contains all three DLLs.

**"NORS could not start the voice relay: ... address already in use."** Something else has that
port, or you started two servers on the same game port. Change one server's `-port`.

**Players connect but hear nothing.** Check the derived UDP port is actually open — this is the
most common cause. Voice is UDP, not TCP.

**Players on different servers hear each other.** Shouldn't be possible with derived ports. If it
happens, one of the servers has `RelayPort` pinned to a fixed number — set it back to `0`.

**A player says the panel shows "P2P can't reach anyone".** They're still on P2P rather than the
relay; see the note below.

---

## Current status — read this before rolling out

The server side described above is complete, and the relay code is the same code that has been
running as NORS's standalone relay. Two honest caveats:

**Players need to change nothing.** `Transport` defaults to `Auto`: on joining, NORS asks your
server whether it hosts voice and uses it if so, falling back to direct Steam P2P otherwise. The
radio panel shows which one it picked. Players only ever touch `Transport` if they want to force
one or the other.

**This has not yet run inside a live dedicated server.** The relay logic, the derived ports,
the room separation and the discovery handshake are all covered by automated tests, including two
relays on one machine proving players on different servers can't hear each other. But the
in-process path has not been exercised on a real running game server yet. If you hit something,
the log lines above are the place to start, and `HostVoiceRelay = false` cleanly disables it.
