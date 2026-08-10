# NORS 0.7.7 — Release Notes

**The first public build since 0.7.4**, and the big one for anyone who plays on dedicated
servers. 0.7.5 and 0.7.6 were developed but never released on their own, so everything here is
new if you're coming from 0.7.4.

Two headlines: **voice now works on dedicated servers**, and a bug that made players randomly
unable to hear each other is **fixed**.

> Update at your own pace — 0.7.7 talks to older clients exactly as before. Nothing here is a
> flag day.

---

## ⚠ Updating from an earlier version — read this

**Delete your old config file** so the new one is written fresh:

```
BepInEx/config/com.dsr.nors.cfg
```

NORS keeps whatever is already in that file, so upgrading does **not** give you the new defaults.
Several settings changed meaning in this release — most importantly the relay address, which used
to default to `127.0.0.1:5555`. Left as-is, that points voice at your own PC and the automatic
server discovery can never work.

NORS will try to correct that one pair for you on first launch and says so in the log. But if
anything behaves oddly after updating, **delete the file and restart** — that is the clean fix,
and everything regenerates.

Deleting it does reset your settings, so note these down first if you've changed them:

- your **push-to-talk key** (you'll be asked to bind one again on next launch)
- any **radio frequencies** you've customised
- any **channel passcodes** — these are shared with your group and worth copying somewhere safe
- mic device, gain and volume

---

## 1. "Some of us can hear each other and some can't" — fixed

Every radio transmits as faction-secure by default, and every incoming transmission was checked
against your faction id before you could hear it. That id was the faction HQ's **network id** —
a value the engine assigns when the object spawns, not a fixed property of the faction. Two
clients could end up holding different ids for the *same* faction after a mission reload, a
respawn, or simply joining in a different order. The moment that happened, those two players were
permanently deaf to each other, with nothing in the log or the panel to say why. It also stamped a
blank id while you sat in the spawn menu, which nobody else matched.

That's why it looked random, why it was worse on dedicated servers than in player-hosted lobbies,
and why it started with the encryption update.

NORS now keys faction-secure on the faction's **Encyclopedia index** — the same content-defined
value the game itself uses to identify a faction on the wire. It's built from an ordered asset
list on every client, so it agrees without being replicated at all.

- A mismatch is **never silent again** — the panel names both ids.
- An unknown id **no longer mutes you**; audio is dropped only when both sides are known and
  genuinely differ.
- **Backward compatible.** The new id rides as an extra field older clients skip, so any two
  people on 0.7.7 get the fix and mixed pairs behave as they do today.

Thanks **Zookers**, **Critzlez** and **Tarragon**.

## 2. Dedicated servers can host voice themselves

Install NORS on the dedicated server and it runs the voice relay **inside the game server**.
Players send one copy of their audio to the server they're already connected to, and the server
fans it out.

- **Nothing for players to configure** — no IP to type. The client works out whether the server
  hosts voice and uses it, falling back to direct P2P when it doesn't.
- **Several servers on one machine just work.** Each derives its own voice port from its game
  port (7777 → 8777, 7778 → 8778), so they never clash, and players on one server can't hear
  players on another.
- **Nothing extra to deploy** — same folder, same install, one UDP port.

Voice hosting is **opt-in** — set `Server/HostVoiceRelay = true` on the server. Playing clients
never open a voice port whatever their config says; the server path only runs with the game's
`-DedicatedServer` flag.

Operators: see **SERVER-GUIDE.md** in the download, including a Docker compose example.

This also matters on busy servers. NORS sends uncompressed audio, roughly 280 kbit/s per
listener, and in peer-to-peer the person talking uploads a separate copy to *everyone*. At 20
players that's ~5 Mbit/s of upstream from whoever keys the mic; through the server it's a flat
280 kbit/s.

## 3. Voice transport is now chosen automatically

`Transport` defaults to **Auto**. On joining, NORS asks whether the server hosts voice and uses
direct Steam P2P if it doesn't — you no longer need to know which one your server supports. The
radio panel shows which it picked, and says so plainly if neither works. `P2P` and `Relay` still
force the old behaviour.

## 4. Voice moderation works on dedicated servers

Mute/ban was host-authoritative, and a dedicated server **has no host player** — so nobody could
issue a ban and nobody would have honoured one. Moderation was silently unavailable on exactly
the servers that need it.

- New `Moderation/Moderators` — Steam ids who may mute/ban for everyone.
- **You only obey people on your own list**, so nobody can silence a server just by joining it.
- The game host stays trusted automatically in player-hosted lobbies.
- Bans are **voice-only**: a muted player keeps flying and keeps using chat.

## 5. Shared bans between your servers

Point several servers at one ban file (`Server/SharedBanFile`) and a ban on any of them applies
to all of them within a few seconds. No central service, nothing to authenticate. In Docker it's
one shared bind mount. Anyone already connected who gets banned elsewhere is removed immediately.

## 6. "TX lights up but nobody hears me" — the cause, explained

If your dedicated server runs the default `-socket UDP`, the game never sends players' Steam IDs,
so peer-to-peer voice has no address to send to. That is a property of the server, not a NORS
bug, and no client-side mod can work around it — which is exactly why NORS can now host voice on
the server instead. The panel tells you which situation you're in rather than failing silently.

## 7. Unbound push-to-talk is no longer a silent failure

Push-to-talk still ships **unbound**, and that's deliberate — there is no key worth choosing on
your behalf. `T` fights the game's chat box, and Caps Lock toggles caps every time you key the
mic, so you end up typing in shout case afterwards. You pick your own.

What changed is that it can no longer strand you without saying so:

- The first-launch popup asks you to bind one; **Skip** leaves it unbound and says so in the log.
- While PTT is unbound the radio panel shows a **red warning with one-click bind buttons**.
- The setup popup **re-arms on every launch** until a key is actually set, so anyone stranded by
  an earlier version gets asked again.

---

## Compatibility

Plugin and server only — no protocol bump. Interoperates with every 0.7.x client: the new faction
id is an additive field older builds skip. Requires Nuclear Option 0.34.x; builds before 0.7.4
break on the game's changed player-name API.

## Known limitations

- Shared bans cover servers that can see the same filesystem (one machine, or one Docker host).
  Servers in different datacentres need a central service, which is separate work.
- On a server running `-socket UDP`, bans can only match on IP, because the game never provides
  Steam IDs there. `-socket SteamGameServer` gives you durable Steam-ID bans if you need them.
