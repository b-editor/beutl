# Contract: MCP Tool Surface

The toolkit exposes its capabilities as MCP tools (declared with `[McpServerTool]`, registered explicitly with `WithTools<T>()`). Tool inputs/outputs are JSON; `plan`/`apply` return typed records (structured output). Every tool returns either a success payload or a **typed, machine-readable error** (FR-014) — never a silent failure. Errors use a stable `code` (e.g. `workspace_boundary`, `validation_rejected`, `media_not_found`, `unknown_type`, `stale_handle`, `rendering_unavailable`, `codec_unavailable`, `schema_version_mismatch`, `no_active_editor_session`).

This is **declarative editing first** (FR-027): `read_document` + `plan_edit` + `apply_edit` are the public editing loop. Project/session lifecycle and render/export remain separate tools; structural, property, effect, and keyframe edits are expressed through the declarative document or merge patch.

## Discovery

### `get_started`
Return a compact guide for agents that only know the MCP endpoint URL.
- **Input**: `{ }`.
- **Output**: `{ "schemaVersion": string, "recommendedCalls": string[], "categoryAliases": { ... }, "rawHttpNote": string }`.
- **Use when**: a raw or low-context agent needs the first valid calls, category alias hints, and the SSE note without pulling the full schema.

### `get_schema`
Return the Capability/Schema Descriptor (FR-006/FR-022), including reusable declarative patch examples.
- **Input**: `{ "type"?: string, "category"?: string, "includeProperties"?: bool, "includeExamples"?: bool }` — omit for the full catalog; filter by `$type` or category. Category aliases are accepted for common agent wording: `visualEffect` / `effect` / `filter` / `videoEffect` ⇒ `FilterEffect`, `fill` / `gradient` ⇒ `Brush`, `stroke` ⇒ `Pen`, `ease` ⇒ `Easing`.
- **Output**: `{ "types": [ { type, discriminator, category, properties: [ { name, valueType, elementType?, display, range, step, default, animatable, supportsExpression, converter? } ], baseFields: [...] } ], "examples": [ { name, description, patch } ] }`. `discriminator` is the exact string to use as a node's `$type`. Set `includeProperties=false` for a compact type/discriminator catalog; set `includeExamples=false` when examples would make the response too large. Examples include multiple empty-scene motion-graphics starters (`create-empty-scene-motion-graphics`, `create-empty-scene-orbital-radar`, `create-empty-scene-split-screen-typography`) plus targeted animation and brush/effect-chain snippets.
- **Errors**: `unknown_type`.
- **Backed by**: `PropertyRegistry` + `EngineObject.Properties` + `LibraryService` (data-model §Capability).

### `list_examples`
Return compact example metadata without large patch payloads.
- **Input**: `{ "type"?: string, "category"?: string }` — same filters and aliases as `get_schema`.
- **Output**: `{ "schemaVersion": string, "examples": [ { name, description, categories, tags } ], "selectionHint": string }`.
- **Use when**: a low-context or raw HTTP agent needs to choose a visual direction before fetching a large patch. The returned `examples` order is shuffled so repeated no-context runs do not always pick the same first starter.

### `get_examples`
Return only reusable declarative patch examples, without the full property schema.
- **Input**: `{ "type"?: string, "category"?: string, "name"?: string }` — same filters and aliases as `get_schema`; pass `name` after `list_examples` to fetch exactly one patch.
- **Output**: `{ "schemaVersion": string, "examples": [ { name, description, patch } ], "selectionHint": string }`. If `name` is omitted, the returned `examples` order is shuffled; if `name` is supplied, the response contains the matching patch.
- **Use when**: an agent is starting from an empty scene, wants a known-good patch such as `create-empty-scene-orbital-radar`, or a raw MCP client would truncate the full `get_schema` response.

### `list_compositions`
Return compact Remotion-style composition templates.
- **Input**: `{ "tag"?: string, "seed"?: string }`. `tag` filters templates such as `starter`, `empty-scene`, `orbital`, `split-screen`, or `typography`. `seed` makes template ordering reproducible; omit it to let the toolkit create a seed.
- **Output**: `{ "schemaVersion": string, "seed": string, "compositions": [ { name, description, tags, styleAxes, propNames, defaultMetadata } ], "selectionHint": string }`. The returned `compositions` order is shuffled by seed, not by a fixed recommended name.
- **Use when**: a low-context agent needs a varied high-level visual direction before fetching or materializing a large patch.

