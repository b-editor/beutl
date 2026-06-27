# Data Model: Agent Editing Toolkit

This describes the toolkit's own conceptual entities and how each maps onto existing Beutl types. The toolkit **does not introduce a new persistence model** — the on-disk truth stays `CoreSerializer` JSON (`.bep`/`.scene`/`.belm`). These entities are the in-memory/contract shapes the MCP surface exposes.

## Entity overview

| Entity | What it is | Backed by (Beutl) |
|---|---|---|
| Editing Session | A stateful working context over one open project/scene, owning the live object root and its recording pipeline | `Scene`/`Project` root + `HistoryManager` + `OperationSequenceGenerator` + `CoreObjectOperationObserver` |
| Declarative Document | The identity-anchored JSON desired-state an agent reads/writes (full or via merge-patch) | `CoreSerializer.SerializeToJsonObject(root)` |
| Merge Patch | An RFC 7396 partial document (null = delete) the agent submits | `JsonObject` + hand-rolled RFC 7396 apply |
| Capability / Schema Descriptor | Machine-readable catalog of editable types + their parameters (type/unit/range/default/animatable/`$type`) | `PropertyRegistry` + `EngineObject.Properties` (`IProperty`) + `LibraryService` |
| Change Set / Plan | The minimal, Id-keyed list of changes a desired-state/patch implies, with validation results | derived diff → `IUpdatePropertyValueOperation` / collection ops (introspected, not yet committed) |
| Edit Transaction | The atomic, undoable application of a Change Set | one `HistoryManager.Commit` over a recorded transaction |
| Workspace Guard | The write-boundary policy (read anywhere, write only under the configured root) | `IWorkspaceGuard.ResolveForWrite` (new) |
| Render Job / Export Job | A request to produce a still image or a video/audio file | `SceneRenderer`+`Renderer.Snapshot`+`Bitmap.Save` / `EncodingController.Encode` via `Beutl.FFmpegIpc` |
| Editing Recipe / Specialist | Packaged Skill / Subagent guidance (the non-code pillar) | `.claude/skills/*`, `.claude/agents/*` assets |

---

## Editing Session

**Fields**: `SessionId` (toolkit-minted), `Source` (`File` | `LiveEditor`), `ProjectUri`/`SceneUri`, `Root` (live `CoreObject`), `History` (`HistoryManager`), `Observer` (`CoreObjectOperationObserver`), `Generator` (`OperationSequenceGenerator`), `IsDirty`.

