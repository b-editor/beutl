# Feature Specification: Git Version Control for Editing Projects

**Feature Branch**: `speckit/005-project-git-versioning`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "プロジェクトをGitで履歴管理できるようにしたい。 — Git version control for user editing projects: full in-app Git integration (commit, history browsing, restore of past versions, branching, remote push/pull) for Beutl editing projects, with automatic snapshots on explicit save/close plus manual user commits with messages, with graceful degradation when Git is absent, including prerequisite git-friendly project-storage fixes and generated ignore/attribute rules with optional large-media handling."

## Overview

A Beutl editing project is already a self-contained directory of small, human-readable text files (one project file, one file per scene, one file per timeline element). This feature turns that directory into a Git repository that the app manages for the user: every explicit save becomes a restorable version, the user can browse the project's history and restore any past version from inside the editor, create branches to try alternative edits, and push/pull the project to a remote for backup and multi-machine work — all without requiring any Git knowledge for the core flows.

Version history is powered by the Git tooling installed on the user's machine. When Git is not installed, the feature quietly steps aside: the editor remains fully functional and the versioning surface shows installation guidance instead of errors.

## Clarifications

### Session 2026-07-28

- Q: Default state of the "track history with Git" option on project creation (shown only when Git is detected)? → A: Enabled by default; the default is adjustable in application settings.
- Q: Automatic snapshot triggers — explicit save/close only, or additionally timer-based checkpoints? → A: Explicit save / save-all / project close only; no timer-based checkpoints.
- Q: Does a Save As copy carry the original's history or start fresh? → A: The copy starts a fresh, independent history; the original keeps its history. Copying the repository would silently duplicate history size and remote configuration.
- Q: Default for the large-file extension (Git LFS) on in-project media? → A: Used automatically when detected (configurable off); a one-time quota notice is shown when a remote is first connected with LFS active.

## Scope

### In scope (this feature)

- Opt-in, per-project version tracking with app-managed repository setup (ignore rules, attribute rules, initial version).
- Automatic snapshots on explicit save / save-all / project close, plus manual commits with user messages.
- A version-history view: version list, per-version change summary, and content diff display.
- Whole-project restore of any past version, always non-destructive (history is preserved; a safety snapshot protects unsaved work).
- Branch list / create / switch for exploring alternative edits.
- A single remote per project: push and pull (fast-forward only), with authentication delegated to the user's existing Git credential setup.
- Project-storage hygiene fixes required for meaningful versioning: minimal diffs per save, stable element file names, cross-platform path separators and line endings, and exclusion of per-user editor state from history.
- Graceful degradation when Git (or the optional large-file extension) is unavailable.

### Out of scope (deliberately excluded)

- **In-app merge conflict resolution.** Divergent histories are detected and the user is directed to resolve them with external Git tooling; both sides are always preserved, so no data is lost by this exclusion. A semantic merge tool for scene content is a standalone future feature.
- **Semantic / visual timeline diff.** The history view shows changed items and line-based content diffs; a visual "what changed on the timeline" comparison is a separate feature with its own design surface, and no correctness requirement in this feature depends on it.
- **Partial staging / per-file commits.** Versions always capture the whole project; element-level cherry-picking of changes contradicts the "each save is a version" model.
- **Bundling a Git runtime with the app.** The feature uses the user's installed Git and offers installation guidance when absent; shipping a private Git increases installer size and update surface for little gain in v1.
- **Multiple remotes, tags, rebase, force-push, or history rewriting of any kind.** The in-app surface is intentionally limited to operations that cannot lose committed work.
- **Making element duplication/splitting preserve identifiers.** Duplicated objects must receive new identifiers for correctness; the resulting "new file" diffs are semantically accurate.
- **URI readability cosmetics.** Percent-encoded non-ASCII names in project files are stable across saves and never churn diffs; changing the encoding is a cosmetic, repo-wide-diff-causing change with round-trip risk.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every save is a restorable version (Priority: P1)

A user enables version tracking for a project (at creation time or later from the menu). From then on, every explicit save quietly records a snapshot of the whole project. The user never has to think about Git: saving is versioning.

**Why this priority**: This is the core value — passive, zero-knowledge history. Without it, nothing else in the feature matters.