### `get_composition`
Return one Remotion-style composition contract.
- **Input**: `{ "name": string }`.
- **Output**: `{ "schemaVersion": string, "composition": { name, description, tags, styleAxes, defaultProps, props, defaultMetadata, sequences, transitions } }`.
- **Use when**: an agent needs the template's `defaultProps`, input prop descriptors, calculated default metadata, Sequence-like timing, transitions, style axes, and supported variation controls before rendering a patch.
- **Errors**: `unknown_type`.

### `render_composition_patch`
Materialize a Remotion-style composition into a declarative Beutl JSON Merge Patch.
- **Input**: `{ "name"?: string, "tag"?: string, "inputProps"?: object, "seed"?: string }`. Pass `name` for an explicit template, or omit it and pass `tag` to pick the first seed-shuffled match. `inputProps` override `defaultProps`; metadata is calculated from props such as `width`, `height`, `fps`, and `durationSeconds`.
- **Output**: `{ "schemaVersion": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions, patch }, "usageHint": string }`. The same `name`/`inputProps`/`seed` returns the same patch; a different seed changes seeded layout, colors, noise dots, and motion offsets. `patch` is directly consumable by `plan_edit` / `apply_edit`.
- **Use when**: an agent explicitly needs the generated JSON patch. Prefer `plan_composition` / `apply_composition` for raw HTTP or low-context agents because those tools avoid returning the large patch payload.
- **Errors**: `unknown_type`.

### `read_document_summary`
Return a compact scene summary for live progress observation.
- **Input**: `{ }`.
- **Output**: `{ session, source, rootId, name, width, height, duration, elementCount, elements: [ { id, name, start, length, zIndex, objects: [ { id, name, type, discriminator, animatedProperties, expressionProperties, brushProperties, effectProperties } ] } ] }`.
- **Use when**: checking whether a live edit has started or finished without pulling the full declarative document.
- **Errors**: `no_active_editor_session`.

### `read_document`
Return the project/scene (or a subtree) as the normalized Declarative Document (FR-005).
- **Input**: `{ "rootId"?: guid }` — `rootId` to scope to a subtree (keeps payloads bounded for large projects). The target is the current editing session selected by `open_project` / `create_project` / `attach_active_editor`.
- **Output**: `{ "document": <declarative JSON>, "schemaVersion": string }`.
- **Errors**: `no_active_editor_session`, `stale_handle`.
- **Backed by**: `CoreSerializer.SerializeToJsonObject`.

## Session

A `session` may be **file-opened** (headless server) or **live** (in-app host bound to the running editor). The edit/query/render tools below are identical across both; only how the session is obtained differs. In the in-app host, an agent uses `attach_active_editor` instead of `open_project`/`create_project`.

### `open_project`
- **Input**: `{ "path": string }` (read — unrestricted).
- **Output**: `{ "session": string, "source": "File", "summary": { scenes, elements, duration, frameSize } }`.
- **Errors**: `media_not_found`, `schema_version_mismatch` (project written by an incompatible schema — surfaced, not silently dropped, per FR-031/FR-013).

### `attach_active_editor` *(in-app host only)*
Bind a live session to the project/scene currently open in the running editor (FR-032/FR-033).
- **Input**: `{ }`.
- **Output**: `{ "session": string, "source": "LiveEditor", "summary": { … } }`.
- **Errors**: `no_active_editor_session` (nothing open — FR-035). Edits via this session reflect live in the UI and land on the editor's undo stack; persistence is the editor's (no `save_project` for a live session).

### `save_project`
- **Input**: `{ "session": string, "path"?: string, "confirmOverwrite"?: bool }` (write — **guarded**; defaults to the opened path).
- **Output**: `{ "savedPath": string }`.
- **Errors**: `workspace_boundary`, `destructive_intent`, `stale_handle`.

### `create_project`
Create a new project with its initial scene (FR-001/FR-002).
- **Input**: `{ "path": string, "width": number, "height": number, "frameRate": number, "duration": string, "confirmOverwrite"?: bool }` (write — guarded; an existing target path routes through the destructive-write guard ⇒ `destructive_intent` unless confirmed).
- **Output**: `{ "session": string }`.
- **Errors**: `workspace_boundary`, `validation_rejected`, `destructive_intent`.

