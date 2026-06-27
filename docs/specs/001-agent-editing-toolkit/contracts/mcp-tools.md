# Contract: MCP Tool Surface

The toolkit exposes its capabilities as MCP tools (declared with `[McpServerTool]`, discovered by `WithToolsFromAssembly`). Tool inputs/outputs are JSON; `plan`/`apply` return typed records (structured output). Every tool returns either a success payload or a **typed, machine-readable error** (FR-014) — never a silent failure. Errors use a stable `code` (e.g. `workspace_boundary`, `validation_rejected`, `media_not_found`, `unknown_type`, `stale_handle`, `rendering_unavailable`, `codec_unavailable`, `schema_version_mismatch`, `no_active_editor_session`).

This is **declarative-first with imperative assist** (FR-027): `read_document` + `plan_edit` + `apply_edit` are the main loop; the imperative tools are convenience for surgical edits.

## Discovery

### `get_schema`
Return the Capability/Schema Descriptor (FR-006/FR-022).
- **Input**: `{ "type"?: string, "category"?: string }` — omit for the full catalog; filter by `$type` or category.
- **Output**: `{ "types": [ { type, $type, category, properties: [ { name, valueType, display, range, step, default, animatable, supportsExpression, converter? } ], baseFields: [...] } ] }`.
- **Errors**: `unknown_type`.
- **Backed by**: `PropertyRegistry` + `EngineObject.Properties` + `LibraryService` (data-model §Capability).

### `read_document`
Return the project/scene (or a subtree) as the normalized Declarative Document (FR-005).
- **Input**: `{ "session": string, "rootId"?: guid }` — `rootId` to scope to a subtree (keeps payloads bounded for large projects).
- **Output**: `{ "document": <declarative JSON>, "schemaVersion": string }`.
- **Errors**: `stale_handle`.
- **Backed by**: `CoreSerializer.SerializeToJsonObject`.

## Session

A `session` may be **file-opened** (headless server) or **live** (in-app host bound to the running editor). The edit/query/render tools below are identical across both; only how the session is obtained differs. In the in-app host, an agent uses `attach_active_editor` instead of `open_project`/`create_project`.

### `open_project`
- **Input**: `{ "path": string }` (read — unrestricted).
- **Output**: `{ "session": string, "source": "File", "summary": { scenes, elements, duration, frameSize } }`.
- **Errors**: `media_not_found`, `schema_version_mismatch` (project written by an incompatible schema — surfaced, not silently dropped, per FR-031/FR-013).

### `attach_active_editor` *(in-app host only)*
Bind a live session to the project/scene currently open in the running editor (FR-032/FR-033).
- **Input**: `{ }` (optionally `{ "sceneId"?: guid }` to disambiguate multiple open scenes).
- **Output**: `{ "session": string, "source": "LiveEditor", "summary": { … } }`.
- **Errors**: `no_active_editor_session` (nothing open — FR-035). Edits via this session reflect live in the UI and land on the editor's undo stack; persistence is the editor's (no `save_project` for a live session).

### `save_project`
- **Input**: `{ "session": string, "path"?: string }` (write — **guarded**; defaults to the opened path).
- **Output**: `{ "savedPath": string }`.
- **Errors**: `workspace_boundary`.

### `create_project`
Create a new project with its initial scene (FR-001/FR-002).
- **Input**: `{ "path": string, "frameSize": [w,h], "frameRate": number, "duration": string }` (write — guarded; an existing target path routes through the destructive-write guard ⇒ `destructive_intent` unless confirmed).
- **Output**: `{ "session": string }`.
- **Errors**: `workspace_boundary`, `validation_rejected`, `destructive_intent`.

### `add_scene`
Add a scene to an existing project (FR-002) — a project-level, file-level operation outside any scene's undo stack (data-model §Editing Session "Scope").
- **Input**: `{ "session": string, "frameSize": [w,h], "start": string, "duration": string }`.
- **Output**: `{ "sceneId": guid }`.
- **Errors**: `validation_rejected`. (A subsequent `save_project` persists it through the workspace + destructive-write guards.)

## Declarative edit (the primary loop)

### `plan_edit`
Dry-run a declarative change; **does not mutate** (FR-030).
- **Input**: an envelope `{ "session": string, "schemaVersion": string, "rootId"?: guid, "desired"?: <full document>, "patch"?: <merge-patch> }` — supply exactly one of `desired`/`patch`. The `patch` is **RFC 7396 for objects + id-keyed merge for `Id`-bearing arrays**, with optional member directives `$delete` / `$index` / `$after` / `$before` (mutually exclusive); the full rules are in [contracts/declarative-document.md](./declarative-document.md) §2 and MUST be surfaced in the tool's input description so agents can discover the directive fields. `schemaVersion` is **required** and is checked against the runtime (mismatch ⇒ `schema_version_mismatch`, FR-031, so the check is enforceable for patches too, not only full documents). `rootId` is the Scene (or subtree) the edit targets; it defaults to the session's active scene.
- **Output**: `{ "changeSet": [ { op, targetId, propertyPath?, index?, oldValue?, newValue?, validation } ], "valid": bool }` where `validation` ∈ `ok` | `coerced` (with clamped value+range) | `rejected` (with reason).
- **Errors**: `schema_version_mismatch`, `stale_handle`.
- **Backed by**: reconcile on a deep clone (research §3); introspect operations (`IUpdatePropertyValueOperation`, collection ops).

