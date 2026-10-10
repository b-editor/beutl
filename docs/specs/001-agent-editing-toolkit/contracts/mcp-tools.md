# Contract: MCP Tool Surface

The toolkit exposes its capabilities as MCP tools (declared with `[McpServerTool]`, registered explicitly with `WithTools<T>()`). Tool inputs/outputs are JSON; `plan`/`apply` return typed records (structured output). Every tool returns either a success payload or a **typed, machine-readable error** (FR-014) — never a silent failure. Errors use a stable `code` (e.g. `workspace_boundary`, `workspace_busy`, `validation_rejected`, `media_not_found`, `unknown_type`, `stale_handle`, `rendering_unavailable`, `codec_unavailable`, `schema_version_mismatch`, `no_active_editor_session`).

Beutl supplies editing operations, runtime data, and rendered output. Creative direction and evaluation against the user request belong to the agent. Operation success is not a visual-quality or task-completion decision.

This is **declarative editing first** (FR-027): `read_document` + `apply_edit` are the public editing loop. Project/session lifecycle and render/export remain separate tools; structural, property, effect, and keyframe edits are expressed through the declarative document or merge patch.

## Discovery

### `get_started`
Return the editing API entrypoints and operation semantics.
- **Input**: `{}`.
- **Output**: `{ "schemaVersion": string, "essentials": string[], "recommendedSkills": [ ... ], "categoryAliases": { ... }, "rawHttpNote": string }`.
- **Behavior**: describes sessions, targeted schema lookup, Id-based edits, bounds, rendering, persistence, and export. Beutl does not classify the brief, choose a creative direction, score the work, or return a completion verdict. Technical validation checks only the submitted operation.

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
- **Use when**: a low-context or raw HTTP agent needs compact schema snippets before fetching a patch. Full-scene starters are hidden by default so original creative briefs are not anchored on reusable templates.

### `get_examples`
Return only reusable declarative patch examples, without the full property schema.
- **Input**: `{ "type"?: string, "category"?: string, "name"?: string }` — same filters and aliases as `get_schema`; pass `name` after `list_examples` to fetch exactly one patch.
- **Output**: `{ "schemaVersion": string, "examples": [ { name, description, patch } ], "selectionHint": string }`. If `name` is supplied, the response contains the matching patch.
- **Use when**: an agent is starting from an empty scene, wants a known-good patch such as `create-empty-scene-orbital-radar`, or a raw MCP client would truncate the full `get_schema` response.

### `list_effect_recipes` / `get_effect_recipe`
Discover curated visual and transition recipe templates.
- **Input**: `list_effect_recipes` accepts `{ "intent"?: string }`; `get_effect_recipe` accepts `{ "name"?: string, "intent"?: string }`.
- **Output**: recipe summaries include `{ name, description, intentTags, effectNames, notes, semantic? }`; full recipes add `patch`. Most visual recipes patch a drawable `FilterEffect` chain. Transition recipes may instead be declarative keyframe/placement templates with placeholder Element/Object ids. New continuity-editing transition templates include overlap dissolve with transform continuation (`semantic: "time passage / soft topic shift"`), directional sweep/wipe (`"location or topic change"`), mask reveal (`"introduction / unveiling"`), dip-to-color (`"chapter break"`), and match-move cut (`"conceptual rhyme"`).
- **Use when**: an agent needs a repeated effect chain, shader field, glow/bloom setup, or transition vocabulary before authoring `apply_edit` patches. Replace placeholder ids, align Element `Start`/`Length` to the intended boundary, and adapt the recipe to the requested transition.

### `list_compositions`
List optional reusable named composition presets.
- **Input**: `{ "tag"?: string, "seed"?: string }`.
- **Output**: `{ "schemaVersion": string, "seed": string, "compositions": [ ... ] }`.
- **Behavior**: the seed determines ordering and generated preset values. Any returned name can be selected or repeated. Listing does not record creative history or constrain later edits.

### `get_composition`
Return one Remotion-style composition contract.
- **Input**: `{ "name": string }`.
- **Output**: `{ "schemaVersion": string, "composition": { name, description, tags, styleAxes, defaultProps, props, defaultMetadata, sequences, transitions } }`.
- **Use when**: an agent needs the template's `defaultProps`, input prop descriptors, calculated default metadata, Sequence-like timing, transitions, style axes, and supported variation controls before rendering a patch.
- **Errors**: `unknown_type`.

### `render_composition_patch`
Materialize a Remotion-style composition into a declarative Beutl JSON Merge Patch.
- **Input**: `{ "name"?: string, "tag"?: string, "inputProps"?: object, "seed"?: string }`. An explicit `name` is required. `inputProps` override `defaultProps`; metadata is calculated from props such as `width`, `height`, `fps`, and `durationSeconds`.
- **Output**: `{ "schemaVersion": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions, patch }, "usageHint": string }`. The same `name`/`inputProps`/`seed` returns the same patch; a different seed changes seeded layout, colors, noise dots, and motion offsets. `patch` is directly consumable by `apply_edit`.
- **Use when**: an agent explicitly needs the generated JSON patch. Prefer `plan_composition` / `apply_composition` for raw HTTP or low-context agents because those tools avoid returning the large patch payload.
- **Errors**: `unknown_type`.