### `add_scene`
Add a scene to an existing project (FR-002) — a project-level, file-level operation outside any scene's undo stack (data-model §Editing Session "Scope").
- **Input**: `{ "session": string, "width": number, "height": number, "start": string, "duration": string, "name"?: string }`.
- **Output**: `{ "sceneId": guid }`.
- **Errors**: `validation_rejected`. (A subsequent `save_project` persists it through the workspace + destructive-write guards.)

## Declarative edit (the primary loop)

### `plan_edit`
Dry-run a declarative change; **does not mutate** (FR-030).
- **Input**: an envelope `{ "schemaVersion"?: string, "desired"?: <full document>, "patch"?: <merge-patch> }` — supply exactly one of `desired`/`patch`. The `patch` is **RFC 7396 for objects + id-keyed merge for `Id`-bearing arrays**, with optional member directives `$delete` / `$index` / `$after` / `$before` (mutually exclusive); the full rules are in [contracts/declarative-document.md](./declarative-document.md) §2 and are surfaced in the tool's input description. `schemaVersion` is required for `patch`; for `desired`, either pass it separately or include `schemaVersion` in the document. The edit targets the current session's active Scene.
- **Output**: `{ "changeSet": [ { op, targetId, propertyPath?, index?, oldValue?, newValue?, validation } ], "expectedChangeSet": [ ...same shape... ], "valid": bool }` where `validation` ∈ `ok` | `coerced` (with clamped value+range) | `rejected` (with reason). `expectedChangeSet` is intentionally shaped for direct reuse in `apply_edit`.
- **Errors**: `no_active_editor_session`, `schema_version_mismatch`, `stale_handle`.
- **Backed by**: reconcile on a deep clone (research §3); introspect operations (`IUpdatePropertyValueOperation`, collection ops).

### `plan_composition`
Dry-run a Remotion-style composition without returning a huge patch.
- **Input**: `{ "name"?: string, "tag"?: string, "inputProps"?: object, "seed"?: string }`. Arguments match `render_composition_patch`, but the generated patch stays server-side.
- **Output**: `{ "schemaVersion": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions }, "plan": { "changes": [ ... ], "validation": [ ... ], "valid": bool, "expectedChangeSet": [ ... ] } }`.
- **Use when**: a low-context agent wants to preview the composition edit while avoiding large SSE/terminal payloads.
- **Errors**: `no_active_editor_session`, `unknown_type`, `schema_version_mismatch`, `stale_handle`.

### `apply_edit`
Commit a declarative change atomically and undoably (FR-007/FR-012/FR-015/FR-028/FR-029).
- **Input**: same as `plan_edit`, plus `"expectedChangeSet"?` (optional — pass `plan_edit.expectedChangeSet`; reject if the live diff diverges, guaranteeing SC-009 plan↔apply parity).
- **Output**: `{ "plan": { "changes": [ ... ], "validation": [ ... ], "valid": bool, "expectedChangeSet": [ ... ] }, "document": <updated declarative JSON> }`. The returned document includes toolkit-minted Ids for new nodes; agents should use it, or call `read_document`, before follow-up edits.
- **Errors**: `no_active_editor_session`, `validation_rejected` (whole batch rolled back), `stale_handle`, `schema_version_mismatch`.
- **Backed by**: reconcile on the live root inside `HistoryManager.ExecuteInTransaction` (commits on success, **rolls back on any mid-reconcile exception** — a bare `Commit` would leave partial live mutations, breaking FR-012).

### `apply_composition`
Apply a Remotion-style composition atomically without sending the generated patch through the client.
- **Input**: same as `plan_composition`, plus `"expectedChangeSet"?` (optional — pass `plan_composition.plan.expectedChangeSet`; reject if the live diff diverges).
- **Output**: `{ "schemaVersion": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions }, "result": { "plan": { ... }, "document": <updated declarative JSON> } }`.
- **Use when**: a raw or low-context agent should create motion graphics in one server-side declarative operation.
- **Errors**: `no_active_editor_session`, `validation_rejected`, `unknown_type`, `stale_handle`, `schema_version_mismatch`.

