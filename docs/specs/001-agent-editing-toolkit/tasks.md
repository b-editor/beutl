---
description: "Task list for Agent Editing Toolkit implementation"
---

# Tasks: Agent Editing Toolkit — MCP, Skills, and Subagents for AI-Driven Beutl Editing

**Input**: Design documents from `docs/specs/001-agent-editing-toolkit/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/mcp-tools.md](./contracts/mcp-tools.md), [contracts/declarative-document.md](./contracts/declarative-document.md), [quickstart.md](./quickstart.md)

**Tests**: INCLUDED. NUnit tests are explicitly required for this feature — Constitution III (Test-First with NUnit), AGENTS.md mandatory rule 3 ("New logic ships with a NUnit test"), and the quickstart's Tests section. Test tasks are written before the implementation they cover and must fail first.

**Organization**: Tasks are grouped by user story. Phases run in spec priority order — P1 (US1), then P2 (US2, US3, US6), then P3 (US4, US5) — so each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story the task serves (US1–US6); Setup/Foundational/Polish carry no story label
- Every task names an exact file path

## Path Conventions

New code lives under three new MIT `net10.0` projects plus one test project (see plan.md "Project Structure"):

- `src/Beutl.AgentToolkit/` — transport-agnostic core library (also hosts the **shared** MCP tool wrappers)
- `src/Beutl.AgentToolkit.Mcp/` — stdio MCP console server (host + file-session lifecycle tools)
- `src/Beutl.Extensions.FFmpeg.Core/` — non-UI encode core (split out for US4 export)
- `src/Beutl/AgentHost/` — in-app loopback MCP endpoint + the live-session binding (US6)
- `tests/Beutl.AgentToolkit.Tests/` — GPU-free NUnit suite (core only); app-host tests go in `tests/Beutl.HeadlessUITests/`

> **Tool-surface placement (resolves the "identical surface for both hosts" rule, contracts/mcp-tools.md:23-25).** The **edit / query / render / imperative** `[McpServerToolType]` wrappers live in the **core library** `src/Beutl.AgentToolkit/Tools/`, so BOTH the stdio server and the in-app host register the identical surface via `WithToolsFromAssembly(<core assembly>)`. Only **session acquisition** differs per host: `open_project`/`create_project`/`add_scene`/`save_project` are stdio-only (`SessionTools` in the Mcp exe); `attach_active_editor` is in-app-only (`AgentHost`). The core therefore `PackageReference`s the permissive `ModelContextProtocol` for the attributes.

> **Dependency-shape note (verified against the repo).** The headless core is **UI-free in the Constitution-IV sense** — no XAML/UserControls/compiled bindings, no Avalonia app startup, no `Beutl.Controls`/`EditViewModel` reference. It is **not** "Avalonia-package-free": `Beutl.Extensibility` (the encoder abstraction) `PackageReference`s `Avalonia`/`FluentAvaloniaUI`, so the Avalonia *assemblies* arrive transitively (loading the DLLs needs no display). `EditViewModel` lives in the **app** (`src/Beutl/ViewModels/EditViewModel.cs`), so the live binding must NOT be referenced from core (T058/T059).

> **Render GPU classification (verified).** The CPU SkiaSharp fallback in `RenderTarget.Create` (`RenderTarget.cs:56-74`) covers vector/text/unscaled-bitmap **and** SKSL runtime-shader effects (`SKRuntimeEffect.CreateShader`) **and** particles (`RenderTarget.Create`). `rendering_unavailable` is returned **only** for content whose render path hard-requires a GPU context — notably **3D (Graphics3D/Vulkan)**.

> **Undo scope (FR-015, clarified).** Undo of agent edits is **same-session** — Beutl's history is per-`HistoryManager` and not persisted across a close/reopen (identical to human edits). The undo tasks target the in-memory history; cross-reopen replay is out of scope.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new projects, register them, and add packages so any later task can compile.

- [ ] T001 Create `src/Beutl.AgentToolkit/Beutl.AgentToolkit.csproj` (MIT, single-target `net10.0`, `IsPackable=false`) with `ProjectReference`s to `Beutl.Editor`, `Beutl.ProjectSystem`, `Beutl.Engine`, `Beutl.Extensibility` and a `PackageReference` to `ModelContextProtocol` (for the shared tool attributes; the FFmpeg.Core + AVFoundation references are added in **T068/T069**, gated to US4); create the folders `Sessions/`, `Documents/`, `Schema/`, `MergePatch/`, `Reconciliation/`, `Rendering/`, `Workspace/`, `Common/`, `Tools/`
- [ ] T002 Create `src/Beutl.AgentToolkit.Mcp/Beutl.AgentToolkit.Mcp.csproj` (MIT, `net10.0`, `OutputType=Exe`, `IsPackable=false`) referencing the core library + `Microsoft.Extensions.Hosting`, and create the `Tools/` folder (for the stdio-only `SessionTools`)
- [ ] T003 [P] Create `tests/Beutl.AgentToolkit.Tests/Beutl.AgentToolkit.Tests.csproj` (NUnit + Moq, referencing the **core library** not the exe, with `coverlet.runsettings` honored)
- [ ] T004 Register `Beutl.AgentToolkit`, `Beutl.AgentToolkit.Mcp`, and `tests/Beutl.AgentToolkit.Tests` in `Beutl.slnx`
- [ ] T005 [P] Add `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, and `Microsoft.Extensions.Hosting` to `Directory.Packages.props` (central package management; watch for a `Microsoft.Extensions.*` version-conflict warning against the existing 10.0.9 pins — research §5)
- [ ] T006 [P] Confirm the Nuke build/publish handles the first MIT console `Exe` under `src/` — inspect `nukebuild/Build.cs` (the `Publish`/`Zip`/`BundleApp` targets and any `*.Mcp`/worker-specific globs) so `Beutl.AgentToolkit.Mcp` is published as a normal MIT exe and not treated like the GPL `Beutl.FFmpegWorker` (research §5)

**Checkpoint**: Solution builds with two new toolkit projects + the test project wired in. The third new source project, `Beutl.Extensions.FFmpeg.Core`, is **intentionally deferred to Phase 7 (US4 export)** — only the export path needs it; T063 creates and registers it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared substrate every user story builds on — session abstraction, document adapter, typed errors, the workspace + destructive-write guards, validation evaluator, schema-version stamp, and the MCP host skeleton.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete. The destructive-write guard (T011/T016) is foundational so every later write/delete tool (`create_project` overwrite, `save_project`, `remove_element`, render/export output) is gated on it from the start (FR-024), not retrofitted.

