# NORS 0.7.1 — Release Notes

A quick follow-up to 0.7.0, fixing the oldest annoyance in the mod — and
carrying NORS's **first community pull request**.

## Push-to-talk no longer fights the chat box
The old default PTT key (T) is also the game's **chat key** — so opening chat
keyed your radio every time, and typing a message could hit the radio hotkeys
(panel, tuning, TX switch). Now:

- **PTT ships unbound**, and all NORS inputs are **ignored while the chat box
  is open**. Huge thanks to **@Appulcake** for the PR — the first community
  contribution since NORS went open source. o7
- **First-launch setup popup** — new players get a window at the main menu:
  press any key to bind push-to-talk, or quick-pick **Caps Lock**, **~**,
  **Mouse 4** or **Mouse 5**. Bind or skip; it never appears again.
- **Existing players are untouched** — if your config already has a PTT key,
  you keep it and never see the popup.
- The DarkSkies ATC panel's browser push-to-talk stays live even while the
  chat box is open (it's not part of the keyboard conflict).

## Compatibility
Plugin-only update — no relay/server changes, no wire-format changes.
Fully compatible with 0.7.0 clients and relays.