### `read_document_summary`
Return a compact scene summary for live progress observation.
- **Input**: `{ }`.
- **Output**: `{ session, source, rootId, name, width, height, duration, elementCount, elements: [ { id, name, start, length, zIndex, objects: [ { id, name, type, discriminator, animatedProperties, expressionProperties, brushProperties, effectProperties, nestedAnimatedProperties, isFallback, fallbackReason?, fallbackTypeName?, fallbackMessage? } ] } ] }`.
- **Use when**: checking whether a live edit has started or finished without pulling the full declarative document.
- **Errors**: `no_active_editor_session`.

### `read_document`
Return the project/scene (or a subtree) as the normalized Declarative Document (FR-005).
- **Input**: `{ "rootId"?: guid }` — `rootId` to scope to a subtree (keeps payloads bounded for large projects). The target is the current editing session selected by `open_project` / `create_project` / `attach_active_editor`.
- **Output**: `{ "document": <declarative JSON>, "schemaVersion": string }`.
- **Errors**: `no_active_editor_session`, `stale_handle`.
- **Backed by**: `CoreSerializer.SerializeToJsonObject`.

### `list_fonts`
Report the font families this runtime has registered, with each available weight/style typeface pair.
- **Input**: `{ "nameFilter"?: string }` — case-insensitive substring match on the family name.
- **Output**: `{ "schemaVersion": string, "familyCount": number, "families": [ { "name": string, "typefaces": [ { "weight": number, "style": string } ] } ], "usageHint": string }`.
- **Use when**: before setting `FontFamily` / `FontWeight` / `FontStyle`. Resolution is by typographic family name: a subfamily such as `"Inter 28pt"` is not a family and will not match, and a registered family may still lack the requested weight/style pair.
- **Notes**: an unresolvable family renders in the fallback face rather than failing, and `apply_edit` warns by name when one is set. Choose `weight` and `style` together from the same `typefaces` entry, then pass the numeric `weight` to `FontWeight` and the named `style` to `FontStyle`; an unavailable pair resolves to the nearest face and can otherwise render quietly with the wrong thickness or slant.

## Session

A `session` may be **file-opened** (headless server) or **live** (in-app host bound to the running editor). The edit/query/render tools below are identical across both; only how the session is obtained differs. In the in-app host, an agent uses `attach_active_editor` instead of `open_project`/`create_project`.

### `open_project`
- **Input**: `{ "path": string }` (read — unrestricted).
- **Output**: `{ "session": string, "source": "File", "summary": { scenes, elements, duration, frameSize }, "warnings": string[], "recoveryIncidents": [{ "sceneId": string, "sceneName": string, "elementFile": string, "reason": "TypeNotFound" | "DeserializationFailed", "typeName": string | null, "message": string | null }] }`. `warnings` remains the presentation-oriented recovery summary; `recoveryIncidents` exposes the same incidents as stable structured data. `sceneId` and `sceneName` identify the containing scene when different scenes contain the same relative element path. `elementFile` is a forward-slash scene-relative path when available, otherwise the element name. `typeName` is the original serialized discriminator when available, otherwise `null`; `message` is the unmodified nullable deserialization error rather than a presentation fallback.
- **Errors**: `media_not_found`, `schema_version_mismatch` (project written by an incompatible schema — surfaced, not silently dropped, per FR-031/FR-013).

### `attach_active_editor` *(in-app host only)*
Bind a live session to the project/scene currently open in the running editor (FR-032/FR-033).
- **Input**: `{ }`.
- **Output**: `{ "session": string, "source": "LiveEditor", "summary": { … } }`.
- **Errors**: `no_active_editor_session` (nothing open — FR-035). Edits via this session reflect live in the UI and land on the editor's undo stack; persistence is the editor's (no `save_project` for a live session).

### `save_project`
- **Input**: `{ "session"?: string, "path"?: string, "confirmOverwrite"?: bool }` (write — **guarded**; defaults to the current file-backed session and opened path). Pass `session` only to disambiguate; omitting it uses the same active-session resolution as query/render tools.
- **Output**: `{ "savedPath": string, "saved": bool, "session"?: string, "source"?: string, "message"?: string }`.
- **Errors**: `workspace_boundary`, `destructive_intent`, `stale_handle`.

### `create_project`
Create a new project with its initial scene (FR-001/FR-002).
- **Input**: `{ "path": string, "width": number, "height": number, "frameRate": number, "duration": string, "confirmOverwrite"?: bool }` (write — guarded; project file tools use `path`, render/export tools use `outputPath`/`outputDirectory`; an existing target path routes through the destructive-write guard ⇒ `destructive_intent` unless confirmed).
- **Output**: `{ "session": string }`.
- **Errors**: `workspace_boundary`, `validation_rejected`, `destructive_intent`.

### `add_scene`
Add a scene to an existing project (FR-002) — a project-level, file-level operation outside any scene's undo stack (data-model §Editing Session "Scope").
- **Input**: `{ "session": string, "width": number, "height": number, "start": string, "duration": string, "name"?: string }`.
- **Output**: `{ "sceneId": guid }`.
- **Errors**: `validation_rejected`. (A subsequent `save_project` persists it through the workspace + destructive-write guards.)

## Declarative edit (the primary loop)