**Independent Test**: Create a project with tracking enabled, make three edits with an explicit save after each, and verify three distinct versions exist, each reflecting the project state at that save.

**Acceptance Scenarios**:

1. **Given** a new project and Git installed, **When** the user enables version tracking, **Then** the project directory becomes a repository with an initial version, and per-user editor state (view state, output profiles, temp files) is excluded from tracking.
2. **Given** a tracked project with unsaved changes, **When** the user explicitly saves, **Then** a snapshot version is recorded automatically, labeled as a save snapshot.
3. **Given** a tracked project with no changes since the last snapshot, **When** the user explicitly saves again, **Then** no new version is created (no empty versions).
4. **Given** a tracked project with changes, **When** the user closes the project, **Then** a close snapshot is recorded so nothing is left unversioned.
5. **Given** a tracked project, **When** the user performs many rapid edits without an explicit save, **Then** no versions are created for individual edits (autosave keeps files current, but versions mark user-meaningful points only).

---

### User Story 2 - Browse history and restore a past version (Priority: P1)

The user opens the version-history view, sees a chronological list of versions (save snapshots, close snapshots, manual commits), inspects what changed in each, and restores the project to any past version. Restore never destroys anything: the current state is snapshotted first, and the restore itself is recorded as a new version.

**Why this priority**: History is only useful if you can get back to it. Restore is the second half of the core value and the feature's biggest safety promise.

**Independent Test**: Build a 10-version history, restore version 4, verify the project reopens exactly in its version-4 state, and verify all 10 prior versions plus the pre-restore state remain reachable in history.

**Acceptance Scenarios**:

1. **Given** a tracked project with history, **When** the user opens the history view, **Then** versions are listed with time, kind (automatic/manual), message, and author, and the list stays responsive for long histories.
2. **Given** a selected version, **When** the user inspects it, **Then** a summary of changed items and a readable content diff are shown.
3. **Given** a selected past version, **When** the user chooses Restore, **Then** the app explains that the project will close and reopen and that undo history will be cleared, snapshots any unsaved changes, restores the project files to the selected version, records the restore as a new version, and reopens the project.
4. **Given** a completed restore, **When** the user inspects history, **Then** the pre-restore state is still present and restorable (no version was deleted or rewritten).
5. **Given** elements that were added after the restored version, **When** the restore completes, **Then** those elements are absent from the reopened project (the project matches the restored version exactly).

---

### User Story 3 - Safe coexistence and graceful degradation (Priority: P1)

A user without Git installed keeps using Beutl exactly as before; the versioning surface shows what to install and why. A user whose projects already live inside an existing repository (e.g. their own monorepo) gets versioning that cooperates with that repository instead of fighting it.

**Why this priority**: The feature must never make the editor worse for users who don't use it, and must never corrupt a user's existing repository. Both are launch-blocking safety properties.

**Independent Test**: On a machine without Git, exercise the full editor surface and verify zero versioning errors; then place a project inside an existing repository and verify snapshots and restore touch only the project's own directory while disclosed branch, push, and pull operations affect the whole enclosing repository.

**Acceptance Scenarios**:

1. **Given** Git is not installed (or is older than the supported floor), **When** the user opens any project, **Then** the editor is fully functional, the versioning surface shows installation guidance, and no error dialogs appear.
2. **Given** a project directory already inside an existing repository, **When** the user enables version tracking, **Then** the app detects the enclosing repository, never creates a nested repository without explicit consent, and offers to use it while disclosing that branch and remote operations affect the whole enclosing repository.
3. **Given** a project in a shared (enclosing) repository, **When** a snapshot is recorded, **Then** only files under the project directory are ever included in the version.
4. **Given** a previous app crash left a stale repository lock, **When** the project is next opened, **Then** versioning recovers automatically or offers a one-click recovery, and never wedges permanently.

---

### User Story 4 - Manual commits with messages (Priority: P2)

At meaningful milestones ("rough cut done", "client feedback round 1"), the user records a named version with their own message, visually distinguished from automatic snapshots in the history view.

**Why this priority**: Named milestones make long histories navigable, but automatic snapshots already provide the safety net, so this is additive.

**Independent Test**: Record a manual commit between automatic snapshots and verify it appears in history with the user's message and a distinct visual treatment.