### Tests for Foundation ⚠️ (write first, must fail)

- [ ] T007 [P] Workspace-guard test in `tests/Beutl.AgentToolkit.Tests/Workspace/WorkspaceGuardTests.cs` — in-root ok, `..` escape rejected, in-root symlink-to-outside rejected (symlink fixtures self-skip on Windows without Developer Mode) (FR-026; quickstart Tests)
- [ ] T008 [P] Document round-trip test in `tests/Beutl.AgentToolkit.Tests/Documents/DocumentRoundTripTests.cs` — serialize → normalize → de-normalize → re-serialize preserves 100% of content incl. unrecognized keys (FR-013/SC-003)
- [ ] T009 [P] Validation-evaluator test in `tests/Beutl.AgentToolkit.Tests/Reconciliation/ValidationEvaluatorTests.cs` — covers **both** property systems: a CoreProperty with `[Range]` and an engine `IProperty` with `[Range]`; in-range ⇒ `ok`, out-of-range ⇒ `coerced` with bounds, invalid ⇒ `rejected` (FR-007/SC-002)
- [ ] T010 [P] Recording-pipeline test in `tests/Beutl.AgentToolkit.Tests/Sessions/RecordingPipelineTests.cs` — a live mutation through the trio (`OperationSequenceGenerator` + `HistoryManager` + `CoreObjectOperationObserver`) records one undoable operation that `Undo()` reverts **in the same session** (FR-015)
- [ ] T011 [P] Destructive-write guard test in `tests/Beutl.AgentToolkit.Tests/Workspace/DestructiveGuardTests.cs` — overwriting an existing project file (incl. via `create_project`) or deleting content requires an explicit/confirmed flag; without it the guard rejects (typed `destructive_intent`), with it the guard allows (FR-024)
- [ ] T012 [P] Schema-version-stamp unit test in `tests/Beutl.AgentToolkit.Tests/Common/SchemaVersionTests.cs` — a document carries `schemaVersion`; a submission whose version is unknown to the runtime raises `schema_version_mismatch` (FR-031) — written before T020

### Implementation for Foundation

