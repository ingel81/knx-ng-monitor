---
name: release
description: Cut a new versioned release of knx-ng-monitor — bumps README, commits, tags `vX.Y.Z`, pushes both, and links the GitHub Actions release workflow. Use whenever the user says "release", "tag a release", or "publish vX.Y.Z".
argument-hint: "[major|minor|patch] or [vX.Y.Z]"
---

# Release skill

Releases for this repo are triggered by pushing a `v*` git tag. The workflow
in `.github/workflows/release.yml` then builds 6 self-contained binaries
(linux/win/macOS × x64/arm64), publishes a Docker image
`ingel81/knx-ng-monitor:<version>` + `:latest`, and creates a GitHub Release
with auto-generated notes.

This skill walks the user through cutting that release safely.

## Conventions

- **Tag format:** `vMAJOR.MINOR.PATCH` (e.g. `v0.1.0`). No pre-release suffixes.
- **Bump rule of thumb:**
  - `patch` — bug fixes, doc/script tweaks, no behavior change
  - `minor` — new features, no breaking changes (default for most releases here)
  - `major` — breaking API/UI/DB changes
- **Single source of truth for sample fixtures:** `docs/samples/`. Do not reintroduce duplicates under `backend/.../TestData/`.
- **Private files stay local:** `docs/samples/own/` and `docs/samples/other/` are gitignored. Never `git add -A` blindly.

## Steps

Run these in order. If anything looks off, stop and ask the user.

### 1. Pre-flight checks

Show the user the current state before touching anything:

```bash
git status --short
git log --format="%h %s" -10
git tag --sort=-creatordate | head -5
git fetch --tags
git ls-remote --tags origin | tail -10
```

Confirm:
- working tree clean (or only `.claude/settings.local.json`, which we never commit)
- on `master` branch and up to date with `origin/master`
- the latest local tag matches the latest remote tag

### 2. Determine the next version

If the user gave an explicit version (e.g. `v0.2.0`), use it.

Otherwise, derive from `$ARGUMENTS` (`major`/`minor`/`patch`) and the latest tag:

```bash
LAST=$(git tag --sort=-v:refname | grep '^v' | head -1)   # e.g. v0.1.0
```

If `$ARGUMENTS` is empty, look at the commits since `$LAST`
(`git log $LAST..HEAD --format="%s"`) and propose a bump (`minor` for new
features, `patch` for fixes only, `major` for breaking changes). Confirm with
the user before continuing.

### 3. Run the test suite

```bash
dotnet test backend/KnxMonitor.ProjectParser.Tests/KnxMonitor.ProjectParser.Tests.csproj --nologo --verbosity quiet
```

Expected: all tests pass; some `[SkippableFact]` may skip if the user does not
have `docs/samples/own/` locally — that is fine. Hard failures block the release.

Optional but recommended:
```bash
bash test-all-samples.sh
```

### 4. Update README before tagging (important!)

The release workflow tags a specific commit, and that commit's source tree is
what shows up in the GitHub Release "Source code" download. **Always update
the README first, commit it, and only then tag** — otherwise the tag points
to a tree that still advertises the old version (we hit this in v0.1.0).

In `README.md`:
- Add a new `### vX.Y.Z (Latest)` block at the top of the `## Releases`
  section. Move the previous `(Latest)` block down, drop its `(Latest)` marker.
- Update the bullet list under the new version with the user-visible changes
  since the last tag (look at `git log $LAST..HEAD`).
- Update the Docker pull example to the new tag and link the new release page.
- Update the `## Project Status` "Current:" line to the new version.
- If the **Features** section needs a new top-level capability (e.g. a new ETS
  version, a new auth flow), add it there too.

Stage and commit:
```bash
git add README.md
git commit -m "Update README for vX.Y.Z (<one-line headline>)"
```

### 5. Push the README commit, then tag

```bash
git push origin master
git tag vX.Y.Z
git push origin vX.Y.Z
```

(Two pushes on purpose: master first so the tag points at a commit that is
already on the remote.)

### 6. Watch the workflow

Tell the user where to look:

```
gh run watch
# or in the browser:
https://github.com/ingel81/knx-ng-monitor/actions
```

Build time is ~15–20 min (binaries × 6 + docker amd64 + release).

### 7. After the workflow finishes

- Verify on https://github.com/ingel81/knx-ng-monitor/releases/tag/vX.Y.Z that all 6 archives are attached.
- `docker pull ingel81/knx-ng-monitor:vX.Y.Z` should succeed.
- The release notes are auto-generated from commit messages between tags — if they look wrong, fix on the GitHub release page (does not require re-tagging).

## Things to avoid

- **Never `--no-verify`, never `--amend` a pushed commit, never `--force` a tag** unless the user explicitly says so. The workflow ran on the original tag; force-pushing it would invalidate the existing release.
- Do not rebuild `publish/` and forget to kill the running `KnxMonitor.Api` process — it locks the DLLs and the publish fails silently. Stop the dev process first (`powershell -Command "Get-Process -Name KnxMonitor.Api -ErrorAction SilentlyContinue | Stop-Process -Force"`).
- Do not commit anything from `docs/samples/own/`, `docs/samples/other/`, or `backend/KnxMonitor.ProjectParser.Tests/TestData/`. They are gitignored; verify with `git status` before staging.
- Do not push a tag without a README update — see step 4.

## When to skip steps

- If the user passes an explicit version, skip step 2's prompt.
- If the user says "skip tests", skip step 3 but warn them.
- If the user says "no readme", skip step 4 but warn that the release page will misrepresent the version.
