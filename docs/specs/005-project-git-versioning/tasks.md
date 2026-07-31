# Tasks: Git Version Control for Editing Projects

**Input**: Design documents from `docs/specs/005-project-git-versioning/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: included — constitution principle III ("new logic in `src/` is incomplete without an accompanying test") makes them mandatory, not optional. Unit suites run real `git` in temp directories with env isolation (research R-14).

**Organization**: grouped by user story (US1–US6 from spec.md) so each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US6 (user-story phases only)

## Phase 1: Setup

**Purpose**: shared configuration and strings every story consumes

- [X] T001 Add `VersionControlConfig` (`ConfigurationBase`; properties per data-model.md) in src/Beutl.Configuration/VersionControlConfig.cs and wire it into `GlobalConfiguration` (`Save`/`Restore`/`AddHandlers`/`RemoveHandlers`) in src/Beutl.Configuration/GlobalConfiguration.cs; NUnit round-trip tests in tests/Beutl.UnitTests/Configuration/VersionControlConfigTests.cs
- [X] T002 [P] Add the new user-facing strings (menu entries, dialogs, snapshot badges, degradation guidance, error dialogs) to src/Beutl.Language/Strings.resx and the ja locale, following the existing resource conventions

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the four serialization fixes (research R-10 — land first so early adopters' commits are clean) and the Avalonia-free git core every story builds on

**⚠️ CRITICAL**: user-story phases must not start before this phase completes

- [X] T003 [P] appVersion churn fix (`feat!:`): persist the loaded `AppVersion` and advance it only when a migration rewrites content, in src/Beutl.Core/Project.cs; update tests/Beutl.UnitTests/ProjectSystem/NoMigrationRegressionTests.cs (plain resave leaves `.bep` byte-identical) and document the `BREAKING CHANGE:` migration rule
- [X] T004 [P] Pin `NewLine = "\n"` in `JsonHelper.WriterOptions`/`SerializerOptions` in src/Beutl.Core/JsonHelper.cs; add a newline byte-stability regression test (all platforms produce LF) in tests/Beutl.UnitTests/ProjectSystem/NoMigrationRegressionTests.cs
- [X] T005 [P] Normalize Scene `Elements` Include/Exclude entries to `/` separators on write and accept both on read in src/Beutl.ProjectSystem/ProjectSystem/Scene.cs; round-trip test proving a Windows-written (`\`) exclude entry still matches on POSIX in tests/Beutl.UnitTests/ProjectSystem/SceneTests.cs
- [X] T006 [P] Add `ElementFileNaming` (`{Id:N}.belm`, `-{index}` collision suffix, matching `DeclarativeDocumentApplier`) in src/Beutl.Editor/Services/ElementFileNaming.cs; replace `RandomFileNameGenerator` at the six GUI call sites (src/Beutl/ViewModels/EditContext/ElementAdderImpl.cs:50,287; src/Beutl.Editor/Services/ElementStructureService.cs:74; src/Beutl.Editor/Services/ElementClipboardService.cs:205,294; src/Beutl.Editor/Services/DuplicateHelper.cs:162); tests for the convention + collisions in tests/Beutl.UnitTests/Editor/ElementFileNamingTests.cs
- [X] T007 [P] Create the VersionControl model types per data-model.md (`GitAvailability`, `RepositoryInfo`, `SnapshotKind`, `CommitInfo`, `FileChange`, `WorkspaceStatus`, `CommitResult`, `RemoteOpResult`, `BranchInfo`, `RemoteInfo`, `GitIdentity`, exceptions) under src/Beutl.Editor/VersionControl/
- [X] T008 Implement `GitInstallationLocator` (ordered probe incl. the macOS CLT-stub check, version floor 2.23, `VersionControlConfig.GitExecutablePath` override, LFS probe) in src/Beutl.Editor/VersionControl/GitInstallationLocator.cs; tests in tests/Beutl.UnitTests/Editor/VersionControl/GitInstallationLocatorTests.cs
- [X] T009 Implement `GitCliRunner` per contracts/git-cli-invocation.md (no shell, env injection, NUL-separated parsing helpers, stderr capture, timeout, cancellation kills the process, stale-lock detection hook) in src/Beutl.Editor/VersionControl/GitCliRunner.cs; tests (args, parsing, typed errors, env, cancellation) in tests/Beutl.UnitTests/Editor/VersionControl/GitCliRunnerTests.cs
- [X] T010 [P] Implement `RepositoryWatcher` (recursive watch on repo root, 500 ms debounce, `.git/`/`**/.beutl/`/`*.tmp` exclusion, background-thread events; modeled on `DirectoryWatcherService` but Avalonia-free) in src/Beutl.Editor/VersionControl/RepositoryWatcher.cs; TimeProvider-based debounce/exclusion tests in tests/Beutl.UnitTests/Editor/VersionControl/RepositoryWatcherTests.cs
- [X] T011 Implement `IProjectVersionControlService` + `GitCliVersionControlService` core per contracts/version-control-service.md (semaphore serialization, `GetAvailabilityAsync`, `GetStatusAsync` with porcelain-v2 parsing incl. `HasConflicts`, `StatusChanged` wiring to the watcher) in src/Beutl.Editor/VersionControl/; real-git test fixture infrastructure (temp repos, `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_NOSYSTEM=1`, fixed dates, `Assert.Ignore` when git absent) + status tests in tests/Beutl.UnitTests/Editor/VersionControl/GitCliVersionControlServiceTests.cs

**Checkpoint**: foundation ready — user stories can begin

---

## Phase 3: User Story 1 - Every save is a restorable version (Priority: P1) 🎯 MVP

**Goal**: opt-in per-project tracking; every explicit save/close records a snapshot; zero git knowledge needed

**Independent Test**: create a tracked project, save after three edits → three versions, each matching the saved state; repeated clean saves add nothing (spec US1 scenarios)

- [X] T012 [US1] Implement `InitializeAsync` (`git init` + `git symbolic-ref HEAD refs/heads/main` for the Git 2.23 floor, generated `.gitignore` `**/.beutl/` + `*.tmp`, `.gitattributes` `eol=lf` + LFS patterns when active, `git lfs install --local`, initial commit with `Beutl-Snapshot: init`) in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; artifact + initial-commit tests in tests/Beutl.UnitTests/Editor/VersionControl/GitCliVersionControlServiceTests.cs
- [X] T013 [US1] Implement `CommitAllAsync` (clean-tree skip → `NoChanges`, `git add -A -- <pathspec>`, `Beutl-Snapshot` trailer for auto kinds, `SkippedNoIdentity` for unattended auto commits) and `GetIdentityAsync`/`SetLocalIdentityAsync` (repo-local only) in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; commit/trailer/identity tests in the same suite
- [X] T014 [US1] Implement `VersionControlCoordinator` (subscribe `ProjectService.ProjectObservable`, per-project service + watcher lifecycle, `NotifySavedAsync`, close-snapshot hook, config gating) in src/Beutl/Services/VersionControlCoordinator.cs, constructed in src/Beutl/ViewModels/MainViewModel.cs
- [X] T015 [US1] Wire triggers and exposure: call the coordinator at the end of `OnSave`/`OnSaveAll` in src/Beutl/ViewModels/MenuBarViewModel.Files.cs, in the project-close flow before `ProjectService.CloseProject()`, and add the `IProjectVersionControlService` branch to `EditViewModel.GetService` in src/Beutl/ViewModels/EditViewModel.cs
- [X] T016 [P] [US1] Add the "Track history with Git" checkbox (visible when git detected, default `VersionControlConfig.EnableForNewProjects`) to src/Beutl/ViewModels/Dialogs/CreateNewProjectViewModel.cs and its dialog XAML; initialize after creation when checked
- [X] T017 [P] [US1] Add the "Enable Version Control…" command (gated on `ProjectService.IsOpened`) to src/Beutl/ViewModels/MenuBarViewModel.Files.cs, src/Beutl/Views/MainView.axaml, src/Beutl/Views/MainView.axaml.InitializeMenuBar.cs, and the command palette in src/Beutl/ViewModels/MenuBarViewModel.Palette.cs
- [X] T018 [P] [US1] Identity prompt dialog (first commit with unset `user.name`/`user.email`; prefill OS username; writes repo-local via `SetLocalIdentityAsync`) under src/Beutl/Views/Dialogs/ + ViewModel with compiled bindings
- [X] T019 [US1] Shell E2E scenario: explicit save on a tracked project produces exactly one snapshot commit (and none when clean) in tests/Beutl.HeadlessUITests/

**Checkpoint**: US1 fully functional — the MVP ("save = version") works end to end

---

## Phase 4: User Story 2 - Browse history and restore (Priority: P1)

**Goal**: history view (list / changed files / diff) and non-destructive whole-project restore

**Independent Test**: 10-version history → restore version 4 → project reopens in version-4 state; all prior versions plus the pre-restore state remain reachable (spec US2 scenarios)

- [X] T020 [US2] Implement history queries: `GetHistoryAsync` (paged `git log … -z` with trailer parse), `GetCommitFilesAsync` (`git show --name-status -z`), `GetDiffAsync` (unified diff, 1 MB cap with truncation marker) in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; paging/trailer/diff tests in the service suite
- [X] T021 [US2] Implement `RestoreWorktreeFromAsync` (`git restore --source=<sha> --worktree -- <pathspec>` + `git clean -fd -- <pathspec>`) in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; tests: restored tree byte-matches the target commit, later-added elements removed, ignored `.beutl/`/`*.tmp` survive the clean
- [X] T022 [US2] Implement the coordinator restore cycle per contracts/coordinator-lifecycle.md (confirm dialog disclosing close/reopen + undo loss, safety snapshot when dirty, close → restore → `Beutl-Snapshot: restore` commit → reopen, failure path reopens the original state, refusal while an export is running) in src/Beutl/Services/VersionControlCoordinator.cs
- [X] T023 [US2] Build the Version Control tool tab: `VersionControlTabExtension` in src/Beutl/Services/PrimitiveImpls/VersionControlTabExtension.cs (registered in src/Beutl/Services/StartupTasks/LoadPrimitiveExtensionTask.cs) + views/viewmodels under src/Beutl.Editor.Components/VersionControlTab/ (status header with branch/ahead-behind/dirty, incrementally loaded history list with kind badges, changed-files pane, monospace +/- diff view; `x:CompileBindings` + `x:DataType` everywhere; `StatusChanged` marshaled to the UI thread); ViewModel tests in tests/Beutl.UnitTests/Editor/VersionControl/
- [X] T024 [P] [US2] "Restore to new branch" context action (`git switch -c <name> <sha>` through the same cycle) in the tab ViewModel + service; test for the created branch state
- [X] T025 [US2] Shell E2E scenario: restore an older version → close/reopen completes, project state matches, undo history cleared, in tests/Beutl.HeadlessUITests/

**Checkpoint**: US1+US2 = the complete safety story (save = version, any version restorable, nothing ever lost)

---

## Phase 5: User Story 3 - Safe coexistence and degradation (Priority: P1)

**Goal**: never corrupt a user's existing repository; fully functional editor without git

**Independent Test**: (a) no git → full editor pass with zero versioning errors + guidance panel; (b) project inside an existing repo → snapshots and restore touch only the project directory, while branch, push, and pull are verified to act on the whole enclosing repository after explicit disclosure (spec US3 scenarios)

- [X] T026 [US3] Repo discovery + nested-repo handling: `git rev-parse --show-toplevel` before init, consent flow ("use enclosing repository" with pathspec scoping + project-local `.gitignore` / "leave unmanaged"), `RepositoryInfo.IsNestedInForeignRepo`/`Pathspec` plumbing through every path-touching call, and explicit disclosure that branch/push/pull act on the whole enclosing repository, in src/Beutl.Editor/VersionControl/ + coordinator consent dialog; nested fixtures (repo root above project) asserting foreign files are never staged, restored, or cleaned by project-scoped operations and that branch/push/pull retain whole-repository semantics, in tests/Beutl.UnitTests/Editor/VersionControl/NestedRepositoryTests.cs
- [X] T027 [P] [US3] Degradation surface: availability drives the tab to a single per-OS guidance state and disables the menu commands (no error dialogs anywhere) in src/Beutl.Editor.Components/VersionControlTab/ + src/Beutl/ViewModels/MenuBarViewModel.Files.cs; availability-state ViewModel tests
- [X] T028 [P] [US3] Stale-lock recovery per contracts/git-cli-invocation.md (detect `index.lock` failure, age + liveness check, consent-gated removal, logged) in src/Beutl.Editor/VersionControl/GitCliRunner.cs; tests with a fabricated stale lock in tests/Beutl.UnitTests/Editor/VersionControl/GitCliRunnerTests.cs
- [X] T029 [US3] Conflicted-state lockout: `HasConflicts` ⇒ mutating members throw `VersionControlConflictedException` with guidance while reads keep working; coordinator surfaces the guidance and warns before opening files containing conflict markers; unmerged-path fixture tests in the service suite

**Checkpoint**: all three P1 stories done — safe to ship as the MVP release

---

## Phase 6: User Story 4 - Manual commits with messages (Priority: P2)

**Goal**: named milestones, visually distinct from automatic snapshots

**Independent Test**: commit with a message between auto snapshots → appears with the message and a distinct badge; clean-tree commit reports "nothing to record" (spec US4 scenarios)

- [X] T030 [US4] Manual commit UI: message box + Commit button in src/Beutl.Editor.Components/VersionControlTab/ (uses `CommitAllAsync(message, Manual)`, `NoChanges` feedback, triggers the identity flow when unset), a "Commit Version…" palette/menu command in src/Beutl/ViewModels/MenuBarViewModel.Files.cs + MenuBarViewModel.Palette.cs, and Manual-vs-auto badge distinction in the history list; ViewModel tests

**Checkpoint**: US4 done — history becomes navigable by milestones

---

## Phase 7: User Story 5 - Branches for experiments (Priority: P2)

**Goal**: create/list/switch branches with the same safety cycle; both lines always intact

**Independent Test**: create a branch, diverge both branches, switch back and forth → each reopens with exactly its own state (spec US5 scenarios)

- [X] T031 [US5] Implement `GetBranchesAsync` (`for-each-ref`), `CreateBranchAsync`, `SwitchBranchAsync` in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; branch create/switch/divergence tests in the service suite
- [X] T032 [US5] Branch UI + cycle: branch dropdown/list + "New branch" dialog in src/Beutl.Editor.Components/VersionControlTab/, coordinator switch cycle (dirty prompt → safety snapshot → close → `git switch` → reopen; failure surfaces stderr and reopens the original branch) in src/Beutl/Services/VersionControlCoordinator.cs; ViewModel tests

**Checkpoint**: US5 done — no merge surface exists beyond fast-forward (FR-028 guardrail holds)

---

## Phase 8: User Story 6 - Remote backup and multi-machine (Priority: P3)

**Goal**: one remote; push with progress; ff-only pull; auth fully delegated

**Independent Test**: push to a local bare "remote", clone elsewhere, open, pull new versions; divergence and auth failures produce the specified guidance (spec US6 scenarios)

- [X] T033 [US6] Implement `GetRemotesAsync`/`SetRemoteAsync`/`PushAsync` (progress from stderr, cancelable)/`PullFastForwardAsync` with `RemoteOpResult` mapping (`Success`/`AuthFailed`/`Diverged`/`Offline`/`Failed`) in src/Beutl.Editor/VersionControl/GitCliVersionControlService.cs; local-bare-remote tests (push, ff pull, divergence) in tests/Beutl.UnitTests/Editor/VersionControl/RemoteOperationsTests.cs
- [X] T034 [US6] Remote UI: URL field, Push/Pull commands with progress + cancel, divergence/auth/offline guidance dialogs, pull via the coordinator cycle, in src/Beutl.Editor.Components/VersionControlTab/ + src/Beutl/Services/VersionControlCoordinator.cs; ViewModel tests
- [X] T035 [P] [US6] LFS + large-media policy: auto-track `resources/**` patterns when LFS active (`UseLfsWhenAvailable`), one-time quota notice on first remote connect with LFS, one-time `LargeMediaWarningThresholdMb` warning without LFS — never blocking; tests for attribute generation and the warning triggers

**Checkpoint**: all six stories functional

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T036 [P] R-8 stress test: scripted 1000-edit burst against a tracked temp project asserts a bounded number of `git status` invocations (watcher debounce + `GIT_OPTIONAL_LOCKS=0` hold) in tests/Beutl.UnitTests/Editor/VersionControl/RepositoryWatcherStressTests.cs
- [X] T037 [P] macOS native menu mirror for the new commands in src/Beutl/Views/MacWindow.axaml.cs and shortcut/palette completeness via `ContextCommandDefinition` in src/Beutl/Services/PrimitiveImpls/MainViewExtension.cs
- [X] T038 Verify SC-002/SC-003 measurably: one-property edit + save touches exactly one `.belm` (assert in a service test); snapshot timing on a 500-element fixture ≤ 2 s; history load ≤ 1 s for 200 commits (timed tests, generous CI margins)
- [X] T039 `dotnet format Beutl.slnx` + `dotnet build Beutl.slnx` + `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings` all green; fix fallout (2026-07-28: format 0 violations after encoding/import fixes; build 0 errors; per-project runs — UnitTests 5,010 pass/7 skip, HeadlessUI 198/198, E2E 80/80, AgentToolkit 527/527, FFmpegIpc 56/56, SourceGenerator 11/11, Graphics3D 5/5, FFmpegWorker 1/1, AVFoundation 12/12, MediaFoundation 55/55; one pre-existing flaky proxy-timing unit test passed on rerun)
- [ ] T040 Run the quickstart.md manual verification matrix (network/credential/LFS/macOS-discovery/notarization rows) and record results in the PR description; include the release-notes callout for the one-time Windows newline diff (R-10.4)

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2 → user stories**: T001 (config) blocks T008/T014/T016; the four serialization fixes T003–T006 are independent of each other and of T007–T011, but all of Phase 2 blocks every story phase.
- **US1 (Phase 3)** blocks **US2** (restore commits via `CommitAllAsync`; the tab hosts later UI), and US2's tab (T023) hosts US4/US5/US6 UI (T030/T032/T034).
- **US3** depends only on Phase 2 (T026–T029 touch discovery/runner/service) plus the tab's degradation state (T027 → after T023; the rest can run parallel to US2).
- **US5/US6** depend on the coordinator cycle from US2 (T022).
- Story order for a single implementer: US1 → US2 → US3 → US4 → US5 → US6 → Polish. Suggested PR slicing: T003–T006 as individual prerequisite PRs (T003 is `feat!:`), then one PR per story phase.

### Parallel opportunities

- Phase 2: T003, T004, T005, T006, T007, T010 in parallel (distinct files); T008/T009/T011 sequential on T007.
- Phase 3: T016, T017, T018 in parallel after T014/T015.
- Phase 5: T027, T028 in parallel; T026/T029 sequential on the service.
- Phase 8: T035 parallel to T033/T034.

## Implementation Strategy

**MVP = Phases 1–5 (US1+US2+US3, all P1)**: "every save is a restorable version, restore never loses anything, and the feature can never hurt users who don't want it". Ship/validate there, then add US4 (milestones), US5 (branches), US6 (remotes) as independent increments. Stop at any checkpoint — each story leaves the product consistent.
