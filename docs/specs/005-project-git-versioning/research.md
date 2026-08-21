# Research: Git Version Control for Editing Projects

**Feature**: 005-project-git-versioning | **Date**: 2026-07-28

Each entry records a decision that resolves an unknown from the Technical Context, with rationale and the alternatives that were evaluated.

## R-1. Git engine: the user's installed `git` CLI

**Decision**: Invoke the user's installed `git` binary as a child process. Do not take a `LibGit2Sharp` dependency. No hybrid.

**Rationale** (in order of weight):

1. **Credentials.** Push/pull must work with SSH keys + agents, HTTPS credential helpers (Git Credential Manager, osxkeychain, libsecret), and host-specific configuration the user already has. The CLI inherits all of it for free. LibGit2Sharp requires hand-written credential callbacks per transport, and the stock `LibGit2Sharp.NativeBinaries` libgit2 build has no usable SSH transport — "SSH remotes don't work" is unacceptable for the approved remote scope (FR-032).
2. **git-lfs.** libgit2 does not run smudge/clean filters, so LFS-tracked media would check out as pointer files. The CLI delegates to `git-lfs` transparently (FR-035).
3. **Native binary / codesigning.** Bundling `libgit2` dylibs inside the notarized macOS .app means signing third-party natives for x64+arm64 in the release pipeline — pure added risk. The CLI needs zero native payload.
4. **Maintenance.** LibGit2Sharp releases are sporadic and historically lag new .NET versions; the CLI is evergreen and the on-disk repo format is the compatibility contract.
5. **Performance is irrelevant here.** All operations run at human-interaction rate over hundreds of small JSON files; ~10 ms process-spawn overhead is noise.

**Alternatives considered**:
- *LibGit2Sharp*: rejected on credentials/SSH, LFS, native bundling, and maintenance grounds above. License note: LibGit2Sharp is MIT but links libgit2 (GPLv2 **with linking exception** — permissible, but moot given rejection).
- *Hybrid (library for read, CLI for network)*: rejected — two failure domains, two behavior models, no measurable win.

**Consequence**: graceful degradation when git is absent is a first-class requirement (FR-037), following the `FFmpegInstallService` probe precedent (`src/Beutl.Extensions.FFmpeg/FFmpegInstallService.cs` — `which` probe, stdout capture, `WaitForExitAsync`).

## R-2. CLI invocation contract

**Decision**: A single `GitCliRunner` owns all process invocation, with these rules:

- Never through a shell; argument arrays only. Working directory = repository root.
- Environment on every call: `GIT_TERMINAL_PROMPT=0` (fail fast instead of hanging on credential prompts), `GIT_OPTIONAL_LOCKS=0` (`git status` must not write the index — prevents a feedback loop with the work-tree watcher), `GIT_LITERAL_PATHSPECS=1` (treat generated project paths as literal data), and `LC_ALL=C` (stable parseable output). The sole literal-path exception is `git check-ignore --stdin -z`, which receives validated NUL-delimited repository-relative paths and sets `GIT_LITERAL_PATHSPECS=0` so Git can apply ignore patterns. Network operations preserve inherited `GIT_SSH_COMMAND`/`GIT_SSH`/`GIT_SSH_VARIANT` and effective repository/global `core.sshCommand`/`ssh.variant`; only the unconfigured default transport adds `GIT_SSH_COMMAND=ssh -oBatchMode=yes`.
- Machine-readable output only: `status --porcelain=v2 -z`, `log --format=…%x00 -z`, `show --name-status -z`, `rev-parse`, `for-each-ref`. Human-facing output is never parsed.
- Cancellation kills the child process.

**Rationale**: prompts hanging a GUI process, locale-dependent output, and index-writing status calls are the three classic failure modes of GUI-embedded git; each rule closes one. Preserving the effective SSH command keeps user-selected wrappers and non-OpenSSH clients functional, while closing the redirected standard-input stream and adding BatchMode only to default OpenSSH keeps the default path noninteractive. Detailed in `contracts/git-cli-invocation.md`.

**Alternatives considered**: parsing default (`--porcelain` v1 / human) output — rejected, v2 -z is the documented stable machine interface.

## R-3. Git discovery on GUI launch

**Decision**: Probe an ordered candidate list, overridable via `VersionControlConfig.GitExecutablePath`:

