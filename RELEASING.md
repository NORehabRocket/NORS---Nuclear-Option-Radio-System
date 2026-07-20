# Releasing NORS (NOMNOM rules)

NORS is distributed through **NOMNOM** (Nuclear Option Managed & Neatly Organised Manifest), which
mod managers use to download mods. NOMNOM auto-discovers new GitHub Releases on this repo
(`autoUpdateArtifacts: true`) and records a **sha256 hash of the release asset** in its manifest.

## The one golden rule

> **Never edit, replace, or re-upload an asset on a release that's already published.**

NOMNOM has already hashed that asset. If you swap the file — even for a one-line fix — every mod
manager download will fail with a **hash mismatch**. There is no "quiet quickfix" of an existing
release. A fix, however small, is a **new version + new release**.

Also, from the NOMNOM requirements:
- **One downloadable asset per release** (ours is a zip).
- The asset file name is registered in the manifest — for NORS it must be exactly **`1NORS.zip`**.
- The release **tag must be parseable semver** (e.g. `0.5.0` or `v0.5.0`).

## Normal release checklist

1. **Bump the version** (all five places must match):
   - `meta.json` → `"version"`
   - `src/NORS.Plugin/NORS.Plugin.csproj` → `<Version>`
   - `src/NORS.Common/NORS.Common.csproj` → `<Version>`
   - `src/NORS.Plugin/NorsPlugin.cs` → the `[BepInPlugin(...)]` attribute **and** the startup log line
   - `README.md` → the Status line
2. **Update `CHANGELOG.md`** — add a new `## vX.Y.Z` section at the top. (The release body on GitHub
   is usually pasted from here.)
3. **Build + package:** run `package-plugin.ps1`. It builds Release and produces:
   - `dist/NORS-<version>.zip` (versioned archive for your records)
   - `dist/1NORS.zip` ← **this is the file you upload** (NOMNOM's registered asset name)
4. **Sanity-check the zip:** it must contain a single top-level `NORS/` folder with exactly
   `NORS.dll`, `NORS.Common.dll`, `meta.json` (+ README/CHANGELOG). No extra DLLs — netstandard
   facade shims break the game's Mono runtime.
5. **Quick in-game smoke test** with the freshly deployed build (`deploy.ps1`): F7 panel opens,
   voice TX works (MicMonitor sidetone is enough solo).
6. **Tag + publish the GitHub Release:**
   - Tag = the version, e.g. `0.5.0` (keep the same tag style every time).
   - Title: `NORS v0.5.0`; body: paste the changelog section.
   - Attach **one** asset: `dist/1NORS.zip`.
   - Publish.
7. **Verify NOMNOM picked it up** (it self-updates on a schedule): check the manifest entry for
   `com.dsr.nors` shows the new `version`, a `downloadUrl` pointing at the new tag, and a fresh
   `hash`. Then update in the mod manager and confirm it installs.

## Quickfix ("oops") procedure

You published `0.5.0` and immediately found a bug:

1. **Do NOT touch the 0.5.0 release or its asset.** (Golden rule.)
2. Fix the bug, bump to **`0.5.1`** (all five places), add a short changelog entry.
3. `package-plugin.ps1` → new `dist/1NORS.zip`.
4. New tag `0.5.1`, new release, upload the new asset.
5. If the broken version is genuinely harmful (crashes the game, breaks saves), you may **delete the
   bad release + tag entirely** — deleting a whole release is safe for NOMNOM (the entry disappears);
   *modifying* one is what breaks it.

## Version numbering

- **Patch** (`0.5.0 → 0.5.1`): bug fixes only, no new features — the quickfix case.
- **Minor** (`0.4.0 → 0.5.0`): new features/config options. This is most NORS releases.
- Voice/wire compatibility is governed by the **protocol version** (`NorsProtocol.Version`), not the
  mod version. If a change bumps the protocol, say so loudly in the changelog: old and new clients
  reject each other, and relay-server operators must update too.
