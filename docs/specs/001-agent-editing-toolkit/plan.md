# Implementation Plan: Agent Editing Toolkit

**Branch**: `calm-path-4879` (spec dir `001-agent-editing-toolkit`) | **Date**: 2026-06-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/001-agent-editing-toolkit/spec.md`

## Summary

Build an MCP-server-fronted toolkit so external AI agents can author and edit Beutl projects programmatically (no live-GUI automation). The primary interaction is **declarative**: an agent reads a project as an identity-anchored JSON document and submits a desired end-state — a full document or a JSON Merge Patch (RFC 7396) — which the toolkit reconciles, by matching `CoreObject.Id`, into Beutl's **existing undoable edit operations**, applied atomically; a `plan` dry-run previews the change set + validation before `apply` commits. The toolkit also exposes capability/schema discovery, still-frame rendering, and audio+video export, all on the MIT side (the GPL FFmpeg worker is reached only through `Beutl.FFmpegIpc`). Writes are confined to a configured workspace root; reads are unrestricted. **Two hosting modes share one core**: a standalone **stdio** server for headless, file-opened sessions, and an **in-process endpoint inside the running editor** for live sessions — when hosted in-app it drives the same scene + `HistoryManager` the UI is bound to, so agent edits reflect in the preview/timeline/property panels in real time and land on the normal undo stack (the session source differs; the reconcile/plan/apply core is identical).

The technical approach is **adapter, not reimplementation**: the toolkit drives the same non-UI machinery the editor uses — `CoreSerializer` for the document shape, the `HistoryManager` + `CoreObjectOperationObserver` recording pipeline for undoable edits, `PropertyRegistry`/`EngineObject.Properties` for schema, and `SceneRenderer`/`EncodingController` for render/export. New code is glue (reconciler, schema generator, workspace guard, MCP tool surface), not a parallel editing engine.

## Technical Context

**Language/Version**: C# (`LangVersion: preview`, `Nullable: enable` per `Directory.Build.props`)

**Primary Dependencies**: `ModelContextProtocol` 1.4.0 (official C# MCP SDK, permissively licensed — Apache-2.0 per NuGet / the repo is MIT; either way it does not affect the MIT/GPL boundary — see research §1) + `Microsoft.Extensions.Hosting` for the **stdio** server; `ModelContextProtocol.AspNetCore` for the **in-app loopback HTTP/SSE** endpoint (the running GUI cannot be stdio-spawned, so the live mode needs a connectable transport — bound to loopback only). Internally (corrected module ownership): `Beutl.Core` (`CoreSerializer`, `PropertyRegistry`, `CoreObject`/`Id`), `Beutl.Engine` (`Renderer`/`Renderer.Snapshot`, `EngineObject`/`IProperty`), `Beutl.ProjectSystem` (`Scene`/`Element`, `SceneRenderer`), `Beutl.Editor` (`HistoryManager` + `CoreObjectOperationObserver` + `Operations/` + editing services), `Beutl.Extensibility` (the `EncodingController`/`ControllableEncodingExtension` encoder abstraction). **Video export** needs a concrete encoder. `FFmpegEncodingControllerProxy` today lives in `Beutl.Extensions.FFmpeg`, which is MIT but `ProjectReference`s `Beutl.Controls` (Avalonia), and its built-in registration is app-startup code (`LoadPrimitiveExtensionTask`) with an internal package loader — so it is **not headless-reachable as-is**. **Decision**: split the FFmpeg encode core (`FFmpegEncodingControllerProxy` + `FFmpegEncodingSettings` + their support types `FFmpegWorkerProcess`/`FFmpegWorkerLogPump`/`FFmpegWorkerCodecCache` and a UI-free libraries-missing state) into a small MIT **non-UI encoder assembly** (e.g. `Beutl.Extensions.FFmpeg.Core`) that the toolkit references directly; the existing extension keeps only its Avalonia settings-editor UI. *(`Beutl.Extensions.AVFoundation` is **already** non-UI MIT — it references only `Beutl.Extensibility`, no `Beutl.Controls` — so the macOS AVFoundation encoder needs **no split**; the toolkit references it directly. This supersedes the earlier "plus the AVFoundation equivalents [split]" note.)* Installed third-party encoders are discovered via a **public headless encoder-registration service** factored out of `LoadPrimitiveExtensionTask` / the internal `PackageManager`. The GPL worker is reached only via `Beutl.FFmpegIpc` throughout (no compile reference to the worker, no Avalonia in the headless core). System.Text.Json for documents/merge-patch (no JSON-patch library — RFC 7396 is hand-rolled, ~30 lines).

**Storage**: Beutl project files on the local filesystem (`.bep` project / `.scene` / `.belm` element files, JSON via `CoreSerializer`). No database.

**Testing**: NUnit + Moq under `tests/`. New GPU-free suite `tests/Beutl.AgentToolkit.Tests` — schema gen; **merge-patch incl. id-keyed array edge cases (`$delete`; `$delete` on a missing `Id` ⇒ idempotent no-op; omitted-`Id` insert; unknown-`Id` ⇒ `stale_handle`; same-`Id`/different-`$type` ⇒ `validation_rejected`; ordering via `$index`/`$after`/`$before` ⇒ `move-child`; **multiple directives on one member ⇒ `validation_rejected`; `$after`/`$before` naming a non-existent sibling ⇒ `stale_handle`**; keyframes ordered by time)**; reconciliation→operations **with a mid-reconcile `ExecuteInTransaction` rollback case proving live mutations revert**; workspace guard; capability discovery. GPU-gated render/export tests self-skip via the existing `VulkanTestEnvironment`/`GpuTestEnvironment` idiom; IPC export is testable worker-free via a fake `NamedPipe` host (as in `tests/Beutl.FFmpegIpc.Tests`).

**Target Platform**: Cross-platform .NET, single-target `net10.0` (headless; no Avalonia, no Windows-only API). Agent host launches the server over stdio via `.mcp.json`.

**Project Type**: Headless library + console MCP server (plus agent assets — Skills/Subagents — as the guidance pillar), and an in-process MCP endpoint hosted by the Avalonia editor for live sessions. The library is UI-agnostic; only the in-app host references the editor's ViewModel layer.

**Performance Goals**: Single edit/query operation result fast enough for an interactive agent loop (target < 2 s for a typical project, per SC-008). `plan` predicts `apply` exactly (SC-009).

**Constraints**: MIT-only; the GPL FFmpeg worker is reached solely through `Beutl.FFmpegIpc` (no `ProjectReference` to `Beutl.FFmpegWorker`). All writes confined to a configured workspace root. Rendering degrades to a CPU SkiaSharp surface when no Vulkan/MoltenVK device is present (vector/text/bitmap stills, SKSL runtime-shader effects, and particles work CPU-side — verified `SKRuntimeEffect`/`RenderTarget.Create` CPU paths; only 3D (Graphics3D/Vulkan) and any node hard-requiring a GPU context need a device — surfaced as a typed "rendering unavailable" error). FFmpeg native libraries are a runtime prerequisite for video export (preflight check + typed error).

**Scale/Scope**: **Three** new `src/` projects (`Beutl.AgentToolkit`, `Beutl.AgentToolkit.Mcp`, and the split-out `Beutl.Extensions.FFmpeg.Core`) + one test project, plus an in-app host folder in `src/Beutl` and relocations into `Beutl.Editor` (`FrameProviderImpl`/`SampleProviderImpl`) and `Beutl.Extensions.FFmpeg` (UI kept, encode core extracted). ~6 MCP tool groups (schema, read, plan, apply, render-still, export-video); covers visual + audio content, properties, keyframe animations, and effects across built-in and installed-extension types.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. License Firewall (NON-NEGOTIABLE) | PASS | New projects are MIT; reach the FFmpeg worker only via `Beutl.FFmpegIpc` (separate process + pipe). No `ProjectReference` to `Beutl.FFmpegWorker` (the `check-gpl-mit-boundary.sh` hook enforces this mechanically). |
| II. Dual-Target Framework | PASS | The toolkit single-targets `net10.0`, matching its dependency closure (`Beutl.Editor`/`Engine`/`ProjectSystem`/`Extensibility`/`FFmpegIpc` are all single-target `net10.0`); the split-out `Beutl.Extensions.FFmpeg.Core` is likewise `net10.0` single-target. No new TFM is introduced; the dual-target app/worker keep building untouched. |
| III. Test-First with NUnit | PASS | New logic ships with NUnit tests in `tests/Beutl.AgentToolkit.Tests`; the reconciler, schema gen, merge-patch, and guard are GPU-free and unit-testable. |
| IV. Avalonia + Compiled Bindings | N/A | The core library and headless server are UI-free. The in-app host (`src/Beutl/AgentHost`) is endpoint wiring that drives the existing `EditViewModel` — it adds **no new XAML/UserControls**, so the compiled-bindings rule does not apply; any incidental UI (e.g. an on/off toggle) follows the rule. |
| V. Style Belongs to the Linter | PASS | `dotnet format` / `.editorconfig` own style; no hand-formatting. |
| VI. Source Generators Are Load-Bearing | PASS | No generator changes. Schema generation is **runtime reflection** (it must see runtime-registered plugin types), deliberately not a compile-time generator. |

**Result: PASS — no violations.** Complexity Tracking is empty (omitted).

## Project Structure

### Documentation (this feature)

```text
docs/specs/001-agent-editing-toolkit/
├── plan.md              # This file (/speckit-plan)
├── research.md          # Phase 0 — decisions on the 6 unknowns
├── data-model.md        # Phase 1 — entities (declarative document, schema, change set, sessions)
├── quickstart.md        # Phase 1 — build, wire .mcp.json, worked example, tests
├── contracts/
│   ├── mcp-tools.md             # the MCP tool surface (inputs/outputs/errors)
│   └── declarative-document.md  # document shape, merge-patch semantics, schema descriptor
├── spec.md              # feature spec
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/
├── Beutl.AgentToolkit/                 # NEW — MIT core library (net10.0), the transport-agnostic toolkit
│   ├── Documents/                      #   normalized document read/write over CoreSerializer
│   ├── Schema/                         #   capability/schema generation (PropertyRegistry + EngineObject.Properties)
│   ├── MergePatch/                     #   RFC 7396 apply (hand-rolled, System.Text.Json)
│   ├── Reconciliation/                 #   desired-state/patch → HistoryManager operations (plan/apply)
│   ├── Rendering/                      #   headless still + video-export orchestration (SceneRenderer/EncodingController)
│   ├── Workspace/                      #   IWorkspaceGuard write-boundary enforcement
│   └── Sessions/                       #   editing session with a SOURCE: file-opened (headless) or live (in-app editor)
└── Beutl.AgentToolkit.Mcp/             # NEW — MIT console exe (net10.0), stdio MCP server (file-opened sessions)
    ├── Program.cs                      #   Host.CreateApplicationBuilder + AddMcpServer().WithStdioServerTransport()
    └── Tools/                          #   [McpServerToolType] classes wrapping Beutl.AgentToolkit services