- macOS: `git` on PATH → `/usr/bin/git` only if Xcode CLT is actually installed (`xcode-select -p` succeeds; the bare stub otherwise triggers Apple's CLT install dialog) → `/opt/homebrew/bin/git` → `/usr/local/bin/git`.
- Windows: `where.exe git` → `%ProgramFiles%\Git\cmd\git.exe`.
- Linux: `git` on PATH.
- Validate with `git --version` and enforce a minimum version floor (2.23+, for `git switch`, worktree, and the required plumbing behavior). Repository initialization uses `git init` followed by `git symbolic-ref HEAD refs/heads/main`, because `git init -b` is only available from Git 2.28.
- Bound each subprocess probe to 5 seconds and the complete ordered discovery pass to a shared 10-second budget, while preserving caller cancellation. If the shared budget expires after Git validation but during the LFS probe, report Git as installed with LFS unavailable.

**Rationale**: macOS GUI apps launch with a minimal PATH; the CLT stub is a well-known trap that would pop an OS dialog from inside Beutl. A shared deadline prevents several missing or stalled candidates from multiplying the per-process timeout into an unbounded GUI wait.

**Alternatives considered**: requiring PATH only — breaks the majority macOS GUI-launch case.

## R-4. Commit model: snapshot on explicit save/close only

**Decision**: The work tree is continuously current (autosave writes every undoable edit); commits mark user-meaningful points only — explicit Save / Save All, project close, safety snapshots around destructive-ish operations, recovery snapshots after compensating a failed restore, and manual commits. A dirty pull first writes the project state to a durable private ref without advancing the branch, then promotes it to a normal safety commit on the fast-forwarded tip. Clean-tree triggers skip silently. Always `git add -A -- <pathspec>` (whole project); no partial staging. *(Pinned by clarification 2026-07-28 and review resolution 2026-07-31.)*

**Rationale**: autosave fires per edit (`EditViewModel.OnChangeOperations` → `AutoSaveService`); mapping commits 1:1 onto it would produce a commit per drag. `HistoryManager` is per-scene, in-memory, with no save-point concept, so the only honest definition of "version" is "the on-disk state at a moment the user called done".

**Alternatives considered**: timer-based checkpoints (rejected in clarification — history noise); commit-per-undo-transaction (rejected — explodes history and couples undo to VC).

## R-5. Snapshot message format

**Decision**: Stable English subjects (`beutl: snapshot on save`, `beutl: snapshot on close`, `beutl: safety snapshot before <op>`, `beutl: restore project state from <shortsha>`, `beutl: recover original project state after failed restore`, `beutl: initialize version control`) plus a machine-readable trailer `Beutl-Snapshot: save|close|safety|restore|recovery|init`. Manual commits use the user's message verbatim, no trailer. The history UI localizes the *display* by parsing the trailer.

**Rationale**: repository content must be language-independent (survives locale switches and external tools); trailers are git's sanctioned metadata channel (FR-016).

**Alternatives considered**: localized subjects written into the repo — rejected (locale-coupled history); git notes — rejected (don't survive push by default).

## R-6. Restore semantics: "restore as a new commit"

**Decision**: Default restore = close project → validate the attached branch plus scoped worktree/index → apply the selected tree through the guarded tree-transition transaction → append a commit with the `restore` trailer → reopen. The transition removes project files absent from the selected tree without running a broad clean and protects untracked or ignored collisions. If failure occurs after the Restore commit, apply the captured original tree and append a compensating `recovery` commit before reopening; never erase the attempted restore. A secondary "Restore to new branch" is offered in the commit context menu. Exposing detached HEAD and destructive reset is rejected outright.

**Rationale**: history stays linear and complete (the pre-restore state is one commit back), nothing is ever lost, `push` keeps working, and the mental model — "make the project look like it did then" — needs zero git literacy. Detached HEAD orphans subsequent auto-commits (GC-able = data loss); reset rewrites history (violates FR-021/FR-028).

**Alternatives considered**: checkout-detached with a rescue branch on edit — rejected as the *default* (silent branch proliferation, confusing state), retained as the explicit secondary action.

## R-7. Live-editor constraint: close → operate → reopen

**Decision**: Every operation that changes files under the editor (restore, branch switch, pull) runs a read-only preflight → release backend gate → confirm → acquire project transition/work-tree lease → reacquire backend gate and revalidate → preserve dirty project state → `ProjectService.CloseProject()` → git operation → `ProjectService.OpenProject()` cycle. This project-transition→backend mutation order matches normal close and prevents lock inversion. Restore and branch switch preserve dirty state as an ordinary safety commit. Pull preserves it as a durable private-ref checkpoint so the checked-out branch can still fast-forward, then publishes a separate durable recovery descriptor immediately before transition, reapplies and commits that state on the new tip, and removes descriptor+checkpoint atomically only after verified reopen/recovery. Restart activation enumerates descriptors and offers recovery without requiring the tool tab; declined entries remain actionable in the tab. Push does not touch the work tree and needs no cycle.

**Rationale**: the in-memory `Scene` is live-bound and `HistoryManager` is per-open-scene; rewriting files under them is undefined behavior. `ProjectPackageService.ImportAsync` already uses exactly this shape, so the lifecycle seam is proven.

**Alternatives considered**: in-place model reload — a much larger feature (object-graph diffing against the live scene) with no v1 payoff; explicitly rejected for v1. Committing dirty state before pull — rejected because it creates local divergence exactly when the remote is ahead. A process-only temporary stash — rejected because cancellation or a process crash can make the saved state undiscoverable to the app; the private ref is a durable recovery marker.

## R-8. Status pipeline and the autosave feedback loop

**Decision**: A `RepositoryWatcher` (recursive FileSystemWatcher on `ProjectRoot`, non-recursive `.gitignore`/`.gitattributes` watchers in each ancestor directory through `RepoRoot`, dedicated Git metadata watchers resolved from `RepoRoot` through `.git`/gitdir/commondir and refs, 500 ms debounce, background-thread events) triggers a single `git status --porcelain=v2 -z` per burst. The ancestor watchers ignore unrelated files and sibling subtrees; `.git/`, `**/.beutl/`, and `*.tmp` are excluded from worktree watch events. `GIT_OPTIONAL_LOCKS=0` guarantees status never writes the Git index, so status cannot retrigger the watcher (double protection). Mutating service calls refresh status on completion. All git operations serialize on one `SemaphoreSlim(1,1)` per project.

**Rationale**: autosave writes the tree on every edit, so the watcher fires constantly; the debounce+exclusion+no-lock triple keeps status calls bounded. Modeled on `DirectoryWatcherService` (`src/Beutl.Editor.Components/FileBrowserTab/Services/DirectoryWatcherService.cs`) but Avalonia-free.

**Verification**: a scripted 1000-edit burst test asserts a bounded number of status invocations.

## R-9. Repository hygiene: generated `.gitignore` / `.gitattributes`

**Decision**: On init, write at the project root:

- `.gitignore`: `**/.beutl/` (per-user view state **and** `output-profile.json`, which lives under `<sceneDir>/.beutl/` — so its absolute paths never enter history), `*.tmp` (atomic-write leftovers).
- `.gitattributes`: `*.bep`/`*.scene`/`*.belm` (+ the dotfiles themselves) `text eol=lf`; LFS patterns for `resources/**` media extensions when LFS is active.
- `resources/` is **committed** (media traveling with the project is a core value of remotes).

**Rationale**: Beutl's own exporter already excludes `.beutl` (`ProjectPackageService`); ignoring it also covers the absolute-path output-profile issue without a serializer change.

**Alternatives considered**: ignoring `resources/` — rejected (a cloned project would silently lose its relocated media).

## R-10. Serialization prerequisites (in-scope fixes)

**Decision**: Four fixes land first, each as an independent PR-sized task with tests:

1. **Id-based element file names** — extract the AgentToolkit convention (`{Id:N}.belm`, `-{index}` collision suffix; `DeclarativeDocumentApplier.cs:788`) into an `ElementFileNaming` helper in `Beutl.Editor` and replace the six GUI call sites of `RandomFileNameGenerator` (`ElementAdderImpl.cs:50,287`, `ElementStructureService.cs:74`, `ElementClipboardService.cs:205,294`, `DuplicateHelper.cs:162`). No bulk rename of existing files (scene loading is glob-based; names are cosmetic).
2. **appVersion churn** — `Project.Serialize` writes `BeutlApplication.Version` unconditionally (`src/Beutl.Core/Project.cs:96`); change to persist the loaded `AppVersion` and advance it only when a migration actually rewrites content. Project-item deserialization reports real persisted-content migrations back to `Project`, including legacy formats that normalize to an empty current collection; plain old-version resaves remain unchanged. `feat!:` + positive and negative migration regressions.
3. **Exclude-list separator normalization** — `Scene` stores `Path.GetRelativePath` output (native `\` on Windows; `Scene.cs` include/exclude update paths); normalize to `/` on write, accept both on read.
4. **JSON newline pinning** — `JsonHelper.WriterOptions` (`src/Beutl.Core/JsonHelper.cs:41`) leaves `JsonWriterOptions.NewLine` at its .NET default (`Environment.NewLine` ⇒ CRLF on Windows); pin `NewLine = "\n"`, paired with the `.gitattributes` `eol=lf`. One-time diff for existing Windows projects, called out in release notes.

**Rationale**: without these, SC-002 (minimal diffs) and SC-006 (cross-platform portability) are unfalsifiable; each is a spurious-diff or correctness defect independent of this feature's UI.

**Explicitly not fixed** (recorded in spec Out of Scope): ObjectRegenerator GUID regeneration (semantically required for duplicates), percent-encoded URIs (stable, cosmetic), output-profile absolute paths (never committed).

## R-11. Nested / pre-existing repository handling

**Decision**: Before init, `git rev-parse --show-toplevel` from the project directory. If an enclosing repo exists: never nested-init without consent; offer "use enclosing repository" (all path-touching and project-history calls are scoped with pathspec `-- <projectRelDir>`; a project-local `.gitignore` is written inside the project directory) or "leave unmanaged". Repository-level branch, push, and pull operations act on the whole enclosing repository, disclosed in the UI ("repository root: …").

**Rationale**: users keep projects in their own monorepos; sweeping unrelated files into a Beutl snapshot (or nesting repos silently) is corruption of *their* repository (FR-003). Pathspec scoping also defuses the pathological "home directory is a repo" case.

## R-12. Remote scope and conflict policy

**Decision**: One remote (`origin`), URL-configurable. Push = `git push -u origin HEAD` with progress + cancel. Pull fetches, resolves the configured upstream commit, proves the update is fast-forward, and performs the close/reopen tree transition without invoking merge or rebase. A dirty pull captures the attached branch tip and a durable project checkpoint, builds the merged project tree and Safety commit off-ref, then applies that exact state and compare-and-swaps the branch as the last durable step. Any failure restores the captured tree/index only while ownership fingerprints still match; an unexpected external ref movement or unrelated dirty repository state is refused, never overwritten. Divergence and unmerged states are detected and surfaced with "resolve outside Beutl" guidance; all VC operations block in the `Conflicted` state; the editor itself stays usable; opening files containing conflict markers warns first (they fail JSON parse).

**Rationale**: fast-forward-only means git itself refuses anything destructive; the element-per-file layout keeps realistic conflicts confined to `.scene`/`.bep`, which external tools handle. A semantic merge UI is a standalone future feature (spec Out of Scope).

## R-13. Identity handling

**Decision**: Use `git config user.name/user.email`. If unset at first commit: prompt once (prefilled from the OS username), write **repo-local** config only. The initialization seam accepts `Func<CancellationToken, Task<GitIdentity?>>` and passes the exact operation token into that prompt. The Avalonia identity flyout observes the token, cancels its pending result, and closes itself on the UI thread. Unattended auto-commit with missing identity is skipped with a one-time warning instead of fabricating an identity.

**Rationale**: mutating `--global` config from an app is hostile; silent fabricated identities poison shared repos (FR-004).

## R-14. Test strategy against real git

**Decision**: Unit tests run real `git` in per-test temp directories: fixture-level `git --version` probe with `Assert.Ignore` when absent; determinism via `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_NOSYSTEM=1`, fixed `GIT_AUTHOR_DATE`/`GIT_COMMITTER_DATE`, repo-local identity. Remote tests use a local bare repository (no network). Two headless-shell E2E scenarios (save→commit appears; restore cycle) live in `tests/Beutl.HeadlessUITests/`; everything else stays in `tests/Beutl.UnitTests/Editor/VersionControl/` per the csharp.md placement rule.

**Rationale**: mocking git verifies nothing about the porcelain formats this feature depends on; CI runners always ship git. Network/credential paths are the manual-verification matrix (they cannot be automated honestly).