### `plan_composition`
Dry-run a named composition preset without returning its full patch.
- **Input**: `{ "name": string, "tag"?: string, "inputProps"?: object, "seed"?: string, "includeDetailedPlan"?: bool }`. An explicit name is required. The generated patch stays server-side.
- **Output**: `{ "schemaVersion": string, "planId": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions }, "plan": { "valid": bool, "changeCount": number, "operations": { ... }, "usageHint": string }, "detailedPlan": <reconcile plan>|null }`. Detailed changes and `expectedChangeSet` are included only when requested. `valid` describes whether the edit can be applied, not the visual result.
- **Use when**: inspecting the proposed edit before applying the returned `planId`.
- **Errors**: `no_active_editor_session`, `validation_rejected`, `unknown_type`, `schema_version_mismatch`, `stale_handle`.

### `apply_edit`
Commit a declarative change atomically and undoably (FR-007/FR-012/FR-015/FR-028/FR-029).
- **Input**: an envelope `{ "schemaVersion"?: string, "desired"?: <full document>, "patch"?: <merge-patch>, "includeDocument"?: bool, "quiet"?: bool }` — supply exactly one of `desired`/`patch`. The `patch` is **RFC 7396 for objects + id-keyed merge for `Id`-bearing arrays**, with optional member directives `$delete` / `$index` / `$after` / `$before` (the ordering three are mutually exclusive) and a leading `{ "$replace": true }` sentinel to wholesale-rebuild an id-keyed array; the full rules are in [contracts/declarative-document.md](./declarative-document.md) §2 and are surfaced in the tool's input description. `schemaVersion` is required for `patch`; for `desired`, either pass it separately or include `schemaVersion` in the document. The edit targets the current session's active Scene.
- **Output**: `{ "valid": bool, "operations": { "<operation>": number }, "changeCount": number, "validationStatuses": { "<status>": number }, "validationCount": number, "createdIds": [ { id, path, type?, name? } ], "createdIdCount": number, "changes"?: [ ... ], "validation"?: [ ... ], "appliedChangeSet"?: [ ... ], "document"?: <updated declarative JSON> }`. `createdIds` contains addressable created entities and excludes animations/keyframes, which are reached through their owning property. `createdIdCount` always reports the full count before response filtering. With `quiet=true`, `createdIds` is further limited to named entities and the detailed `changes`, `validation`, and `appliedChangeSet` members are omitted; operation/validation counts remain. `document` is returned only when `includeDocument=true`; otherwise agents should use `createdIds`, `read_document_summary`, or `read_document` before follow-up edits that need newly minted `Id` values.
- **Errors**: `no_active_editor_session`, `validation_rejected` (whole batch rolled back), `stale_handle`, `schema_version_mismatch`.
- **Backed by**: reconcile on the live root inside `HistoryManager.ExecuteInTransaction` (commits prior pending work separately, blocks concurrent records, commits on success, and **rolls back only the callback operations on any mid-reconcile exception** — a bare `Commit` would leave partial live mutations or absorb unrelated edits, breaking FR-012).

### `duplicate_object`
Duplicate one `EngineObject` (e.g. a Drawable) within its owning `Element.Objects`, minting **fresh `Id`s on every nested node**, and return the new object's `Id` (FR-011/FR-012). A convenience over hand-authoring the copy in `apply_edit`: it deep-clones the target, strips every `Id` so fresh ones are minted, and appends the copy after the original (front-most within the Element). Both modes commit as one undoable `HistoryManager` transaction — the default (`wrapInGroup=false`) path runs the same reconcile pipeline as `apply_edit`; the `wrapInGroup=true` path reparents the live objects imperatively (identity is preserved because the original instance is moved, not re-serialized).
- **Input**: `{ "objectId": string, "elementId"?: string, "wrapInGroup"?: bool }`. `objectId` is the `CoreObject.Id` of an object inside some `Element.Objects`; `elementId` optionally scopes the search to one element.
- **Output**: `{ "valid": bool, "elementId": string, "objectId": string, "createdIds": [ { id, path, type?, name? } ], "groupId"?: string }`. `objectId` is the new copy's `Id`; `createdIds` covers the copy and its minted descendants (and the new `DrawableGroup`/portal when one is created); `groupId` is present only for `wrapInGroup=true`.
- **Use when**: building a layered look that needs a second copy of a drawable — e.g. an additive emissive glow: `duplicate_object` with `wrapInGroup=true`, then `apply_edit` the `additive-bloom` recipe (blur + `BlendMode` `Plus` + reduced `Opacity`) onto the returned `objectId` so the copy adds light over the untouched original. Move the copy to a separate Element (higher `ZIndex`) when it needs independent timing or z-order.
- **Errors**: `no_active_editor_session`, `stale_handle` (unknown `objectId`, or `objectId` not found under `elementId`), `validation_rejected` (e.g. `wrapInGroup=true` on a non-Drawable), `schema_version_mismatch`.