**Acceptance Scenarios**:

1. **Given** a tracked project, **When** the user invokes Commit with a message, **Then** a version with that message is recorded, capturing the whole current project state.
2. **Given** mixed history, **When** the user browses it, **Then** manual commits are visually distinguishable from automatic snapshots at a glance.
3. **Given** no changes since the last version, **When** the user tries to commit, **Then** the app says there is nothing to record (and does not create an empty version).

---

### User Story 5 - Branches for experiments (Priority: P2)

The user creates a branch to try a different edit of the same project ("alt-ending"), switches between branches, and keeps both lines of work intact.

**Why this priority**: Valuable for creative iteration, but builds entirely on the P1 snapshot/restore machinery.

**Independent Test**: Create a branch, make divergent edits on both branches, switch back and forth, and verify each branch reopens with exactly its own state.

**Acceptance Scenarios**:

1. **Given** a tracked project, **When** the user creates a branch, **Then** the new branch starts from the current version and becomes the active branch.
2. **Given** unsaved changes, **When** the user switches branches, **Then** the app prompts, snapshots the current state, closes the project, switches, and reopens — never silently discarding work.
3. **Given** two diverged branches, **When** the user switches between them, **Then** each branch's project state is fully restored, and no in-app operation offers a merge beyond fast-forward.

---

### User Story 6 - Remote backup and multi-machine work (Priority: P3)

The user connects the project to a remote repository, pushes their history for backup, and pulls it on another machine (or after edits elsewhere), using the credentials they already have configured for Git.

**Why this priority**: High value but depends on everything else working, adds network/auth complexity, and is the first story where the outside world can push back (divergence, auth failures).

**Independent Test**: Push a tracked project to a remote, clone it on a second machine (different OS), open it in Beutl, and verify it loads and renders identically; then verify pull brings new versions across.

**Acceptance Scenarios**:

1. **Given** a tracked project and a remote URL, **When** the user connects the remote and pushes, **Then** the full history transfers using the user's existing Git authentication, with visible progress and the ability to cancel.
2. **Given** a remote with new versions, **When** the user pulls and the local history has not diverged, **Then** the project updates to the remote state via the same safe close/reopen cycle, after a safety snapshot.
3. **Given** local and remote histories have diverged, **When** the user pulls or pushes, **Then** the app clearly explains the situation, preserves both sides untouched, and directs the user to external Git tooling — it never merges, overwrites, or discards either side.
4. **Given** authentication fails, **When** the user pushes or pulls, **Then** the failure surfaces immediately with actionable guidance (credential helper / SSH agent setup), and the app never prompts for or stores passwords itself.
5. **Given** a project committed on Windows and cloned on macOS or Linux, **When** it is opened, **Then** it loads with zero path or line-ending errors.

---

### Edge Cases

- **Project inside the user's own existing repository**: detected before enabling; no nested repository is created without explicit consent; snapshots, status, history, and restore are scoped to the project directory, while branch, push, and pull operations affect the whole enclosing repository and are disclosed as such.
- **Git missing, broken, or below the version floor**: versioning UI degrades to guidance; every other editor feature is unaffected; the probe never blocks startup.
- **Snapshot concurrent with export/render/proxy generation**: output operations hold a shared workspace lease, while snapshots hold the exclusive lease through staging and commit. An already-running output makes save/close skip only the Git snapshot, and a snapshot in progress refuses a new output. The save/close action itself continues; the next explicit save after output completes records the accumulated changes, so no snapshot captures a partially written file.
- **Restore or branch switch with unsaved in-memory state**: the user is prompted; dirty on-disk project state is recorded in a safety snapshot first, while a clean project creates no empty snapshot; the close/reopen cycle is the only path that changes files under the editor.
- **Editing after restoring an old version**: the restore itself is a new version on the current branch, so subsequent saves continue linearly — no detached or orphaned states are ever created.
- **Huge media files committed into the project**: when the large-file extension is unavailable or a candidate path is not effectively covered by an LFS filter, a one-time warning explains that history size is permanent before large media is first committed; the operation is never blocked.
- **Cross-platform round-trip**: path separators are normalized in stored file lists, line endings are pinned identically on all platforms, and case-only filename differences are avoided by the app's own file naming; a project committed on one OS opens cleanly on the others.
- **Interrupted version operation (crash mid-commit)**: a stale repository lock is detected and recovered on next open; the project files themselves are always intact thanks to atomic saves.
- **Remote failures (offline, rejected auth, non-fast-forward push)**: each failure mode surfaces an actionable, distinct message; saving and editing are never blocked by remote problems.
- **Stale per-user view state after restore**: reopening tolerates view state that references elements that no longer exist (view state is untracked and may lag the restored content).
- **Second writer (e.g. a headless agent or external Git session) on the same project**: Beutl serializes its own mutations; tree transitions lock the worktree HEAD, validate scoped fingerprints, and use expected-old branch updates. A detected external change aborts without being overwritten and may leave the project closed with its checkpoint retained. Ordinary snapshots still capture only completed atomic file writes.
- **Project Save As / rename**: saving a copy to a new location starts a fresh, independent history for the copy (the original keeps its history); an in-place rename of project items relies on rename detection and does not lose history.

