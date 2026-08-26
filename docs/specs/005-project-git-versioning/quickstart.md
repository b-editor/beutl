# Quickstart: Git Version Control for Editing Projects

**Feature**: 005-project-git-versioning

This walkthrough doubles as the SC-005 discoverability check (enable → history → restore within 2 minutes, in-app UI only) and the manual-verification script.

## 1. Enable tracking

**New project**: File → New Project → the "Track history with Git" checkbox is visible (Git detected) and pre-checked → Create. The project directory is now a repository with an initial version; `.beutl/` state and `*.tmp` are excluded automatically.

**Existing project**: Project → Enable Version Control…. If the project already sits inside one of your own repositories, Beutl asks whether to use that repository or leave the project unmanaged — it never creates a nested repository on its own. Snapshots, status, history, and restore stay scoped to the project folder; branch, push, and pull actions apply to the whole enclosing repository and the UI shows its root.

**No Git installed?** The Version Control tab shows a single guidance panel with per-OS install instructions; everything else in Beutl works as usual.

## 2. Save = version

Edit something, press Ctrl+S / Cmd+S. Open View → Version Control: a "Saved" snapshot appears at the top of the history. Save again without changes — no new version (no empty snapshots). Closing the project with unsaved-to-history changes records a "Closed" snapshot.

## 3. Name a milestone

In the Version Control tab, type a message ("rough cut v1") and press Commit. Your commit appears with a distinct badge next to the automatic snapshots.

## 4. Inspect and restore

Select any version → the changed files list appears; select a file → a line diff. Click Restore on an older version → Beutl explains the project will close and reopen (undo history clears), snapshots your current state for safety, restores, and reopens. The history keeps everything: the old versions, your pre-restore state, and a new "Restored" entry. Nothing is ever deleted.

Prefer to keep the restored line separate? Right-click the version → Restore to new branch.

## 5. Branch an experiment

Version Control tab → branch dropdown → New branch ("alt-ending"). Edit and save freely; switch back via the dropdown (Beutl runs the same safe close/reopen cycle, snapshotting first if needed). Each branch reopens with exactly its own state. Beutl never merges branches beyond fast-forward — divergent lines stay intact as separate versions.

## 6. Back up to a remote

Version Control tab → Remote → paste your repository URL (GitHub/GitLab/self-hosted) → Push. Authentication uses whatever Git already uses on your machine (credential manager, SSH agent); Beutl never asks for or stores passwords. If large media is tracked with LFS, a one-time notice explains hosting quotas.

On another machine: clone the repository with any Git tool, open the `.bep` in Beutl, and continue. Pull fetches new versions (fast-forward only); if histories diverged, Beutl tells you and leaves both sides untouched for resolution in an external Git client.

## Manual verification matrix (release gate)

| Check | Platforms |
|---|---|
| HTTPS push/pull via credential helper (GitHub) | Windows / macOS / Linux |
| SSH push/pull via agent; repeat with a custom `core.sshCommand` or `GIT_SSH*` wrapper/proxy and verify Beutl preserves it | Windows / macOS / Linux |
| GUI-launch git discovery (Homebrew git, CLT git, no git) | macOS |
| LFS round-trip with a >100 MB video in `resources/`, clone on 2nd machine, verify playback | any two |
| Git-absent degradation (full editor pass, zero errors) | one per OS |
| Windows-committed project cloned and opened on macOS/Linux (SC-006) | Windows → macOS/Linux |
| Auth-failure dialog wording (revoked token / no agent) | any |
| Notarized-bundle smoke test: process spawn works from the .app | macOS |