### `apply_composition`
Apply a named composition preset atomically without sending its patch through the client.
- **Input**: `{ "name"?: string, "tag"?: string, "inputProps"?: object, "seed"?: string, "planId"?: string, "expectedChangeSet"?: array|string }`. Pass a stored `planId`, or an explicit name and preset properties. If supplied, `expectedChangeSet` must match the live change set; it is available from a detailed plan.
- **Output**: `{ "schemaVersion": string, "composition": { name, seed, inputProps, resolvedProps, metadata, sequences, transitions }, "appliedPlanId": string|null, "result": { "plan": { ... }, "document": <updated declarative JSON> } }`.
- **Behavior**: stored plans are bound to their editing session and checked for intervening changes. Any named preset can be selected and repeated; no stylistic selection or history gate applies.
- **Errors**: `no_active_editor_session`, `validation_rejected`, `unknown_type`, `stale_handle`, `schema_version_mismatch`.

## Editing Surface

Element, property, transform, geometry, pen, brush, visual effect, audio effect, structure, and keyframe changes are edited through the declarative `apply_edit` document surface. Keyframes live under `Animations.<Property>.KeyFrames`; brushes are assigned to properties such as `Fill` and carry nested `GradientStops`; filter effects are assigned through `FilterEffect` / `FilterEffect.Children`; audio effects are assigned through `Effect` / `AudioEffectGroup.Children`; transforms, geometry, and pens are ordinary typed properties. Targeted changes should use `patch`; full `desired` documents are authoritative and can delete omitted child arrays such as `Elements` or `Objects`. The convenience mutators outside `apply_edit` are `duplicate_object` (a declarative shortcut for id-safe object cloning that still commits through the reconcile/`HistoryManager` pipeline) and `group_elements` / `ungroup_elements` (which reparent Elements under a flow operator); all three commit as ordinary `HistoryManager` transactions and are therefore reachable by `undo`. there are otherwise no public imperative edit tools for keyframes, properties, elements, or effects. History navigation is exposed separately as `undo` / `redo` / `read_history`, which drive the same `HistoryManager` the editor itself uses — they move through history rather than mutating properties, so the authoring surface stays declarative.

## History

### `undo`
Revert the most recent edit transactions on the active session, newest first.
- **Input**: `{ "steps"?: number }` — clamped to `1..50`; stops early when the undo stack empties.
- **Output**: `{ "applied": [ { "id": string, "name": string|null } ], "canUndo": bool, "canRedo": bool, "undoCount": number, "redoCount": number, "nextUndo": entry|null, "nextRedo": entry|null, "message": string }`.
- **Use when**: an experiment should be backed out. Restoring prior state exactly beats authoring a compensating patch, which has to reconstruct it by hand.
- **Notes**: every `apply_edit` / `duplicate_object` / `group_elements` mutation is one transaction. Pending debounced editor edits are flushed before the stack is inspected, so a transaction in flight is not missed. In a LiveEditor session the shared editor history guard pauses and fully drains active preview playback before that flush or any undo/redo mutation; every requested step and the returned point-in-time snapshot run inside one guarded batch. An empty stack with no pending work does not stop playback. The stack is the editor's own, so a step can revert a human's edit — call `read_history` immediately before `undo` and inspect that response's `nextUndo` when that matters. The `undo` response's `applied` list names what was reverted; its `nextUndo` is the transaction that remains next. File-backed sessions still need `save_project` to persist the reverted state.
- **Errors**: `no_active_editor_session`.

### `redo`
Re-apply transactions previously reverted by `undo`.
- **Input**: `{ "steps"?: number }` — clamped to `1..50`.
- **Output**: same shape as `undo`.
- **Use when**: an undo went too far. The redo stack is cleared by any new edit, so this only works when nothing has been authored since.
- **Errors**: `no_active_editor_session`.

### `read_history`
Report undo/redo depth and the next transaction in each direction without undoing or redoing; pending editor work is flushed first so the reported stack is current.
- **Input**: `{}`.
- **Output**: same shape as `undo`, with `applied` empty.
- **Use when**: before `undo`, to see what a step would revert.
- **Errors**: `no_active_editor_session`.

## Render & export

Every tool in this section that exposes `renderScale` normalizes non-finite or non-positive values to `1`, then validates the root output extent before rendering or encoder preflight. Both `ceil(frameWidth * renderScale)` and `ceil(frameHeight * renderScale)` must be at most `16384`; an oversized request returns `validation_rejected` targeted at `renderScale`, naming the requested extent, the axis limit, and the maximum usable scale for the current frame size.

Host-controlled rendering admission applies to `render_still`, `render_storyboard`,
`measure_frame_differences`, and `export_video`. Each can return the stable typed
error `workspace_busy` while a workspace replacement holds the exclusion boundary. Output leases
may coexist; collectively they prevent a workspace replacement until every output finishes.