src/Beutl/                              # EXISTING Avalonia app — gains an in-process MCP endpoint
└── AgentHost/                          # NEW (in-app) — loopback HTTP/SSE endpoint binding the AgentToolkit core
                                        #   to the ACTIVE EditViewModel session (LIVE source; UI reflects edits live)

src/Beutl.Extensions.FFmpeg.Core/       # NEW — MIT, net10.0, non-UI encode core split from Beutl.Extensions.FFmpeg
└── Encoding/                           #   FFmpegEncodingControllerProxy + FFmpegEncodingSettings (no Avalonia/Controls)

tests/
└── Beutl.AgentToolkit.Tests/           # NEW — NUnit; GPU-free unit tests for the core library
    # render/export tests that need a GPU/worker self-skip via VulkanTestEnvironment / fake-pipe host

# Relocated (Avalonia-free, currently in src/Beutl/Models): FrameProviderImpl, SampleProviderImpl → Beutl.Editor
# Split out (headless export): FFmpegEncodingControllerProxy + FFmpegEncodingSettings → new MIT non-UI Beutl.Extensions.FFmpeg.Core
#   (the existing Beutl.Extensions.FFmpeg keeps only its Avalonia settings-editor UI)
```

**Structure Decision**: A **two-project split** — `Beutl.AgentToolkit` (core library) and `Beutl.AgentToolkit.Mcp` (stdio console exe) — mirrors the repo's own `Beutl.Editor` (non-UI logic) / `Beutl.Editor.Components` (host) split. It keeps the reconcile/plan/apply/schema/render logic unit-testable without a transport and lets a future front-end (CLI, plugin) reuse the core. The core references `Beutl.Editor` (which transitively pulls `Beutl.Core`, `Beutl.Engine`, `Beutl.ProjectSystem`, `Beutl.Extensibility`) — these may be listed explicitly for intent. **It does NOT compile-reference `Beutl.Extensions.FFmpeg` (Avalonia-coupled via `Beutl.Controls`) directly**; instead it references the new MIT **non-UI encoder assembly** (`Beutl.Extensions.FFmpeg.Core`, split out per Technical Context) for the built-in FFmpeg encoder, and loads installed third-party encoders via a public headless registration service — all behind the `Beutl.Extensibility.EncodingController` abstraction, keeping the core headless and encoder-agnostic. The MCP exe references only the core; the in-app host (`src/Beutl/AgentHost`) references the core + the app's `EditViewModel`. Both are MIT, `IsPackable=false`, single-target `net10.0`, and registered in `Beutl.slnx`; `ModelContextProtocol` + `Microsoft.Extensions.Hosting` are added to `Directory.Packages.props` (central package management). The `FrameProviderImpl`/`SampleProviderImpl` (today in the Avalonia app `src/Beutl/Models`, but Avalonia-free) relocate into `Beutl.Editor` so the headless export host does not reference the app.

**Hosting modes (one core, two front-ends + an in-app host)**: the same `Beutl.AgentToolkit` core is reached three ways via the **session-source** seam in `Sessions/`. (1) The standalone `Beutl.AgentToolkit.Mcp` stdio exe opens a project file → *file-opened session* (headless). (2) An in-process host inside the Avalonia app (`src/Beutl/AgentHost`) binds the core to the **active `EditViewModel`** (its existing `Scene` + `HistoryManager` + observer) → *live session*; agent edits flow through the same model/observer/render-invalidation path the UI uses, so the preview/timeline/property panels update live and edits land on the editor's undo stack (User Story 6). Because the running GUI cannot be stdio-spawned, the in-app endpoint is a **loopback HTTP/SSE** server (`ModelContextProtocol.AspNetCore`), which an agent host connects to via `.mcp.json` `{ "type": "http", "url": "http://127.0.0.1:<port>/mcp" }`. There is exactly **one writer** (the editor's `HistoryManager`); the agent is an additional command source marshaled onto the editor's writer thread — no two-writer reconciliation, no new IPC layer.

> **Naming**: `Beutl.AgentToolkit` (+ `.Mcp`) is chosen over `Beutl.Mcp` (+ `.Core`) to name the capability rather than the protocol (leaving room for a non-MCP front-end). This is a cosmetic default — see research.md §5; trivially changeable before implementation.

## Phase 0 — Research

See [research.md](./research.md). Six unknowns resolved: (1) MCP .NET SDK & hosting, (2) headless render/export feasibility & the GPL boundary, (3) reconciliation onto the undoable-operation pipeline, (4) schema generation + RFC 7396 merge-patch, (5) project placement/references/TFM, (6) workspace-guard enforcement + GPU-free test strategy. No `NEEDS CLARIFICATION` remained after research.

## Phase 1 — Design & Contracts

- [data-model.md](./data-model.md) — entities: Declarative Document, Capability/Schema Descriptor, Editing Session, Change Set / Plan, Workspace Guard, Render/Export Job, and their mapping to Beutl types.
- [contracts/mcp-tools.md](./contracts/mcp-tools.md) — the MCP tool surface (declarative editing + render/export + discovery), each tool's input/output/errors.
- [contracts/declarative-document.md](./contracts/declarative-document.md) — the normalized JSON document shape (`$type`/`Id`/properties/`Animations`/`Expressions`/children), merge-patch semantics, and the machine-readable schema descriptor.
- [quickstart.md](./quickstart.md) — build, `.mcp.json` wiring, a worked author→plan→apply→render example, and the test layout.

**Post-design Constitution re-check: PASS** — the design adds three MIT `net10.0` projects (`Beutl.AgentToolkit`, `Beutl.AgentToolkit.Mcp`, the split-out non-UI `Beutl.Extensions.FFmpeg.Core`) + one test project, no generator changes, NUnit-tested, no GPL edge except via `Beutl.FFmpegIpc`. All new projects single-target `net10.0` (no new TFM). No new violations; Complexity Tracking remains empty.

## Key risks carried into `/speckit-tasks`

- **Headless render fidelity on the CPU fallback**: vector/text/bitmap, SKSL runtime-shader effects, and particles render CPU-side (verified — `SKRuntimeEffect.CreateShader` / `RenderTarget.Create` have CPU paths); only 3D (Graphics3D/Vulkan) needs a real device. Plan: render-capability preflight + typed "rendering unavailable" error for GPU-required content; GPU-gated tests self-skip.
- **Type universe registration in a headless host** — `LibraryService.Current` is populated by the app-side registrar; the toolkit host must run an equivalent registration (or enumerate `EngineObject`/`CoreObject` subclasses) so schema discovery sees all built-in + installed-extension types.
- **Headless encoder (export) — DECIDED** (was a deferred risk): the FFmpeg encode core is split into a MIT **non-UI** assembly (`Beutl.Extensions.FFmpeg.Core`) the toolkit references directly (built-in encoder), and installed third-party encoders load via a **public headless encoder-registration service** factored out of the app-startup `LoadPrimitiveExtensionTask` / internal `PackageManager` (Technical Context). The remaining `/speckit-tasks` work is mechanical: perform the split, add the registration service, and update tests/dependencies. FFmpeg native libraries remain a runtime prerequisite (preflight + typed error).
- **Project vs Scene scope** — the undoable history is per-Scene (`EditViewModel.Scene` + one `HistoryManager`); project-level operations (create project, add/remove scenes, project variables) are separate file-level actions, not part of a scene's undo stack. Keep the declarative reconcile/undo surface Scene-rooted and provide distinct project tools.
- **Validation-preview adapter** — `CoreObject.SetValue` is `void` and coerces-then-records-errors; `IProperty` setters assign coerced values without returning status. To report typed accepted/coerced/rejected for `plan` and `apply` (FR-007/FR-030/SC-002/SC-009), run each property's validator (`TryCoerce`/`Validate`) explicitly to compute the outcome, rather than inferring it post-mutation.
- **`TypeFormat` is `internal`** to `Beutl.Core` — get the `$type` string via the public `JsonHelper.WriteDiscriminator`/`TryGetDiscriminator` (or `CoreSerializer` output), not by reaching into the internal API.
- **Reconciler identity edge cases** — new objects without an `Id` (mint one), duplicate/unknown `Id`s, the keyframe time-sort invariant (use `KeyFrames.Add(item, out _)` for sorted insert + `KeyFrames.Remove`/`RemoveAt`; the convenience `AnimationOperations.AddKeyFrame`/`RemoveKeyFrame` are **UI-only** in `Beutl.Editor.Components`, so the headless core uses the engine `KeyFrames` collection directly), and engine-vs-core property dispatch (engine params live in `EngineObject.Properties`, not `PropertyRegistry`).
- **Single-writer threading** — reconciliation must marshal all mutations onto one writer thread; the render path must run on `RenderThread.Dispatcher`.
- **MCP structured-output ergonomics** (typed record vs serialized string) and **SDK/TFM alignment** (SDK ships net8/net9 TFMs, consumed from net10.0) — confirm at first build; pin the exact SDK version.
- **In-app transport & security** — the live endpoint is a self-hosted loopback HTTP/SSE (Kestrel via `ModelContextProtocol.AspNetCore`) inside a desktop Avalonia app, which is unusual: bind `127.0.0.1` only, consider a per-session token, pick/announce a free port, and confirm clean start/stop within the app lifecycle. It must not widen the write-boundary or validation guarantees (FR-035).
- **In-app threading & lifetime** — agent mutations must be marshaled onto the editor's single writer (Avalonia dispatcher) thread; a live session is valid only while its scene/`EditViewModel` is open — closing the project must invalidate the session (typed "no active editor session").
- **No external-file-change reload exists today** — confirmed: `ProjectService.OpenProject` deserializes once and there is no project-file `FileSystemWatcher`. So headless saves do **not** auto-reflect in an already-open editor; real-time reflection is delivered by the in-app host, not by file watching. The two modes are complementary (don't expect a headless save to live-update an open GUI).