## Requirements *(mandatory)*

### Functional Requirements

**Versioning lifecycle & repository hygiene**

- **FR-001**: Version tracking MUST be opt-in per project: offered as a pre-selected option when creating a project (only when Git is available) and as an explicit "enable version tracking" action for existing projects. The system MUST NOT initialize a repository without user consent.
- **FR-002**: Enabling tracking MUST set up the repository at the project root with generated ignore rules (per-user editor state, temporary files) and attribute rules (consistent line endings; large-media handling when available), and record an initial version of the current project state.
- **FR-003**: Before initializing, the system MUST detect an enclosing existing repository. If found, it MUST NOT create a nested repository without explicit consent, MUST offer using the enclosing repository, and MUST scope every versioning operation (status, snapshot, history, restore) to the project's own directory so unrelated files are never touched.
- **FR-004**: Version authorship MUST use the user's existing Git identity. When unset, the system MUST ask once and store the identity for that repository only — it MUST NOT modify the user's global Git configuration, MUST NOT silently fabricate an identity, and MUST propagate the initiating operation's cancellation token through the identity request.
- **FR-005**: The system MUST recover from interrupted version operations (e.g. a stale lock left by a crash) on the next project open, without data loss and without permanently disabling versioning.

**Git-friendly project storage**

- **FR-006**: Changing a single property of a single element and saving MUST produce a version whose changes touch only that element's file (plus the scene file for structural changes) — no unrelated file churn.
- **FR-007**: The project file's application-version metadata MUST NOT be rewritten on save unless the project content was actually migrated; opening and saving with a newer app MUST NOT by itself dirty the project.
- **FR-008**: Newly created element files MUST be named from the element's stable identity rather than randomly, so file names are meaningful and reproducible. Existing files MUST NOT be mass-renamed.
- **FR-009**: Stored file lists (element include/exclude patterns) MUST use `/` separators on write and accept both separators on read, so a project saved on one OS loads on the others.
- **FR-010**: Project files MUST serialize with identical line endings on every platform, and repository attribute rules MUST pin the same policy, so cross-platform collaboration produces no line-ending diffs.
- **FR-011**: Per-user editor state (view state, output profiles) and temporary save artifacts MUST never be recorded in versions.

**Automatic snapshots**

- **FR-012**: When tracking is enabled, an explicit save or save-all MUST record an automatic snapshot if anything changed since the last version, except while an output operation owns the shared workspace lease. In that case the save succeeds without a snapshot, and the next explicit save after output completes records the accumulated changes.
- **FR-013**: Closing a tracked project with changes since the last version MUST record a close snapshot, except while an output operation owns the shared workspace lease. In that case closing continues without asking Git to snapshot files that may still be changing.
- **FR-014**: When nothing changed, save/close/commit MUST NOT create a version (no empty versions), and repeated saves MUST NOT spam history.
- **FR-015**: The system MUST NOT record a version per editing action or autosave tick; continuous autosave keeps files current, while versions mark explicit user save points only.
- **FR-016**: Automatic snapshot messages MUST be stable and machine-readable in the repository, with the kind (save / close / safety / restore / recovery) distinguishable, while the history view localizes what the user sees.
- **FR-017**: Version operations MUST run off the UI thread, MUST be serialized against each other, and MUST NOT capture partially written files. Automatic snapshots MUST hold the exclusive workspace lease for their entire staging/commit interval; an existing output lease skips the snapshot, while an existing snapshot lease refuses a new output.