- [ ] T013 [P] Implement the typed result/error model in `src/Beutl.AgentToolkit/Common/ToolResult.cs` and `src/Beutl.AgentToolkit/Common/ErrorCode.cs` — the stable codes `workspace_boundary`, `validation_rejected`, `media_not_found`, `unknown_type`, `stale_handle`, `rendering_unavailable`, `codec_unavailable`, `schema_version_mismatch`, `no_active_editor_session`, `destructive_intent`, `project_conflict` (FR-014; contracts/mcp-tools.md "Cross-cutting")
- [ ] T014 [P] Implement identity + discriminator helpers in `src/Beutl.AgentToolkit/Common/IdentityHelper.cs` — `ICoreObject.FindById(guid)` wrapper and `$type` read/write via the public `JsonHelper.WriteDiscriminator`/`TryGetDiscriminator` (NOT the internal `TypeFormat`) (research §3/§4)
- [ ] T015 [P] Implement `IWorkspaceGuard` + `WorkspaceBoundaryException` in `src/Beutl.AgentToolkit/Workspace/WorkspaceGuard.cs` — `ResolveForWrite`: `Path.GetFullPath` → deepest-existing-ancestor `ResolveLinkTarget(returnFinalTarget:true)` → re-append remainder → trailing-separator containment with per-OS case sensitivity; reads bypass entirely (FR-026; research §6)
- [ ] T016 [P] Implement the destructive-write guard in `src/Beutl.AgentToolkit/Workspace/DestructiveGuard.cs` — a single chokepoint that rejects an overwrite of an existing project file (incl. `create_project` onto an existing path) or a content deletion unless the caller passes an explicit confirm flag (raises `destructive_intent`); `create_project`/`save_project`/`remove_element`/render-export output route through it (FR-024; satisfies T011)
- [ ] T017 Implement the session abstraction + source seam in `src/Beutl.AgentToolkit/Sessions/IEditingSession.cs` and `src/Beutl.AgentToolkit/Sessions/ISessionSource.cs`, plus the recording-trio setup helper `src/Beutl.AgentToolkit/Sessions/RecordingPipeline.cs` — `Source` ∈ `File` | `LiveEditor` (mirroring `EditViewModel.cs:117-127`) (FR-032; data-model §Editing Session)
- [ ] T018 Implement the normalized Declarative Document adapter in `src/Beutl.AgentToolkit/Documents/DocumentAdapter.cs` — `CoreSerializer.SerializeToJsonObject(root)` ⇄ the inline `Elements` view, mapping the Scene's on-disk Include/Exclude glob → `.belm` representation to/from the inline array (`Scene` serializes under key `Elements`; CLR `Children` is `[NotAutoSerialized]`) (FR-005/FR-013; contracts/declarative-document.md §1)
- [ ] T019 [P] Implement the validation-outcome evaluator in `src/Beutl.AgentToolkit/Reconciliation/ValidationEvaluator.cs` — run validators **explicitly** to compute `ok` | `coerced` (clamped value + range) | `rejected` (reason), across **both** property systems: CoreProperty via the public `ICorePropertyMetadata.GetValidator()` (the concrete `GetValidator()` is `protected internal`), and engine `IProperty` via `property.CreateValidator(property.GetAttributes() ?? [])` (`GetAttributes()` returns `Attribute[]?`). Compute the outcome rather than infer it post-mutation (FR-007; research §3)
- [ ] T020 [P] Implement the schema-version stamp + mismatch check in `src/Beutl.AgentToolkit/Common/SchemaVersion.cs` — emit `schemaVersion` on every document and raise `schema_version_mismatch` when a submission's version is unknown (satisfies T012) (FR-031)
- [ ] T021 Implement the MCP host skeleton in `src/Beutl.AgentToolkit.Mcp/Program.cs` — `Host.CreateApplicationBuilder` → `AddMcpServer().WithStdioServerTransport()` → register **both** assemblies by a type that already exists here: `WithToolsFromAssembly(typeof(Beutl.AgentToolkit.Common.ToolResult).Assembly)` (the shared core surface — scanned at runtime, so tool classes added in US1+ are picked up without a compile-time reference) and `WithToolsFromAssembly(typeof(Program).Assembly)` (this exe's `SessionTools`, added in T026). DI registration; **STDERR-only logging** (`LogToStandardErrorThreshold = LogLevel.Trace`) so STDOUT stays the JSON-RPC channel (FR-021; research §1)

**Checkpoint**: Foundation ready — session, document, errors, both guards, validation, schema-version, and the MCP host all build (zero tools registered until US1 adds them). User-story tools can now be implemented.

---

## Phase 3: User Story 1 - Author a project from a brief (Priority: P1) 🎯 MVP

**Goal**: Turn a natural-language brief into a saved Beutl project — create the project/scene with the right canvas/rate/duration, add timeline elements with content at the right times/layers, set core properties, and save a file that opens cleanly in Beutl.

**Independent Test**: Feed the agent a set of briefs, have it author projects (via `create_project` + full-document `apply_edit`), open each in Beutl, and confirm the structure matches the brief.

### Tests for User Story 1 ⚠️ (write first, must fail)

- [ ] T022 [P] [US1] Reconcile-full-document test in `tests/Beutl.AgentToolkit.Tests/Reconciliation/FullDocumentApplyTests.cs` — a desired document yields `set-property` + `insert-child` ops and applies inside `ExecuteInTransaction` (property set; Element into `Elements`; `EngineObject` into `Objects`; omitted-`Id` ⇒ mint); **includes a mid-reconcile failure case proving `ExecuteInTransaction` rolls the live model back** (FR-012/FR-029) — written before the reconciler T027
- [ ] T023 [P] [US1] Authoring integration test in `tests/Beutl.AgentToolkit.Tests/Sessions/AuthorFromBriefTests.cs` — create_project → apply full desired document → save → reload via `CoreSerializer` and assert scene dimensions/duration + one element per requested item at the requested time/layer (Acceptance 1–2); `add_scene` adds a second scene that reloads; `create_project` onto an existing path without confirm ⇒ `destructive_intent`; internally-inconsistent timing ⇒ typed warning (Acceptance 3) (FR-002/FR-024)

### Implementation for User Story 1

- [ ] T024 [US1] Implement the file-opened session source in `src/Beutl.AgentToolkit/Sessions/FileSessionSource.cs` — open via `CoreSerializer.RestoreFromUri`, wire its own recording trio, expose the Scene root; `IsDirty` + source-file last-write stamp (for T083) (data-model §Editing Session)
- [ ] T025 [US1] Implement project-level operations in `src/Beutl.AgentToolkit/Sessions/ProjectOperations.cs` — `create_project` (frameSize/frameRate/duration) and `add_scene` (frame size/start/duration) as file-level actions outside any scene's undo stack (FR-001/FR-002; data-model §Editing Session "Scope")
- [ ] T026 [US1] Implement the stdio-only session-lifecycle tools in `src/Beutl.AgentToolkit.Mcp/Tools/SessionTools.cs` — `open_project` (read, unrestricted; surfaces `media_not_found`/`schema_version_mismatch`); `create_project` (write, routed through `IWorkspaceGuard` **and** the destructive-write guard T016 on path collision); `add_scene` (FR-002, project-level); `save_project` via `CoreSerializer.StoreToUri` through `IWorkspaceGuard` + the destructive-write guard for overwrites (FR-001/FR-002/FR-024/FR-026; contracts/mcp-tools.md "open_project"/"create_project"/"add_scene"/"save_project")
- [ ] T027 [US1] Implement the Id-diff reconciler core in `src/Beutl.AgentToolkit/Reconciliation/Reconciler.cs` — structural diff current-vs-desired keyed by `CoreObject.Id` + property name → minimal change set (`set-property`, `insert-child`), mutate the live attached instance at leaf granularity inside `HistoryManager.ExecuteInTransaction` (commit-on-success / **rollback-on-exception**) (FR-029; research §3; tested by T022 incl. its rollback case)
- [ ] T028 [P] [US1] Implement collection-insert by identity in `src/Beutl.AgentToolkit/Reconciliation/CollectionReconciler.cs` — match by `Id`, deserialize a new member into a fresh instance, `Insert`/`Add` onto the live `ICoreList` (Scene `Elements`, Element `Objects`); mint a Guid when `Id` is omitted (FR-003; contracts/declarative-document.md §1 Id rules)
- [ ] T029 [P] [US1] Implement element content binding in `src/Beutl.AgentToolkit/Documents/ContentFactory.cs` — set an element's content for image/video/text/shape/group and audio source, binding a local media path where applicable (FR-004)
- [ ] T030 [US1] Implement the `apply_edit` tool (full-document `desired` path) in `src/Beutl.AgentToolkit/Tools/EditTools.cs` (shared core surface) — routes through the reconciler; report validation outcomes (FR-007/FR-012; contracts/mcp-tools.md)
- [ ] T031 [P] [US1] Implement the `add_element` imperative assist in `src/Beutl.AgentToolkit/Tools/ElementTools.cs` (shared core surface) — builds a one-entry change set routed through the same reconcile/commit path (FR-003/FR-027)

**Checkpoint**: An agent can open or author a project (incl. multiple scenes) from a full desired document and save it; it opens in Beutl. MVP complete.

---

## Phase 4: User Story 2 - Inspect and refine an existing project (Priority: P2)

**Goal**: Read an existing project as a structured document, then apply targeted edits (property/keyframe/effect/structural) via merge-patch or full document, with a dry-run plan, stable handles, and atomic rollback.

**Independent Test**: Take representative existing projects, issue scripted edits ("retime X by +2s", "add a fade-out on Y"), and confirm the saved project reflects exactly that change, still opens, and nothing else changed.

### Tests for User Story 2 ⚠️ (write first, must fail)

- [ ] T032 [P] [US2] Merge-patch edge-case battery in `tests/Beutl.AgentToolkit.Tests/MergePatch/MergePatchTests.cs` — null-delete; nested object; id-keyed array: `$delete`, `$delete` on missing `Id` ⇒ idempotent no-op, omitted-`Id` insert, unknown-`Id` ⇒ `stale_handle`, same-`Id`/different-`$type` ⇒ `validation_rejected`, ordering `$index`/`$after`/`$before` ⇒ `move-child`, multiple directives ⇒ `validation_rejected`, `$after`/`$before` naming a non-existent sibling ⇒ `stale_handle`; scalar/non-id array wholesale-replace (FR-028; contracts/declarative-document.md §2)
- [ ] T033 [P] [US2] Plan==apply parity test in `tests/Beutl.AgentToolkit.Tests/Reconciliation/PlanApplyParityTests.cs` — `plan_edit` change-set + validation equals the subsequent `apply_edit` for a representative edit set; `expectedChangeSet` divergence is rejected (SC-009)
- [ ] T034 [P] [US2] Merge-patch-path rollback test in `tests/Beutl.AgentToolkit.Tests/Reconciliation/TransactionRollbackTests.cs` — a mid-batch failure in a multi-step patch apply leaves the live model exactly at its pre-batch state (the full-document rollback is already covered by T022; this covers the patch path) (FR-012)
- [ ] T035 [P] [US2] Keyframe + effect + structural-op tests in `tests/Beutl.AgentToolkit.Tests/Reconciliation/KeyframeEffectStructureTests.cs` — keyframe sorted-insert/remove keeps time order; effect attach/reorder/remove by `Id`; move/resize/duplicate/split/group/ungroup; remove_element; an audio property + audio effect edit through the generic surface (FR-008/FR-009/FR-010/FR-025)
- [ ] T036 [P] [US2] `read_document` test in `tests/Beutl.AgentToolkit.Tests/Tools/ReadDocumentTests.cs` — full-document read returns the normalized inline view; `rootId` returns just that subtree (bounded payload); a stale/unknown `rootId` ⇒ `stale_handle` — written before T038 (FR-005)

### Implementation for User Story 2

- [ ] T037 [P] [US2] Implement the RFC 7396 + id-keyed merge-patch applier in `src/Beutl.AgentToolkit/MergePatch/MergePatch.cs` — object recurse (null deletes); id-keyed arrays (match by `Id`, untouched siblings, `$delete` removes one, ordering directives, keyframes by `KeyTime`); DeepClone before reparenting; scalar/non-id arrays wholesale-replace (FR-028; research §4)
- [ ] T038 [US2] Implement `read_document` in `src/Beutl.AgentToolkit/Tools/QueryTools.cs` (shared core surface) — normalized document with optional `rootId` subtree scoping for bounded payloads; unknown `rootId` ⇒ `stale_handle` (satisfies T036) (FR-005; contracts/mcp-tools.md)
- [ ] T039 [US2] Extend the reconciler with `plan_edit` (dry-run) in `src/Beutl.AgentToolkit/Reconciliation/PlanGenerator.cs` — reconcile on a **deep clone** of the root (no live events), emit change set + validation results, no mutation (FR-030; research §3)
- [ ] T040 [US2] Wire `plan_edit`/`apply_edit` patch path + `expectedChangeSet` enforcement into `src/Beutl.AgentToolkit/Tools/EditTools.cs` — merge-patch → desired document → Id-diff → transaction; reject on `expectedChangeSet` divergence (SC-009; contracts/mcp-tools.md)
- [ ] T041 [P] [US2] Implement keyframe reconcile in `src/Beutl.AgentToolkit/Reconciliation/KeyframeReconciler.cs` — sorted insert via `animation.KeyFrames.Add(keyframe, out _)` + `Remove`/`RemoveAt` (the engine `KeyFrames : HierarchicalList<IKeyFrame>` collection directly; the UI-only `AnimationOperations.*` helpers in `Beutl.Editor.Components` are NOT used headlessly) (FR-008; research §3)
- [ ] T042 [P] [US2] Implement the keyframe assists `add_keyframe`/`update_keyframe`/`remove_keyframe` in `src/Beutl.AgentToolkit/Tools/KeyframeTools.cs` (shared core surface) (FR-008; contracts/mcp-tools.md)
- [ ] T043 [P] [US2] Implement effect reconcile + the `attach_effect`/`remove_effect`/`reorder_effect` assists in `src/Beutl.AgentToolkit/Tools/EffectTools.cs` (shared core surface) — visual filter + audio effects by `Id`/index (FR-009)
- [ ] T044 [P] [US2] Implement the structural imperative assists in `src/Beutl.AgentToolkit/Tools/StructureTools.cs` (shared core surface) — `remove_element` (routes the delete through the destructive-write guard T016), `move_element`/`resize_element`/`duplicate_element`/`split_element`/`group_elements`/`ungroup_element`, each a one-entry change set through the reconcile/commit path (FR-003/FR-010/FR-024; contracts/mcp-tools.md)
- [ ] T045 [P] [US2] Implement `set_property` and `undo`/`redo` in `src/Beutl.AgentToolkit/Tools/PropertyTools.cs` (shared core surface) — `set_property` returns the validation outcome; undo/redo call `HistoryManager.Undo()`/`Redo()` on the **same-session** history (FR-007/FR-015)
- [ ] T046 [US2] Implement stable-handle + stale-handle semantics in the shared tool base (`src/Beutl.AgentToolkit/Tools/ToolBase.cs`) — every `*Id` is a `CoreObject.Id` valid for the session; a removed target ⇒ `stale_handle` (FR-011; contracts/mcp-tools.md "Stable handles")

**Checkpoint**: An agent can read, plan, and atomically apply partial or full edits to an existing project, including keyframes, effects, structure, and audio — undoable and validated.

---

## Phase 5: User Story 3 - Discover capabilities and get safe, actionable feedback (Priority: P2)

**Goal**: Enumerate every editable type and its parameters (type/unit/range/default/animatable), and return typed, actionable errors for invalid edits instead of silent no-ops or clamps.

**Independent Test**: Query the capability surface and diff it against what the GUI exposes for the same types; feed a battery of invalid edits and confirm each returns a typed, descriptive error and leaves the project uncorrupted.

### Tests for User Story 3 ⚠️ (write first, must fail)

- [ ] T047 [P] [US3] Schema-completeness test in `tests/Beutl.AgentToolkit.Tests/Schema/SchemaGenerationTests.cs` — every built-in (and a fake installed-extension) GUI-editable type is discoverable with its parameters' type/unit/range/default/animatable; base fields present (SC-005; FR-006/FR-022)
- [ ] T048 [P] [US3] Invalid-edit battery in `tests/Beutl.AgentToolkit.Tests/Schema/SafeFeedbackTests.cs` — out-of-range ⇒ typed `coerced`/`rejected` with readable range; missing media ⇒ `media_not_found`; uninstalled effect type ⇒ `unknown_type`; 100% typed errors, 0% corruption (SC-002; FR-007)

### Implementation for User Story 3

- [ ] T049 [US3] Implement the schema generator in `src/Beutl.AgentToolkit/Schema/SchemaGenerator.cs` — instantiate each registered type, read `EngineObject.Properties` (`IProperty.Name`/`ValueType`/`DefaultValue`/`IsAnimatable`/`SupportsExpression`; attributes via `IProperty.GetAttributes()` → `[Display]`/`[Range]`/`[NumberStep]`) + base `PropertyRegistry.GetRegistered(type)` fields + `LibraryService` category + `$type` discriminator; record a `converter` note for custom `JsonConverter`s (FR-006; research §4; contracts/declarative-document.md §3)
- [ ] T050 [US3] Implement headless type-universe registration in `src/Beutl.AgentToolkit/Schema/TypeRegistration.cs` — **run the app-side registrar equivalent at host startup to populate `LibraryService.Current`** (this single approach covers built-in **and** installed-extension types with their categories, satisfying FR-022; bare reflection of `EngineObject`/`CoreObject` subclasses is rejected because it misses extension-supplied registration/category metadata) (plan.md Key risks "Type universe registration")
- [ ] T051 [US3] Implement the `get_schema` tool in `src/Beutl.AgentToolkit/Tools/QueryTools.cs` (shared core surface) — full catalog or filtered by `$type`/`category`; `unknown_type` on a bad filter (FR-006; contracts/mcp-tools.md)
- [ ] T052 [US3] Wire typed errors through the US1–US3 tools — surface `media_not_found` (missing source file) in `src/Beutl.AgentToolkit/Tools/ElementTools.cs`, `unknown_type` (uninstalled drawable/effect) in `src/Beutl.AgentToolkit/Tools/EditTools.cs`, and `validation_rejected` (validator outcome) via the shared `src/Beutl.AgentToolkit/Tools/ToolBase.cs`, each naming the offending file/type and leaving the project unmodified (the render/export `media_not_found`/`codec_unavailable` wiring lives in `RenderTools.cs`, T074) (FR-014; spec Edge Cases)

**Checkpoint**: The surface is self-describing and fails loudly — US1/US2 edits are now trustworthy.

---

## Phase 6: User Story 6 - Watch the agent edit live in the editor (Priority: P2)

**Goal**: Host the toolkit core in-process in the running editor so agent edits land on the live `Scene` + `HistoryManager` the UI is bound to — preview/timeline/property panels update live and each change is one normal undoable entry.

**Independent Test**: Open a project in the editor, drive an edit through the in-process endpoint, and confirm the UI updates without a manual reload and that the change is a single undoable history entry — without simulating any UI input.

### Tests for User Story 6 ⚠️ (write first, must fail)

- [ ] T053 [P] [US6] Core live-session test in `tests/Beutl.AgentToolkit.Tests/Sessions/LiveSessionTests.cs` — drive `LiveSessionSource` through a fake `ILiveSessionBinding` over a bare `Scene` + `HistoryManager`, apply an edit, assert it lands as exactly one entry on that history and `Undo()` reverts it identically to a human edit (same session); binding-absent ⇒ typed `no_active_editor_session` (FR-032/FR-034/FR-035; asserts model + history, NOT pixels)
- [ ] T054 [P] [US6] App-host endpoint lifecycle/security test in `tests/Beutl.HeadlessUITests/AgentHostEndpointTests.cs` (the only suite that may reference `src/Beutl`) — the endpoint binds `127.0.0.1` only, selects a free port, issues a per-session token, starts/stops cleanly within the host lifecycle, and `attach_active_editor` returns `no_active_editor_session` when nothing is open (FR-035)
- [ ] T055 [US6] SC-010 manual-verification note in `docs/specs/001-agent-editing-toolkit/quickstart.md` (and recorded in the implementing PR) — open a project in the editor, drive one `apply_edit` through the endpoint, and visually confirm preview/timeline/property-panel update with no reload and exactly one undo entry (the GPU-dependent UI repaint cannot be asserted GPU-free; SC-010/FR-033)

### Implementation for User Story 6

- [ ] T056 [US6] Wire the in-app host into the app project — add a `ProjectReference` from `src/Beutl/Beutl.csproj` to `Beutl.AgentToolkit` and a `PackageReference` to `ModelContextProtocol.AspNetCore` (gated before T058/T059) (research §7)
- [ ] T057 [US6] Define the **app-agnostic** live-session adapter in core — `src/Beutl.AgentToolkit/Sessions/ILiveSessionBinding.cs` (exposes the active `Scene`, its `HistoryManager`, and a dispatcher/`post` abstraction) and `src/Beutl.AgentToolkit/Sessions/LiveSessionSource.cs` that binds over that interface (NO `EditViewModel` reference — `EditViewModel` is in the app, so referencing it from core would be an upward/circular dependency); mutations are marshaled onto the binding's writer thread, so the agent is an additional command source, not a second writer (FR-032/FR-033/FR-034; data-model §Editing Session)
- [ ] T058 [US6] Implement the `EditViewModel`-specific binding in the app — `src/Beutl/AgentHost/EditViewModelLiveBinding.cs` implements `ILiveSessionBinding` over the active `EditViewModel` (its `Scene` + `HistoryManager` + Avalonia dispatcher), invalidating when the scene closes (FR-033; keeps the app→core direction)
- [ ] T059 [US6] Implement the in-app loopback endpoint + `attach_active_editor` in `src/Beutl/AgentHost/AgentHostEndpoint.cs` and `src/Beutl/AgentHost/AgentHostTools.cs` — self-hosted Kestrel HTTP/SSE via `ModelContextProtocol.AspNetCore` bound to `127.0.0.1` only (free port, per-session token, clean lifecycle); register the **shared core** tools assembly (`WithToolsFromAssembly`) so the live host exposes the identical edit/query/render surface, plus the in-app-only `attach_active_editor` (binds a `LiveEditor` session via `EditViewModelLiveBinding`; `no_active_editor_session` when nothing is open). Validation (FR-007), atomicity (FR-012), and the workspace boundary (FR-026) still hold in live mode (FR-035; contracts/mcp-tools.md)

**Checkpoint**: A creator watches agent edits appear live in the editor and can undo/redo them like their own.

---

## Phase 7: User Story 4 - Render a project to verify the result (Priority: P3)

**Goal**: Render a still frame (no GPL component) and export video (through the existing IPC encoder path) headlessly, with typed "unavailable" errors only when content genuinely needs a missing GPU/codec.

**Independent Test**: Author a project, request a still frame at a time and confirm the image depicts the expected composition; separately request a short video export and confirm a playable file via the existing encoder path.

### Tests for User Story 4 ⚠️ (write first, must fail)

- [ ] T060 [P] [US4] Define a **local** GPU-availability helper `tests/Beutl.AgentToolkit.Tests/Rendering/AgentToolkitGpuTestEnvironment.cs` — the repo's `VulkanTestEnvironment`/`GpuTestEnvironment` are `internal` to other test assemblies and cannot be referenced (verified); duplicate the established pattern (the existing `GpuTestEnvironment` itself documents being "duplicated from VulkanTestEnvironment"), exposing `IsAvailable`/`EnsureAvailable` over `GraphicsContextFactory.GetOrCreateShared()`
- [ ] T061 [US4] Still-render test in `tests/Beutl.AgentToolkit.Tests/Rendering/RenderStillTests.cs` (depends on T060) — **CPU-safe content renders successfully via the CPU SkiaSharp fallback even with no GPU**: vector/text/unscaled bitmap, an **SKSL runtime-shader effect**, and a **particle** still (output path resolved through `IWorkspaceGuard`); only a **GPU-requiring** still (3D / Graphics3D) on a GPU-less host ⇒ `rendering_unavailable`; the GPU-backed success path uses the local helper (T060) to `Assert.Ignore` when no Vulkan/MoltenVK (FR-016/FR-018)
- [ ] T062 [P] [US4] Worker-free export-orchestration test in `tests/Beutl.AgentToolkit.Tests/Rendering/ExportOrchestrationTests.cs` — drive `EncodingController.Encode` over a fake `System.IO.Pipes` host (as `tests/Beutl.FFmpegIpc.Tests`); cover the headless **encoder-registration** selection (built-in FFmpeg encoder resolved) and a missing-encoder failure (`codec_unavailable`); missing FFmpeg native libs ⇒ `codec_unavailable` (FR-017/FR-018/FR-022; covers T070)

### Implementation for User Story 4 — FFmpeg.Core split (atomic sub-tasks T063–T067)

- [ ] T063 [US4] Create the project shell `src/Beutl.Extensions.FFmpeg.Core/Beutl.Extensions.FFmpeg.Core.csproj` (MIT, `net10.0`, **no `Beutl.Controls` / no app-UI**, references `Beutl.Extensibility` + `Beutl.ProjectSystem` + `Beutl.FFmpegIpc`) and register it in `Beutl.slnx` (research §5)
- [ ] T064 [US4] Move the headless encode dependency closure into `src/Beutl.Extensions.FFmpeg.Core/Encoding/` — from `src/Beutl.Extensions.FFmpeg/Encoding/`: `FFmpegEncodingControllerProxy.cs`, `FFmpegEncodingSettings.cs`, `FFmpegVideoEncoderSettings.cs`, `FFmpegAudioEncoderSettings.cs`, `CodecRecord.cs`; plus the support types `FFmpegWorkerProcess.cs`, `FFmpegWorkerLogPump.cs`, `FFmpegWorkerCodecCache.cs` (update namespaces; depends on T063)
- [ ] T065 [US4] Extract the UI-free libraries-missing abstraction in `src/Beutl.Extensions.FFmpeg.Core/FFmpegLibraryState.cs` — `FFmpegWorkerProcess` currently reads `FFmpegInstallNotifier.IsLibrariesMissing` and the proxy calls `FFmpegInstallNotifier.NotifyMissing()` (Avalonia `Dispatcher`/`NotificationService`); replace those with the UI-free state + a typed missing-libraries signal (→ `codec_unavailable`), leaving NO Avalonia reference in Core (depends on T064)
- [ ] T066 [US4] Update the existing `src/Beutl.Extensions.FFmpeg/Beutl.Extensions.FFmpeg.csproj` to `ProjectReference` the new `Beutl.Extensions.FFmpeg.Core` (MIT→MIT) and rewire its remaining call sites; keep `FFmpegInstallNotifier` (the Avalonia toast) here, now subscribing to `FFmpegLibraryState` (T065). The extension keeps only its Avalonia settings-editor UI (depends on T065)
- [ ] T067 [US4] Update the **GPL worker's source links**: in `src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj`, change the `<Compile Include>` **`Include`** paths for `FFmpegEncodingSettings.cs`, `FFmpegVideoEncoderSettings.cs`, `FFmpegAudioEncoderSettings.cs`, `CodecRecord.cs` to the new `..\Beutl.Extensions.FFmpeg.Core\Encoding\` location (keep the `Link="Linked\…"` metadata stable; leave the `Decoding/*` includes untouched). MIT source-link includes, NOT a `ProjectReference` — the GPL/MIT hook is unaffected (depends on T064; FR-023)

### Implementation for User Story 4 — toolkit export wiring

- [ ] T068 [US4] Add a `ProjectReference` from `src/Beutl.AgentToolkit/Beutl.AgentToolkit.csproj` to the **completed** `Beutl.Extensions.FFmpeg.Core` (depends on T063–T067 — the full split incl. the GPL-worker source-link update; gated before T072); confirm no `ProjectReference` to `Beutl.FFmpegWorker` and no reference to the Avalonia-coupled `Beutl.Extensions.FFmpeg` (the `check-gpl-mit-boundary.sh` hook enforces the worker rule) (FR-023; research §5)
- [ ] T069 [US4] Add a `ProjectReference` from `src/Beutl.AgentToolkit/Beutl.AgentToolkit.csproj` to `Beutl.Extensions.AVFoundation` (sequenced **after T068** — same csproj, so NOT `[P]`). It is **already** non-UI MIT (references only `Beutl.Extensibility`, no `Beutl.Controls`), so **no split is needed** (unlike FFmpeg); its `AVFEncodingExtension`/`AVFEncodingController` are `[SupportedOSPlatform("macos")]`, so guard their registration to macOS in T070 (FR-022; supersedes the design-doc "split AVFoundation" note — see plan.md/research.md §2)
- [ ] T070 [US4] Implement a public headless encoder-registration service in `src/Beutl.AgentToolkit/Rendering/EncoderRegistration.cs` (depends on T068/T069 for the encoder refs) — factor the built-in/installed-encoder discovery out of the app-startup `LoadPrimitiveExtensionTask` / internal `PackageManager` so it runs without the GUI, behind the `Beutl.Extensibility.EncodingController`/`ControllableEncodingExtension` abstraction; register the FFmpeg (all platforms) and AVFoundation (macOS only) encoders (tested by T062) (FR-022/FR-023; research §2)
- [ ] T071 [P] [US4] Relocate `FrameProviderImpl` (`src/Beutl/Models/FrameProviderImpl.cs`) and `SampleProviderImpl` (`src/Beutl/Models/SampleProviderImpl.cs`) into `Beutl.Editor` (they are Avalonia-free) so the headless export host does not reference the app; update their namespaces + the app call sites (research §2; plan.md "Relocated")
- [ ] T072 [US4] Implement video export in `src/Beutl.AgentToolkit/Rendering/VideoExporter.cs` (depends on **T070** for the registered encoder + **T071** for the providers) — build `SceneRenderer` + `FrameProviderImpl` + `SceneComposer` + `SampleProviderImpl` and call `EncodingController.Encode(frameProvider, sampleProvider, ct)`; FFmpeg-native-libs preflight ⇒ `codec_unavailable`; reuse the existing `ClampWorkingScaleToBufferBudget` (FR-017/FR-018; research §2)
- [ ] T073 [P] [US4] Implement still rendering in `src/Beutl.AgentToolkit/Rendering/StillRenderer.cs` — `SceneRenderer` → `Compositor.EvaluateGraphics(time + scene.Start)` → `Render(frame)` → `Snapshot()` → `Bitmap.Save(..., Png)` on `RenderThread.Dispatcher`. **CPU-safe content (incl. SKSL effects and particles) uses the CPU SkiaSharp fallback and returns normally** (`RenderTarget.Create` already falls back when no graphics context exists); return `rendering_unavailable` **only** for content whose render path hard-requires a GPU context (3D / Graphics3D) on a GPU-less host (FR-016/FR-018; research §2)
- [ ] T074 [US4] Implement the `render_still` and `export_video` MCP tools in `src/Beutl.AgentToolkit/Tools/RenderTools.cs` (shared core surface) — both resolve `outputPath` through `IWorkspaceGuard` + the destructive-write guard (overwrite); surface `media_not_found`/`rendering_unavailable`/`codec_unavailable`/`workspace_boundary` (FR-016/FR-017/FR-018/FR-024/FR-026; contracts/mcp-tools.md)

**Checkpoint**: An agent can verify its work with a still and a short video export through the GPL-safe path.

---

## Phase 8: User Story 5 - Packaged editing know-how and specialists (Priority: P3)

**Goal**: Ship discoverable editing recipes (Skills) and scoped specialists (Subagents) so agents follow Beutl's conventions without reading source.

**Independent Test**: With only the shipped guidance, have an agent complete a representative editing task end-to-end following the documented recipe; invoke a packaged specialist on a scoped sub-task and confirm it completes in isolation.

### Validation for User Story 5 ⚠️ (define first)

- [ ] T075 [US5] Author a guidance-conformance manual-validation checklist in `docs/specs/001-agent-editing-toolkit/checklists/guidance-validation.md` — a representative editing task an agent must complete using only the shipped Skills/Subagents (no Beutl source reading), reaching a valid generated project under 15 minutes (SC-006); recorded in the implementing PR

### Implementation for User Story 5

- [ ] T076 [P] [US5] Author two editing-recipe Skills — `.claude/skills/beutl-agent-timeline-from-shotlist/SKILL.md` (lay out a timeline from a shot list) and `.claude/skills/beutl-agent-look-effect-chain/SKILL.md` (apply a consistent look/effect chain) — each documenting the declarative loop, the id-keyed array-merge rule, PascalCase property keys, and in-range values (FR-019; quickstart "guidance pillar")
- [ ] T077 [P] [US5] Author two scoped Subagents — `.claude/agents/beutl-agent-timeline-builder.md` (builds a timeline from a shot list) and `.claude/agents/beutl-agent-look-applier.md` (applies a look/effect chain) — for delegated editing sub-tasks an orchestrator can compose (FR-020)
- [ ] T078 [US5] Extend `docs/specs/001-agent-editing-toolkit/quickstart.md`'s guidance section with a recipe/specialist index so a new integrator finds the entry points for the SC-006 zero-to-first-project flow

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story hardening, edge cases, the success-metric acceptance matrix, and the final validation pass.

- [ ] T079 [P] Large-project handling test in `tests/Beutl.AgentToolkit.Tests/QueryScopingTests.cs` — confirm `read_document` `rootId` scoping keeps payloads bounded and operations responsive for many-element/keyframe projects at scale (the correctness of `rootId` is covered by T036; this is the performance/scale aspect) (spec Edge Cases "Large projects")
- [ ] T080 [P] Performance smoke test toward SC-008 (single edit/query < 2 s for a typical project) in `tests/Beutl.AgentToolkit.Tests/PerformanceSmokeTests.cs`
- [ ] T081 [P] `schema_version_mismatch`-on-open integration test in `tests/Beutl.AgentToolkit.Tests/Sessions/SchemaVersionMismatchOpenTests.cs` — `open_project` on a project written by an incompatible schema surfaces the typed error at the tool level (the unit-level stamp/mismatch is already covered by T012) (FR-031/FR-013)
- [ ] T082 [P] Concurrent-access test FIRST in `tests/Beutl.AgentToolkit.Tests/Sessions/ConcurrentAccessTests.cs` — a stale/conflicting write against a file modified after the session opened surfaces a typed `project_conflict` rather than clobbering it (spec Edge Cases "Concurrent access"); must fail before T083
- [ ] T083 Implement stale-write detection in the file-session save path — `src/Beutl.AgentToolkit/Sessions/FileSessionSource.cs` re-checks the source file's last-write stamp (recorded on open, T024) before `save_project` writes, raising `project_conflict` (T013) on divergence rather than overwriting (FR-024; spec Edge Cases "Concurrent access"; satisfies T082)
- [ ] T084 Author the success-metric acceptance matrix in `docs/specs/001-agent-editing-toolkit/checklists/acceptance-matrix.md` — brief fixtures + pass thresholds for SC-001 (≥90% briefs open clean), SC-004 (≥90% in-scope GUI-operation parity), and SC-007 (≥95% render/export success); run against the fixtures and record results in the PR (aggregate acceptance metrics, not unit tests)
- [ ] T085 Run `dotnet format Beutl.slnx` on all new/changed files and ensure new `.cs` files carry the UTF-8 BOM (`.editorconfig` `charset=utf-8-bom`); run `dotnet build Beutl.slnx` + `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings`
- [ ] T086 Execute the quickstart.md worked example end-to-end (build → `.mcp.json` wiring → author→plan→apply→render→save) and reconcile any drift between quickstart and the implementation

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup. **Blocks every user story.** Includes the destructive-write guard so all later write/delete tools are gated on it.
- **User stories (Phases 3–8)**: each depends on Foundational. Spec priority order is P1 (US1) → P2 (US2, US3, US6) → P3 (US4, US5). US1 is the MVP and the natural first increment.
- **Polish (Phase 9)**: depends on the user stories it hardens.

### Cross-story dependencies (real, called out so independence is honest)

- **US2/US3 build on the US1 reconciler** (`Reconciler.cs`, `CollectionReconciler.cs`) and add tools to the **shared** `src/Beutl.AgentToolkit/Tools/` surface. US2 adds `plan_edit` (clone), merge-patch input, keyframe/effect/structural paths; US3 adds schema discovery + the validator-driven error battery. Each remains independently *testable* (its own test files), but US1's reconciler must exist first.
- **US6 reuses the US1/US2 reconcile/plan/apply core** against a `LiveEditor` source. The live binding is split **core interface (`ILiveSessionBinding`, T057) ← app implementation (`EditViewModelLiveBinding`, T058)** to keep the app→core direction; its csproj wiring (T056) gates the binding/endpoint tasks. The in-app endpoint registers the **same shared core tools assembly** as the stdio host (T059), so the edit/query/render surface is identical (contracts §23-25).
- **US4 internal order**: the FFmpeg.Core split is five atomic sub-tasks — **T063** (csproj shell + slnx) → **T064** (move files) → **T065** (UI-free library-state extraction) → **T066** (existing-extension rewire) and **T067** (GPL-worker source-link update, depends on T064). Then **T068** (AgentToolkit→FFmpeg.Core, after T063–T066) → **T069** (AgentToolkit→AVFoundation, same csproj, after T068) → **T070** (encoder registration, needs T068/T069). **T072 (video export) is gated on T070 (registered encoder) AND T071 (relocated providers).** **T061 (still-render test) is gated on T060.** T070 is tested by T062.
- **US5 depends only on the surface existing** (it documents and packages it).

### Within each story

- Tests are written first and must fail before implementation: the destructive-write guard (T011 → T016) and schema-version (T012 → T020) land in Foundation; the reconciler rollback case (T022) precedes the reconciler (T027); the `read_document` test (T036) precedes its impl (T038); the concurrent-access test (T082) precedes its impl (T083).
- Reconciler core before the tools that route through it; session source before the tools that need a session; csproj-reference/source-link wiring before the code that needs it.

### Parallel opportunities

- Setup: T003, T005, T006 in parallel.
- Foundational tests: T007–T012 in parallel. Foundational impl: T013, T014, T015, T016, T019, T020 are independent files; T017→T018 chain the session/document substrate; T021 (host) last.
- US1: T022/T023 (tests) together; then T028, T029, T031 parallelize against the T024→T027 spine.
- US2: T032–T036 (tests) together; T037 and the per-tool files T041/T042/T043/T044/T045 are parallel once T039/T040 land (each tool lives in its own file).
- US3: T047/T048 (tests) together. T049→T051 touch `Schema/`+`QueryTools.cs`; T052 is sequential (it edits siblings/`ToolBase.cs`).
- US6: T053 (core) and T054 (HeadlessUITests) in parallel; impl T056→T057→T058→T059 sequential.
- US4: T060 then T061 (T061 depends on T060); T062 parallel. The FFmpeg.Core split spine T063→T064→{T065→T066, T067} then T068→T069→T070 is sequential (csproj/file moves); T071 parallel; T073 parallels T072.
- US5: T076 and T077 in parallel.
- Polish: T079, T080, T081, T082 in parallel; T083 follows T082; T084 then T085/T086 last.

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup → 2 Foundational (CRITICAL — blocks all stories).
2. Phase 3 US1 → **STOP and validate**: author from briefs, open in Beutl.
3. Demo the sentence-to-project MVP.

### Incremental delivery

US1 (author) → US2 (refine) → US3 (discover/safe) → US6 (watch live) → US4 (render/export) → US5 (guidance). Each adds value without breaking the prior increment; the P2 trio (US2/US3/US6) is the "trustworthy interactive editor" milestone, P3 (US4/US5) the "verify + package" milestone.

### Notes

- `[P]` = different files, no incomplete-task dependency.
- Honor the GPL/MIT boundary throughout: reach the FFmpeg worker only via `Beutl.FFmpegIpc`; the headless FFmpeg encoder core is the MIT non-UI `Beutl.Extensions.FFmpeg.Core` (no `ProjectReference` to `Beutl.FFmpegWorker`, no compile reference to the Avalonia-coupled `Beutl.Extensions.FFmpeg`); the AVFoundation encoder (`Beutl.Extensions.AVFoundation`) is already non-UI and referenced directly. T067 updates the worker's MIT **source-link** `Include` paths only — not a `ProjectReference`.
- Undo is **same-session** (FR-015, clarified). Render `rendering_unavailable` is for GPU-required content only (3D); SKSL/particles are CPU-safe.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