**Lifecycle**: A **file-opened** session (`open`, headless) deserializes the project/scene via `CoreSerializer.RestoreFromUri` and wires its own recording trio (mirroring `EditViewModel.cs:117-127`); `save` writes back via `CoreSerializer.StoreToUri` through the Workspace Guard. A **live** session (in-app) does **not** create a new root/trio — it **binds to the running editor's active `EditViewModel`** (its existing `Scene`, `HistoryManager`, and observer), so agent edits reflect live in the UI and land on the editor's undo stack (FR-032/FR-033); it has no separate save (the editor owns persistence). Both expose the identical edit/query surface. Entity handles are stable for the session lifetime (FR-011) because they are `CoreObject.Id` Guids. Mutations are serialized onto a single writer thread (the editor's writer/UI thread for a live session).

**Rules**: exactly one writer; reconciliation runs with `PublishingSuppression`/`RecordingSuppression` **inactive** (so mutations both publish and record). Undo/redo is available via `History.Undo()`/`Redo()` — agent edits are normal, human-undoable history entries (FR-015).

## Declarative Document

The normalized JSON the agent reads and writes. Mirrors `CoreSerializer` output exactly (so read↔write round-trips through the same shape):

- `"$type"` — type discriminator (via `JsonHelper.WriteDiscriminator`).
- `"Id"` — `CoreObject.Id` Guid; **the identity anchor** for diffing.
- one key per property, under its `CoreProperty.Name` / `IProperty.Name` (e.g. `"FrameSize"`, `"Start"`, `"Amount"`).
- `"Animations"` — dict of animatable-property → keyframe animation.
- `"Expressions"` — dict of property → expression.
- child collections (e.g. Scene → Elements, Element → `Objects`) as JSON arrays of typed child documents.

**Hierarchy** (unchanged from the spec's Key Entities): Project → Scene(s) → Element(s) (`Start`/`Length`/`ZIndex`) → content `EngineObject`s (Drawable: image/video/text/shape/group, **audio source**) → properties (visual + audio) → optional `Animations`/`Expressions`/`FilterEffect`(s). Full document and merge-patch both conform to this shape; see `contracts/declarative-document.md`.

**Validation**: applied at mutation time by `SetValue`/`IProperty` (`[Range]` coercion, validators) — surfaced (never silently swallowed) per FR-007.

## Merge Patch (RFC 7396)

A partial Declarative Document. Apply semantics: object members recurse; a `null` member deletes the key; arrays and scalars replace wholesale. The toolkit applies the patch to the current serialized document to derive the **desired document**, then diffs by `Id`. Arrays-replace-wholesale is why per-element edits are reconciled by the separate Id-diff (Change Set), not by the raw merge.

## Capability / Schema Descriptor

Per editable type:

- `type` (CLR name), `$type` (discriminator string), `category` (Drawable / FilterEffect / Sound / Transform / …, from `LibraryService` / `KnownLibraryItemFormats`).
- `properties[]`: `name`, `valueType`, `unit`/`display` (`[Display]`), `range` (`[Range]` min/max), `step` (`[NumberStep]`), `default` (`IProperty.DefaultValue`), `animatable` (`IProperty.IsAnimatable`), `supportsExpression`, optional `converter`/encoding note for custom `JsonConverter`s.
- inherited base fields: `Id` (Guid), `EngineObject` CoreProperties (`IsEnabled`, `ZIndex`, `TimeRange`, …).

Generated by instantiating each registered type and reading `EngineObject.Properties` + `PropertyRegistry.GetRegistered(type)`. Covers built-in **and installed-extension** types (FR-022). This descriptor is what `get_schema` returns and what the agent reads to plan valid edits (FR-006).

## Change Set / Plan

The output of reconciling a desired document/patch against the current session, **before** commit. Each entry:

- `op`: `set-property` | `add-keyframe` | `remove-keyframe` | `update-keyframe` | `insert-child` | `remove-child` | `move-child` | `attach-effect` | `remove-effect` | …
- `targetId` (Guid), `propertyPath` (dotted), `oldValue`/`newValue` (for property/keyframe ops), `index` (for collection ops).
- `validation`: `ok` | `coerced` (with clamped value + range) | `rejected` (with reason).

`plan` computes this on a **deep clone** of the root (serialize→deserialize) so the live tree fires no events; `apply` reconciles the same change set on the live root inside one transaction. SC-009 requires `plan`'s set to equal `apply`'s.

## Edit Transaction

One `HistoryManager.Commit(name)` wrapping all operations the observer recorded during a reconciliation. Atomic (FR-012): a failure mid-reconcile leaves the transaction uncommitted / rolled back, so no partial state persists. Auto-compacts via `IMergableChangeOperation.TryMerge`. Reversible by the human in the editor (FR-015).

## Workspace Guard

`IWorkspaceGuard` with a configured `Root` (abs path, from `BEUTL_WORKSPACE` env / config). `ResolveForWrite(requested)` → canonical abs path or throws `WorkspaceBoundaryException`. Reads are unrestricted (no guard call). Every write-capable operation (project save, render/export output) calls it first (FR-026). See research §6 for canonicalization rules.

## Render Job / Export Job

- **Render Job**: `sceneRef`, `time`, `outputPath` (guarded), `scale?`. Produces a PNG via `SceneRenderer`→`Renderer.Snapshot`→`Bitmap.Save`. Returns `unavailable` (typed) when the content needs a GPU absent on the host (FR-018).
- **Export Job**: `sceneRef`, `range` (or whole timeline), `outputPath` (guarded), `videoSettings`/`audioSettings`. Produces a file via `EncodingController.Encode(frameProvider, sampleProvider, ct)`, reaching the FFmpeg worker over `Beutl.FFmpegIpc` (FR-016/FR-017/FR-023). Returns a typed error when FFmpeg native libraries are missing.

## Mapping summary (toolkit term → Beutl type / API)

| Toolkit term | Beutl type / API |
|---|---|
| identity anchor | `CoreObject.Id` (Guid); `ICoreObject.FindById` |
| document (de)serialize | `CoreSerializer.SerializeToJsonObject` / `RestoreFromUri` / `StoreToUri` |
| `$type` discriminator | `JsonHelper.WriteDiscriminator` / `TryGetDiscriminator` |
| schema source | `PropertyRegistry.GetRegistered` + `EngineObject.Properties` (`IProperty`) + `LibraryService` |
| set property (undoable) | mutate live instance → `CoreObjectOperationObserver` records `UpdatePropertyValueOperation<T>` |
| collection edit (undoable) | `Insert`/`Add`/`RemoveAt`/`Move` on the live `ICoreList`; keyframes via `KeyFrameAnimation.AddKeyFrame`/`RemoveKeyFrame` |
| atomic commit | `HistoryManager.Commit` / `ExecuteInTransaction` |
| still render | `SceneRenderer` + `Renderer.Render`/`Snapshot` + `Bitmap.Save` (on `RenderThread.Dispatcher`) |
| video/audio export | `EncodingController.Encode` + `FrameProviderImpl`/`SampleProviderImpl` via `Beutl.FFmpegIpc` |
| write boundary | `IWorkspaceGuard.ResolveForWrite` (new) |