**History browsing**

- **FR-018**: Users MUST be able to view the version list with time, kind (automatic/manual), message, and author, loaded incrementally so long histories stay responsive.
- **FR-019**: Selecting a version MUST show which files changed, and selecting a changed file MUST show a readable line-based content diff.
- **FR-020**: The history view MUST reflect the current repository state shortly after any change (new snapshots, external commits), without requiring a manual refresh.

**Restore**

- **FR-021**: Users MUST be able to restore the whole project to any past version. Restore MUST be recorded as a new version on the current line of history — the system MUST NOT rewrite, delete, or orphan any existing version to perform a restore.
- **FR-022**: Before any operation that changes files under the editor (restore, branch switch, pull), the system MUST durably preserve the current state when there are changes, then close the project, apply the operation, and reopen it. Restore and branch switch use an ordinary safety commit; pull uses a reachable private checkpoint that does not move the branch and promotes it to a safety commit after fast-forward.
- **FR-023**: The restore confirmation MUST disclose that the project will close and reopen and that the in-session undo history will be cleared.
- **FR-024**: A restored project MUST match the selected version exactly, including the removal of elements that were added after that version.
- **FR-025**: A secondary "restore to a new branch" action MUST be available for users who want to keep the restored line separate.

**Manual commits**

- **FR-026**: Users MUST be able to record a manual version with their own message at any time while a tracked project is open; manual versions MUST be visually distinct from automatic snapshots in the history view.

**Branching**

- **FR-027**: Users MUST be able to list branches, create a branch from the current version, and switch branches; switching follows the same safety-snapshot + close/reopen cycle as restore.
- **FR-028**: The system MUST NOT perform or offer any merge beyond fast-forward, and MUST NOT expose history-rewriting operations (rebase, force operations, resets that discard versions).

**Remotes**

- **FR-029**: Users MUST be able to associate one remote with the project and change its URL.
- **FR-030**: Push MUST transfer the current branch with visible progress and cancellation; push MUST NOT require closing the project.
- **FR-031**: Pull MUST apply only fast-forward updates via the durable-checkpoint + close/reopen cycle. On success, dirty local project state MUST be reapplied and committed on the fast-forwarded tip. On divergence or failure, the system MUST restore the exact captured local branch tip and project state without overwriting a concurrent external ref movement, preserve both sides, and direct the user to external Git tooling when automatic recovery is unsafe. `RepositoryDirty` MUST describe only a failed cleanliness precondition; ownership loss or unverified recovery MUST surface as one localized uncertain-transition failure without composing an inner remote-result message.
- **FR-032**: Authentication MUST be fully delegated to the user's existing Git credential mechanisms; the app MUST NOT collect, store, or transmit credentials itself, and auth failures MUST surface immediately with actionable guidance.
- **FR-033**: When the repository is in a conflicted state (e.g. after an external merge attempt), versioning operations MUST be blocked with clear guidance while the editor itself remains usable; the app MUST warn before opening project files that contain conflict markers.

**Media policy**

- **FR-034**: Media files located inside the project directory MUST be included in versions by default; media referenced from outside the project stays untracked by nature.
- **FR-035**: When the large-file extension is available, it MUST be used automatically for media in the project (configurable); when unavailable, committing media past a size threshold MUST trigger a one-time warning that history growth is permanent — and MUST NOT block. When a remote is first connected while the large-file extension is active, a one-time notice MUST explain that remote hosting quotas may apply to large-file storage and bandwidth.

**Settings & degradation**

- **FR-036**: Application settings MUST cover: default state of the tracking option for new projects, automatic snapshot toggles (save / close), and an override path for the Git executable.
- **FR-037**: When Git is unavailable or below the supported version floor, the entire versioning surface MUST degrade to a single informative state with per-OS installation guidance; every other editor capability MUST remain fully functional with zero versioning errors.

**Non-goals (explicit, to bound scope)**

