# NORS 0.7.9 — Release Notes

Two additions, both asked for on Discord. Nothing here touches the protocol, so 0.7.9 talks to
0.7.5+ clients normally and no server needs updating.

## Per-player volume, remembered between sessions

One player who runs hot has been a moderation problem rather than an audio one. The only lever was
the host's mute/ban, so the answer was "threaten them with a kick". Now anyone can just turn them
down.

Every player gets a slider (0 to 1.5x — boost someone quiet as easily as crush someone loud), a
percentage you click to reset to 100%, and a mute toggle. It is completely local: nothing is
transmitted, the other player is not told, and no moderator rights are needed.

- The controls appear on the **Receiving** rows — for whoever is blasting you *right now* — and on
  the **Players** roster rows beside Ban.
- **It sticks** across restarts, servers and sessions, in
  `BepInEx/config/nors-player-volumes.txt`.
- **It survives a rename.** Levels are keyed to the Steam id where one is known, so somebody
  changing their Steam name keeps the setting instead of quietly returning to full volume.
- Setting someone back to 100% clears their entry, so the file stays a short list of decisions.

Thanks to **Zookers** for the request.

## NORS mark and version on the main menu

A small NORS badge with the running version now sits in a corner of the main menu.

The version is the real point: a stale install is otherwise invisible until something misbehaves
mid-match. Now "which version are you on?" is a glance rather than a conversation. Menu only — it
never appears in flight. `UI/MenuBadge` turns it off, `UI/MenuBadgeCorner` moves it.

## Upgrading

Extract over your game folder as usual, or update through the mod manager. Your config, radios and
keybinds are kept.