### `render_still`
Render one frame to an image without the GUI (FR-016).
- **Input**: `{ "outputPath": string, "timeSeconds"?: number, "renderScale"?: number, "confirmOverwrite"?: bool, "returnImageContent"?: bool }` (write — **guarded**). Bare filenames are resolved directly within the workspace; explicit relative directories and absolute in-workspace paths are preserved. `timeSeconds` is the only time argument; `time` is not a parameter and is rejected as an unknown argument. `returnImageContent` defaults to `false`.
- **Output**: by default, the tool result content remains a single JSON text block with `{ "value": { "outputPath": string, "width": number, "height": number, "time": string, "warnings": string[], "visibilityAnalysis": { totalPixels, visiblePixels, visiblePixelRatio, foregroundPixels, foregroundPixelRatio, occupiedBoundsRatio, maxQuadrantForegroundRatio, left, top, right, bottom, minLuma, maxLuma, meanLuma, lumaStandardDeviation, backgroundLuma, visibilityThreshold, foregroundDeltaThreshold, warnings }, "activeElements": [ { id, name, start, length, zIndex, objectCount } ] }, "error": null }`. `warnings` reports technical measurement failures, such as unavailable pixel data. A dark, static, sparse, or low-contrast frame does not produce an aesthetic warning. `activeElements` reports enabled elements whose ranges include the rendered time. When `returnImageContent=true`, the same JSON text block is followed by one MCP `ImageContentBlock` (`mimeType: "image/png"`) downscaled to about a 768 px long edge.
- **Errors**: `no_active_editor_session`, `workspace_busy`, `workspace_boundary`, `destructive_intent`, `rendering_unavailable` (typed — content needs a GPU absent on the host, FR-018).
- **Backed by**: `SceneRenderer`→`Renderer.Snapshot`→`Bitmap.Save` on `RenderThread.Dispatcher`.

### `render_storyboard`
Render a contact sheet from explicit times, shots, or Element midpoints, with optional intermediate samples. It reports rendered data without imposing a production sequence.
- **Input**: `{ "shots"?: [ { "name": string, "timeSeconds": number } ], "timeSeconds"?: number[], "outputDirectory"?: string, "basename"?: string, "renderScale"?: number, "confirmOverwrite"?: bool, "background"?: bool, "returnImageContent"?: bool, "subdivisionLevel"?: number }` (write — **guarded**). `timeSeconds`, when supplied, overrides both explicit `shots` and auto shot detection; each value becomes an anchor frame named `t:<seconds>`, and `subdivisionLevel` still inserts in-between frames between adjacent supplied times. Empty arrays, non-finite values, duplicate times, out-of-range values, and requests above the 48-frame cap return `validation_rejected` targeted at `timeSeconds`. `subdivisionLevel` is an integer clamped to `0..3` and defaults to `0`. Level `0` is the current one-frame-per-shot behavior. Level `1` adds the midpoint of each adjacent resolved shot pair; level `2` adds 1/4, 1/2, and 3/4 points; level `3` adds 1/8 multiples. Generated auto/shot times are deduplicated with about a half-frame tolerance at the scene frame rate and kept chronological. When both `shots` and `timeSeconds` are omitted, one representative time is auto-derived per timeline `Element` (the midpoint of its `[Start, Start+Length)`), deduplicated and ordered before subdivision. Stills and the contact sheet use the workspace root unless outputDirectory is supplied. Omitting `basename` uses a collision-free session/process token; explicit `basename` values preserve exact filenames. `returnImageContent` defaults to `false` and cannot be combined with `background=true`. The total rendered frame count is capped at 48; requests above the cap return a `validation_rejected` error asking the agent to lower `subdivisionLevel`, narrow the shot set, or pass fewer `timeSeconds`.
- **Output**: by default, the tool result content remains a single JSON text block with `{ "value": { "status": "completed"|"running", "jobId": string|null, "result": { "contactSheetPath": string, "shots": [ { "name": string, "timeSeconds": number, "stillPath": string, "visibilityAnalysis": { ... }, "kind": "shot"|"inbetween", "subdivisionLevel": number } ], "cutEyeTrace": [ { "leftFrame": string, "rightFrame": string, "leftFocalPoint": { "x": number, "y": number }, "rightFocalPoint": { "x": number, "y": number }, "displacementRatio": number } ] } | null }, "error": null }`. Anchor entries use `kind="shot"` and `subdivisionLevel=0`; generated entries use `kind="inbetween"`, a deterministic name such as `between:<leftShotName>~<rightShotName>@L2:1/4`, and the binary subdivision depth that produced the frame. `cutEyeTrace` is computed only between adjacent anchor shot frames, not generated in-between frames; `displacementRatio` is normalized by the frame diagonal. It has no acceptance threshold.
- **Use when**: inspecting chosen times or intermediate samples. Sampling does not require a static-first production sequence. Use playable output to assess motion; focal-point displacement is descriptive data with no prescribed correction. `background=true` queues long renders.
- **Errors**: `no_active_editor_session`, `workspace_busy`, `validation_rejected` (no explicit shots and no timeline Element), `workspace_boundary`, `destructive_intent`, `rendering_unavailable`.
- **Backed by**: per-shot `StillRenderer` + a `StoryboardRenderer` SkiaSharp grid compositor; background jobs run through a single-flight `RenderJobManager`.