### `apply_edit`
Commit a declarative change atomically and undoably (FR-007/FR-012/FR-015/FR-028/FR-029).
- **Input**: same as `plan_edit`, plus `"expectedChangeSet"?` (optional — reject if the live diff diverges, guaranteeing SC-009 plan↔apply parity).
- **Output**: `{ "applied": [ <changeSet> ], "historyEntry": string }`.
- **Errors**: `validation_rejected` (whole batch rolled back), `stale_handle`, `schema_version_mismatch`.
- **Backed by**: reconcile on the live root inside `HistoryManager.ExecuteInTransaction` (commits on success, **rolls back on any mid-reconcile exception** — a bare `Commit` would leave partial live mutations, breaking FR-012).

### `undo` / `redo`
- **Input**: `{ "session": string }`.
- **Output**: `{ "historyEntry": string }`.
- **Backed by**: `HistoryManager.Undo()`/`Redo()`.

## Imperative assists (surgical convenience — FR-003/FR-010)

Thin wrappers that build a one-entry change set and route through the same reconcile/commit path (so they stay undoable/validated):

- `add_element` `{ session, sceneId, content, start, length, zIndex }` → `{ elementId }`
- `remove_element` `{ session, elementId }`
- `move_element` / `resize_element` `{ session, elementId, start?, length?, zIndex? }`
- `set_property` `{ session, targetId, propertyPath, value }` → returns `validation` (FR-007)
- `add_keyframe` / `update_keyframe` / `remove_keyframe` `{ session, targetId, property, time, value?, easing? }` (keeps the time-sort via `KeyFrames.Add(IKeyFrame, out int)`; removal via the `KeyFrames` collection)
- `attach_effect` / `remove_effect` / `reorder_effect` `{ session, targetId, effectType?, index? }`
- `duplicate_element` / `split_element` / `group_elements` / `ungroup_element`

Errors across these: `validation_rejected`, `unknown_type`, `media_not_found`, `stale_handle`.

## Render & export

### `render_still`
Render one frame to an image without the GUI (FR-016).
- **Input**: `{ "session": string, "sceneId": guid, "time": string, "outputPath": string, "scale"?: number }` (write — **guarded**).
- **Output**: `{ "imagePath": string, "size": [w,h] }`.
- **Errors**: `workspace_boundary`, `rendering_unavailable` (typed — content needs a GPU absent on the host, FR-018).
- **Backed by**: `SceneRenderer`→`Renderer.Snapshot`→`Bitmap.Save` on `RenderThread.Dispatcher`.

### `export_video`
Export a range/timeline to a video file (FR-017).
- **Input**: `{ "session": string, "sceneId": guid, "range"?: [start,end], "outputPath": string, "video"?: {...}, "audio"?: {...} }` (write — **guarded**).
- **Output**: `{ "videoPath": string, "frames": number, "duration": string }`.
- **Errors**: `workspace_boundary`, `codec_unavailable` (FFmpeg native libs / worker missing, FR-018), `rendering_unavailable`.
- **Backed by**: `EncodingController.Encode(frameProvider, sampleProvider, ct)` with the concrete encoder from the MIT non-UI `Beutl.Extensions.FFmpeg.Core` (or a headlessly-registered installed encoder) — reaching the FFmpeg worker only via `Beutl.FFmpegIpc` (no GPL `ProjectReference`, and no compile-time reference to the Avalonia-coupled `Beutl.Extensions.FFmpeg`; Constitution I).

## Cross-cutting contract rules

- **Write boundary**: every tool with an `outputPath`/`path` write resolves it through `IWorkspaceGuard.ResolveForWrite` first; out-of-root ⇒ `workspace_boundary` (FR-026). Reads are never guarded.
- **Atomicity**: `apply_edit` and every imperative assist commit as exactly one undoable transaction; a mid-batch failure rolls back wholly (FR-012).
- **Validation surfaced**: coercion/rejection is always reported in the result, never silently applied (FR-007).
- **Stable handles**: all `*Id` are `CoreObject.Id` Guids, valid for the session; a removed target ⇒ `stale_handle` (FR-011).
- **Determinism**: `plan_edit` predicts `apply_edit` exactly (SC-009); pass `expectedChangeSet` to enforce it.
- **Scene-rooted scope**: `plan_edit`/`apply_edit`, `undo`/`redo`, and the imperative assists operate on a **Scene** root (one `HistoryManager`). `create_project`, `save_project`, and scene add/remove + project-variable changes are **project-level, file-level** operations outside any scene's undo stack (data-model §Editing Session). The agent edits one scene at a time through the undoable surface.
- **Validation is computed, not inferred**: coercion/rejection in a result comes from running the property's validator explicitly (`SetValue` is `void`/coerces silently), so `plan_edit` and `apply_edit` report the same typed outcome (FR-007).