- **FR-038**: The system is NOT required to provide in-app merge conflict resolution; divergence handling is detection + preservation + guidance.
- **FR-039**: The system is NOT required to provide a semantic or visual timeline diff; line-based content diffs satisfy this feature.
- **FR-040**: The system is NOT required to support partial staging, multiple remotes, tags, or any history-rewriting operation.
- **FR-041**: The system is NOT required to bundle a Git runtime; installation guidance is the v1 answer to Git absence.

### Key Entities

- **Project repository**: the version store rooted at the project directory (or an enclosing repository the user opted into, with operations scoped to the project directory).
- **Version (snapshot/commit)**: a whole-project state with time, author, message, and kind; immutable once recorded.
- **Snapshot kind**: save, close, safety, restore, recovery, or manual — machine-readable in the repository, localized in the UI.
- **Branch**: a named line of history; exactly one is active per project.
- **Remote**: a single associated backup/collaboration endpoint per project.
- **Ignore/attribute rules**: generated repository configuration that excludes per-user state and pins cross-platform text policies.
- **Safety snapshot**: the reachable preservation point taken when project state is dirty, making restore/switch/pull non-destructive without creating empty commits for clean state. For pull it begins as a private checkpoint and becomes an ordinary commit on the fast-forwarded branch tip.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** (integrity): Restoring any version from a 50-version history reopens the project with zero load errors, and the reopened project renders frame-identically to the state that was saved at that version.
- **SC-002** (diff minimality): Changing one property of one element and saving produces a version that touches exactly that element's file (plus the scene file for structural edits) — never the project file, per-user state, or unrelated files.
- **SC-003** (performance): Recording a snapshot of a 500-element project completes within 2 seconds without blocking the UI; the history view opens within 1 second for a 200-version history.
- **SC-004** (safety): 100% of restore, branch-switch, and pull flows with dirty project state create a durable reachable preservation point before mutating files; successful dirty pulls promote that checkpoint to a safety commit, clean flows create no empty safety version, and no sequence of in-app versioning operations can lose committed work or the currently saved project state.
- **SC-005** (discoverability): A user new to the feature can enable tracking, find the history view, and restore a prior version within 2 minutes using only in-app UI.
- **SC-006** (portability): A project committed on Windows, pushed, and cloned on macOS or Linux opens with zero path or line-ending errors and renders identically.
- **SC-007** (degradation): With Git absent, a full pass over the editor's feature surface produces zero versioning-related errors or dialogs beyond the single guidance state.

## Assumptions

- **Git tooling is the user's responsibility in v1.** The feature relies on an installed Git (with a minimum supported version); the app guides installation but does not bundle it.
- **The tracking option on project creation defaults to enabled when Git is detected**, so most users accumulate history passively; the default is adjustable in settings.
- **Automatic snapshots fire on explicit save/save-all/close only** — not on autosave ticks and not on a timer. Continuous autosave already keeps files current; versions mark user-intent points.
- **Repository content is language-independent**: automatic messages are stored in stable English with a machine-readable kind and localized only for display, so repositories survive locale changes and external tools.
- **Save As starts a fresh history for the copy** rather than duplicating the original's repository; the original project keeps its history.
- **Media inside the project (`resources/`) is committed by default**; the large-file extension is used automatically when available, and a size-threshold warning covers its absence.
- **Restore, branch switch, and pull operate on a closed project.** The editor's in-memory state and undo history are per-session; the close/reopen cycle is the only correct way to change files underneath the editor, and undo history loss on reopen is accepted and disclosed.
- **Beutl is the single in-process writer per project.** Concurrent external Git or file writers are not coordinated by Beutl's internal gate, so every close/reopen transition validates ownership and refuses a mismatch. External writes after the final verified ownership point are new operations observed by the repository watcher; snapshot atomicity remains the boundary for arbitrary file writers.

## Dependencies

- An installed Git meeting the minimum supported version, discoverable on the user's system (with a settings override for nonstandard locations).
- Optionally, the Git large-file extension for media-heavy projects.
- The existing project storage model (directory-rooted project, one file per scene/element, autosave-on-edit, atomic file writes) and the existing project open/close lifecycle, which the restore/switch/pull cycle reuses.
- The existing localization pipeline for all user-facing strings.