### `measure_frame_differences`
Measure rendered pixel changes and frame coverage without a quality or completion verdict.
- **Input**: `{ "timeSeconds"?: number[], "sampleCount"?: number, "renderScale"?: number, "pixelDeltaThreshold"?: number, "foregroundLumaThreshold"?: number }`. At least two distinct sample times are required. Defaults: 5 samples, render scale 1, channel delta 48, foreground threshold 24. Thresholds parameterize measurement, not acceptance criteria.
- **Output**: `{ "pixelDeltaThreshold": number, "foregroundLumaThreshold": number, "minimumChangedPixelRatio": number, "averageChangedPixelRatio": number, "samples": [ { "time": string, "width": number, "height": number } ], "pairVariations": [ { "fromTime": string, "toTime": string, "changedPixels": number, "totalPixels": number, "changedPixelRatio": number, "meanAbsoluteDelta": number } ], "frameCoverage": [ { "time": string, "foregroundPixels": number, "totalPixels": number, "foregroundPixelRatio": number, "occupiedBoundsRatio": number, "maxQuadrantForegroundRatio": number, "left": number, "top": number, "right": number, "bottom": number } ] }`.
- **Behavior**: renders in memory and does not create output files. Static frames successfully return zero change. There is no desired motion/coverage threshold, pass/fail flag, or recommended correction.
- **Errors**: `no_active_editor_session`, `workspace_busy`, `validation_rejected`, `rendering_unavailable`.

### `analyze_audio_rhythm`
Analyze an audio source and return estimated BPM, beats, and onsets as timing data.
- **Input**: `{ "path": string, "startSeconds"?: number, "durationSeconds"?: number, "expectedBpmMin"?: number, "expectedBpmMax"?: number }`. `path` is required and must point to an existing readable file. `startSeconds`/`durationSeconds` select the analysis window. `expectedBpmMin`/`expectedBpmMax` constrain BPM estimation inside the supported 60-200 BPM range.
- **Output**: `{ "schemaVersion": string, "sampleRate": number, "analyzedWindow": { "startSeconds": number, "durationSeconds": number }, "estimatedBpm": number, "confidence": number, "beatTimesSeconds": number[], "strongOnsetTimesSeconds": number[] }`. `beatTimesSeconds` are absolute seconds in the source file/window coordinate space; use them as a timing reference when needed by the edit.
- **Use when**: a scene has a music bed or other timing reference. The agent decides how these estimates relate to the requested timing.
- **Errors**: `validation_rejected` (invalid window or BPM bounds), `media_not_found` (missing path), `codec_unavailable` (decoder/native audio stack unavailable).
- **Backed by**: `SoundSource`/engine media decoding for file input plus a pure PCM novelty-curve analyzer that is independent of FFmpeg for unit tests.

### `export_video`
Export a range/timeline to a video file (FR-017).
- **Input**: `{ "outputPath": string, "frameRateNumerator"?: number, "frameRateDenominator"?: number, "sampleRate"?: number, "renderScale"?: number, "crf"?: number, "bitrate"?: number, "confirmOverwrite"?: bool, "background"?: bool }` (write — **guarded**). Render/export outputs use `outputPath`/`outputDirectory`; project file tools use `path`. Bare filenames are resolved directly within the workspace; explicit relative directories and absolute in-workspace paths are preserved. `crf` (0-51, higher = smaller/lower quality) and `bitrate` (bits/s, ABR) are **mutually exclusive** video-quality knobs; when neither is given the encoder default (crf 22) applies. Raise `crf` for hard-to-compress content such as full-frame procedural grain.
- **Output**: `{ "status": "completed"|"running", "jobId": string|null, "result": { "outputPath": string, "frames": number, "samples": number, "duration": string, "encoder": string, "warnings": string[] } | null }`. Synchronous calls return `status="completed"` with the payload under `result`; `background=true` returns `status="running"` + `jobId` (poll `read_render_job(jobId)`). `encoder` names the encoder that produced the file (`"FFmpeg"`, `"AVFoundation"`, or the extension type name of an installed encoder). `warnings` is advisory and empty on a clean export; current entries are a `crf` request on the AVFoundation fallback (VideoToolbox has no CRF control) and a measured container video bitrate below half of a requested `bitrate` (the audio contribution is subtracted when the audio bitrate is known).
- **Errors**: `no_active_editor_session`, `workspace_busy`, `workspace_boundary`, `destructive_intent`, `validation_rejected` (bad frame rate, `crf` outside 0-51, non-positive `bitrate`, both `crf` and `bitrate` supplied, or a `renderScale` whose output extent exceeds the 16384-pixel axis limit — see the shared `renderScale` paragraph above), `codec_unavailable` (FFmpeg native libs / worker missing, FR-018), `rendering_unavailable`.
- **Backed by**: `EncodingController.Encode(frameProvider, sampleProvider, ct)` with the concrete encoder from the MIT non-UI `Beutl.Extensions.FFmpeg.Core` (or a headlessly-registered installed encoder) — reaching the FFmpeg worker only via `Beutl.FFmpegIpc` (no GPL `ProjectReference`, and no compile-time reference to the Avalonia-coupled `Beutl.Extensions.FFmpeg`; GPL/MIT boundary policy). `crf`/`bitrate` are applied to the FFmpeg encoder settings (the `crf` `AdditionalOption` / `Bitrate`) on the MIT proxy side before encoding.