## Editing Surface

Element, property, transform, geometry, pen, brush, visual effect, audio effect, structure, and keyframe changes are edited through the declarative `plan_edit` / `apply_edit` document surface. Keyframes live under `Animations.<Property>.KeyFrames`; brushes are assigned to properties such as `Fill` and carry nested `GradientStops`; filter effects are assigned through `FilterEffect` / `FilterEffect.Children`; audio effects are assigned through `Effect` / `AudioEffectGroup.Children`; transforms, geometry, and pens are ordinary typed properties. Additions, updates, deletes, and easing or nested-object changes participate in the same id-keyed diff and plan/apply parity checks as the rest of the document. Targeted changes should use `patch`; full `desired` documents are authoritative and can delete omitted child arrays such as `Elements` or `Objects`. There are no public imperative edit tools for keyframes, properties, elements, effects, undo, or redo; undo/redo stays on the editor/session history used by Beutl itself.

## Render & export

### `render_still`
Render one frame to an image without the GUI (FR-016).
- **Input**: `{ "outputPath": string, "timeSeconds"?: number, "renderScale"?: number, "confirmOverwrite"?: bool }` (write — **guarded**). Bare filenames are written under `agent-output/`; explicit relative directories and absolute in-workspace paths are preserved.
- **Output**: `{ "imagePath": string, "size": [w,h] }`.
- **Errors**: `no_active_editor_session`, `workspace_boundary`, `destructive_intent`, `rendering_unavailable` (typed — content needs a GPU absent on the host, FR-018).
- **Backed by**: `SceneRenderer`→`Renderer.Snapshot`→`Bitmap.Save` on `RenderThread.Dispatcher`.

### `export_video`
Export a range/timeline to a video file (FR-017).
- **Input**: `{ "outputPath": string, "frameRateNumerator"?: number, "frameRateDenominator"?: number, "sampleRate"?: number, "renderScale"?: number, "confirmOverwrite"?: bool }` (write — **guarded**). Bare filenames are written under `agent-output/`; explicit relative directories and absolute in-workspace paths are preserved.
- **Output**: `{ "videoPath": string, "frames": number, "duration": string }`.
- **Errors**: `no_active_editor_session`, `workspace_boundary`, `destructive_intent`, `validation_rejected`, `codec_unavailable` (FFmpeg native libs / worker missing, FR-018), `rendering_unavailable`.
- **Backed by**: `EncodingController.Encode(frameProvider, sampleProvider, ct)` with the concrete encoder from the MIT non-UI `Beutl.Extensions.FFmpeg.Core` (or a headlessly-registered installed encoder) — reaching the FFmpeg worker only via `Beutl.FFmpegIpc` (no GPL `ProjectReference`, and no compile-time reference to the Avalonia-coupled `Beutl.Extensions.FFmpeg`; Constitution I).

## Cross-cutting contract rules

- **Write boundary**: every tool with an `outputPath`/`path` write resolves it through `IWorkspaceGuard.ResolveForWrite` first; out-of-root ⇒ `workspace_boundary` (FR-026). Reads are never guarded.
- **Atomicity**: `apply_edit` commits as exactly one undoable transaction; a mid-batch failure rolls back wholly (FR-012).
- **Validation surfaced**: coercion/rejection is always reported in the result, never silently applied (FR-007).
- **Stable handles**: all `*Id` are `CoreObject.Id` Guids, valid for the session; a removed or unknown update target ⇒ `stale_handle` (FR-011). `stale_handle` responses include a hint to omit `Id` for creation and to reuse Ids from `apply_edit.document` or `read_document` for updates.
- **Determinism**: `plan_edit` predicts `apply_edit` exactly (SC-009); pass `expectedChangeSet` to enforce it.
- **Scene-rooted scope**: `plan_edit`/`apply_edit` operate on a **Scene** root (one `HistoryManager`). `create_project`, `save_project`, and scene add/remove + project-variable changes are **project-level, file-level** operations outside any scene's undo stack (data-model §Editing Session). The agent edits one scene at a time through the undoable surface.
- **Validation is computed, not inferred**: coercion/rejection in a result comes from running the property's validator explicitly (`SetValue` is `void`/coerces silently), so `plan_edit` and `apply_edit` report the same typed outcome (FR-007).
