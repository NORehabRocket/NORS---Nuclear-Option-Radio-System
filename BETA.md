# NORS beta / development channel

Two repos, one local checkout, two git remotes.

| Remote   | Repo                                   | Branch | Who sees it |
|----------|----------------------------------------|--------|-------------|
| `origin` | `NORS---Nuclear-Option-Radio-System`   | `main` | Everyone. The NOMNOM mod manager polls this one. |
| `beta`   | `NORS-Beta`                            | `dev`  | Testers who are given the link. The mod manager never looks here. |

## What actually reaches players

The registry (`KopterBuzz/NOMNOM`) runs `Run-AutoUpdates.ps1` hourly at minute 22. For
each manifest with `autoUpdateArtifacts: "True"` it reads the GitHub releases of the
repo named in `githubOwner` / `githubRepoName` -- for NORS that is
`com.dsr.nors.json` -> `NORS---Nuclear-Option-Radio-System`.

Three consequences worth knowing:

1. **Pushing code reaches nobody.** Not even to `main`. Only a *published release*
   is ever read. Branches are free.
2. **Drafts are skipped** (`if ($release.IsDraft){continue}`). A draft release is a
   safe staging area on the live repo.
3. **`NORS-Beta` is invisible to the registry** because the manifest does not name
   it. Nothing published there can reach a mod-manager user, however broken.

## Why beta builds do not live on the live repo

GitHub pre-releases *are* ingested by the registry, tagged `category: "preRelease"`
for users who opt in. Tempting, but the version parser strips the suffix:

```powershell
$version = [version]($release.TagName -replace "v|\-pre|_IL2CPP","")
```

So `0.7.9-pre` is recorded as version `0.7.9`. New artifacts are only added when
`$version -gt $artifacts[0].version`. Publish `0.7.9-pre`, then later publish the
real `0.7.9`, and the stable release is **silently skipped forever** -- it is not
greater than what is already there. No error is raised anywhere. The registry would
keep serving the beta build as if it were the release.

Separate repo, separate version namespace, trap avoided.

## Daily work

```bash
git checkout dev
# ...work...
git push                # goes to beta/main, reaches no player
```

## Cutting a beta build for testers

```bash
git checkout dev
./package-plugin.ps1
```

Publish a release on `NORS-Beta` with the zip attached. Version it however you like
-- `0.8.0-beta3`, whatever -- the numbering here is independent of the live channel
and can never collide with it. Hand testers the link.

## Promoting a beta to stable

```bash
git checkout main
git merge dev
# bump the version in NorsPlugin.cs, meta.json, both .csproj, README.md, CHANGELOG.md
./package-plugin.ps1
git push origin main
```

Then cut the release on `origin` **draft first**: create the draft, upload
`dist/1NORS.zip`, verify the asset shows `state=uploaded` with the right size, and
only then flip `draft: false`. Drafts are invisible to the registry, so a failed
upload can never leave a broken release live. Pin the release to the exact commit
you built from, or GitHub tags whatever `main` happens to point at.

Within the hour the registry picks it up and computes its own hash.

## Rule zero

**Never modify an asset on a release that is already published.** The registry
records the asset hash; replacing the file makes every mod-manager user's hash check
fail, and it fails silently on their end. Ship a new version instead.