### `read_render_job`
Report the status of a background `render_storyboard`/`export_video` job.
- **Input**: `{ "jobId": string }`.
- **Output**: `{ "jobId": string, "kind": "storyboard"|"export", "state": "running"|"completed"|"failed"|"cancelled", "result": <render_storyboard/export_video payload> | null, "error": { "code": string, "message": string, ... } | null, "startedAt": string, "completedAt": string|null, "progress": { "completed": number, "total": number, "ratio": number|null, "stage": string|null } | null }`. `result` is populated only when `state="completed"`; `error` carries the same `ToolError` shape (and code) the synchronous path would have returned when `state="failed"`. `ratio` is `completed / total` clamped to `0..1`, or `null` while `total` is not yet known. A waiting job reports `0/0` with stage `queued`, then `starting`. Storyboards report completed stills against the planned still count with stage `rendering shots`, then `<total>/<total>` with stage `contact sheet`. Exports report only frames already returned to the encoder with stage `encoding`, and publish `<total>/<total>` only after `EncodingController.Encode` returns successfully.
- **Use when**: polling after a `background=true` render/export call. Poll until `state` leaves `running`.
- **Errors**: `stale_handle` (unknown `jobId`).
- **Backed by**: a process-singleton `RenderJobManager` (registered in both hosts) that serializes background jobs single-flight because all stills share the one `RenderThread`.

### `cancel_render_job`
Request cancellation of a running background render/export job.
- **Input**: `{ "jobId": string }`.
- **Output**: same snapshot shape as `read_render_job`. A still-running job transitions to `cancelled` once it observes the request (the per-job `CancellationToken` is plumbed through `StillRenderer`/`VideoExporter`).
- **Errors**: `stale_handle` (unknown `jobId`).

## AI generation *(in-app host only)*

These tools run Beutl's cloud AI on the account signed in to the running app, through the same executor as the AI generation nodes, and **each call is a paid request**. They are not offered by the stdio host. Results are files saved next to the open scene (`resources/ai`); the tools never edit the scene — place a result with `apply_edit` (`SourceImage.Source` / `SourceVideo.Source` set to the returned path) or pass it to another AI tool. Input files (`sourcePath`, `referenceImagePaths`, `firstFramePath`, `lastFramePath`) are uploaded to the service, so they must resolve, after following links, inside the workspace or the open scene's `resources/ai`; relative paths are workspace-relative.

Generation tools start a job and wait up to `waitSeconds` (0–110, default 45; other values are refused) for it. A job still running returns `{ status: "Running", jobId }`; call `read_ai_job` to wait again. The job manager keeps the most recent 128 finished jobs, pruned as jobs finish. Every argument is checked before a job starts, so a refused call costs nothing. Omitted settings are chosen from what the named or default model offers, as the AI tab's controls are; the AI tab's fallback lists apply when the catalog cannot be reached.

- **Errors**: `ai_unavailable` (not signed in, no AI plan, the host has no API clients, or the catalog lists models for the operation but none the account can use; while the account's plan cannot be read the catalog marks every model unavailable, and the request goes to the service instead), `no_active_editor_session` (no scene open to save results next to), `validation_rejected` (missing prompt, unknown operation, task, mode or model, `waitSeconds` out of range, an outpaint expansion other than 10/25/50, more reference pictures than the model takes, `generateAudio` for a model without sound, an aspect ratio, background, duration, resolution, frame or seed the named or default model does not take, a negative seed, a prompt over 4000 characters or the model's own limit (an outpaint's counts the instruction the executor puts in front), an outpaint canvas over 8192 pixels a side, a language that is not an ISO 639-1 code, `durationSeconds` for `edit_video` mode `edit`, a prompt for `remove_background` or `upscale`, a named model the catalog could not be loaded to confirm (the executor would send the service default instead), reference pictures over the catalog's total budget, a source clip larger or longer than the model takes, a last frame without a first), `workspace_boundary` (an input outside the workspace and the scene's AI results; checked before the file's existence), `media_not_found` / `media_unsupported` (input files, including pictures over the upload size or 8192 pixels a side, checked before decoding, and an outpaint whose widened canvas is over the upload size as PNG, and recordings too long to transcribe), `ai_generation_failed` (the service refused or failed; the job's `errorMessage` says why, and an unexpected error's details stay in the app's log), `ai_job_not_found`.

### `list_ai_models`
- **Input**: `{ "operation": "image.generate" | "image.edit.<task>" | "video.generate" | "video.edit" | "video.extend" | "audio.transcribe" }`.
- **Output**: `{ "operation", "models": [ { "id", "label", "isDefault", "isAvailable", "aspectRatios", "backgrounds", "maxReferenceImages", "durationsSeconds", "resolutions", "supportsAudio", "supportsFirstFrame", "supportsLastFrame", "supportsSeed" } ] }` — a `null` list means the model publishes none. Only the capabilities of the operation asked for are reported (image fields for `image.*`, video fields for `video.*`).

### `generate_image`
- **Input**: `{ "prompt": string, "aspectRatio"?: string, "background"?: "auto" | "opaque" | "transparent", "model"?: string, "seed"?: number, "referenceImagePaths"?: string[], "waitSeconds"?: number }`. The aspect ratio defaults to the one the model offers nearest the scene, the background to `auto` (or the model's first). At most 4 reference pictures, fewer when the model takes fewer.
- **Output**: an AI job snapshot (below) whose output is a PNG.

### `edit_image`
- **Input**: `{ "sourcePath": string, "task": "remove_background" | "upscale" | "restyle" | "remove_object" | "outpaint", "prompt"?: string, "outpaintExpansionPercent"?: 10 | 25 | 50, "model"?: string, "waitSeconds"?: number }`. `restyle`, `remove_object` and `outpaint` need a prompt.

### `generate_video`
- **Input**: `{ "prompt": string, "durationSeconds"?: number, "resolution"?: string, "aspectRatio"?: string, "generateAudio"?: boolean, "firstFramePath"?: string, "lastFramePath"?: string, "model"?: string, "seed"?: number, "waitSeconds"?: number }`. Resolution and aspect ratio default to the model's choices nearest the scene, the duration to 6 seconds (or the model's first). `generateAudio` is refused for a model whose `supportsAudio` is false rather than quietly dropped.

### `edit_video`
- **Input**: `{ "sourcePath": string, "prompt": string, "mode"?: "edit" | "extend", "durationSeconds"?: number, "model"?: string, "waitSeconds"?: number }`. mp4 or webm up to 32 MB, opened before the job starts so a file that is not a clip is `media_unsupported`. An extension returns the whole clip with the new part at the end; its duration defaults to 6 seconds (or the model's first).

### `transcribe_audio`
- **Input**: `{ "sourcePath": string, "language"?: string, "model"?: string, "waitSeconds"?: number }`. Any decodable audio or video file; long files are sent in ten-minute parts. Each part's segments are checked as the subtitle flow checks them (in order, inside the part) before they are offset; words with unusable times are left out. A recording longer than `int.MaxValue` samples (about 12.4 hours at 48 kHz) is refused before the first part is sent.
- **Output**: a job snapshot whose `output.transcript` is `{ "language", "segments": [ { "start", "end", "text" } ], "words": [ { "start", "end", "word" } ] | null }`, times in seconds from the start of the file.

### `read_ai_job` / `cancel_ai_job`
- **Input**: `{ "jobId": string, "waitSeconds"?: number }` / `{ "jobId": string }`.
- **Output**: `{ "jobId", "operation", "status": "Running" | "Succeeded" | "Failed" | "Cancelled", "statusText", "elapsedSeconds", "output": { "outputPath", "mediaKind": "image" | "video" | "transcript", "modelId", "seed", "transcript" } | null, "errorCode", "errorMessage", "nextStep" }`. Cancelling stops waiting; a request the service already accepted is still charged, and its result can be collected from the AI tab's job history.

## Extension tools *(in-app host only)*

Installed extensions can add tools through `McpToolExtension` (see the [MCP tool extension guide](../../../extension-authoring/mcp-tools.md)). They are listed and called like built-in tools, appear and disappear with their package without restarting the endpoint, and receive the same strict-argument check and `instanceId` routing. A built-in tool always wins a name collision. `tools/list` reflects the extensions loaded in the connected instance.
- **Errors**: `extension_tool_failed` (the extension threw; the message is included), `extension_tool_unavailable` (the package was unloaded after the tool was listed). Unlike toolkit errors, both are returned as MCP tool errors (`isError: true`), as are failures the extension reports itself in its own wording.

## Cross-cutting contract rules

- **Host output lease**: direct `RenderTools` callers pass an `IOutputOperationLeaseProvider`, using `StandaloneOutputOperationLeaseProvider.Instance` outside the editor. DI and in-app hosts register their host-backed provider so render/export jobs cannot overlap a conflicting workspace operation.
- **Write boundary**: every tool with an `outputPath`/`path` write resolves it through `IWorkspaceGuard.ResolveForWrite` first; out-of-root ⇒ `workspace_boundary` (FR-026). Reads are never guarded, except the in-app AI tools' inputs, which are uploaded off the machine.
- **Strict tool arguments**: unknown MCP tool argument names return typed `validation_rejected` with the accepted parameter names; arguments are not silently ignored. Names matching a `patternProperties` pattern (ECMAScript semantics) are accepted. A tool whose input schema sets `additionalProperties` to anything but `false`, or combines subschemas or uses `$ref` at the top level, is not checked.
- **Atomicity**: `apply_edit` commits as exactly one undoable transaction; a mid-batch failure rolls back wholly (FR-012).
- **Validation surfaced**: coercion/rejection is always reported in the result, never silently applied (FR-007).
- **Stable handles**: all `*Id` are `CoreObject.Id` Guids, valid for the session; a removed or unknown update target ⇒ `stale_handle` (FR-011). `stale_handle` responses include a hint to omit `Id` for creation and to reuse Ids from `apply_edit.document` or `read_document` for updates.
- **Determinism**: `apply_edit` computes the change set before applying and returns the exact applied change set (SC-009).
- **Scene-rooted scope**: `apply_edit` operates on a **Scene** root (one `HistoryManager`). `create_project`, `save_project`, and scene add/remove + project-variable changes are **project-level, file-level** operations outside any scene's undo stack (data-model §Editing Session). The agent edits one scene at a time through the undoable surface.
- **Validation is computed, not inferred**: coercion/rejection in a result comes from running the property's validator explicitly (`SetValue` is `void`/coerces silently), so `apply_edit` reports the typed outcome (FR-007).


### Completed render-job retention

The in-process render job manager retains the most recent 128 completed/failed/cancelled jobs. Running jobs are not evicted. Poll and retain results promptly; a job ID outside that window is reported as unavailable.

Project-scoped live MCP configuration contains a bearer credential. Keep that generated configuration out of version control and shared artifacts. The credential intentionally survives application restarts so existing clients keep working; rotation and client reconfiguration must be explicit.
