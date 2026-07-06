# Feature Specification: Agent Editing Toolkit — MCP, Skills, and Subagents for AI-Driven Beutl Editing

**Feature Branch**: `001-agent-editing-toolkit`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "001 AIエージェントがBeutlを使って編集しやすくするように、MCPやSkill, Subagentを整備したい。"
(Translation: "Set up MCP, Skills, and Subagents so that AI agents can easily edit using Beutl.")

> **Scope note**: This feature is about making Beutl *operable by AI agents as a video-editing tool* — letting an agent author and modify Beutl projects on a user's behalf. It is **not** about the existing AI tooling that helps *develop the Beutl codebase* (build/test/review/spec). Those are different audiences and are out of scope here except as a precedent for how agent tooling is packaged in this repo.

## Clarifications

### Session 2026-06-27

- Q: Editing surface — drive the live GUI, or operate on project files headlessly? → A: Headless/programmatic — operate on Beutl project files through the non-UI editing layer; no live-GUI automation.
- Q: How much rendering/export is in the first release (v1)? → A: Include video export in v1 (the full encoder path; the GPL FFmpeg worker reached only over IPC), in addition to still-frame rendering.
- Q: Filesystem access scope for the agent-driven toolkit? → A: Reads may reference arbitrary local paths; all writes (project saves and render/export outputs) are restricted to a configured workspace, and write targets outside it are rejected.
- Q: How is audio (audio elements, sources, audio properties/effects) handled? → A: Audio is a first-class concern alongside visual — audio sources, per-element mixing, and audio effects are in scope.
- Q: Primary interaction model for editing — declarative desired-state vs imperative call-by-call? → A: Declarative editing surface — agents read the project as an identity-anchored document and submit a desired end-state (full document or partial patch) reconciled into undoable operations; project/session lifecycle and render/export remain separate tools.
- Q: Input format for partial declarative edits? → A: JSON Merge Patch (RFC 7396) — a partial subtree where a null member deletes — plus full desired-state documents. Beutl's serializer has no native patch mechanism, so the toolkit layer owns the merge → identity-diff → undoable-operations reconciliation (it must NOT bypass change-tracking by writing model state directly).
- Q: Can a human watch the agent's edits in the GUI in real time? → A: Yes — via **in-app hosting**: the running Beutl editor hosts an in-process endpoint that drives the same live scene and history the UI is bound to, so agent edits reflect live (preview/timeline/property panels) and land on the normal undo stack. The headless console server remains for the no-GUI case; the editing core is shared and only the *session source* differs (file-opened vs live editor). This is distinct from live-GUI automation (the agent does not simulate UI input — it edits the shared model and the UI observes it), which stays out of scope.
- Q: Does undo of agent edits survive closing and reopening the project? → A: No — same-session only. Beutl's undo history is per-`HistoryManager` and is not persisted across a reopen (true for human edits too); FR-015 covers undo **while the project is open in the editor** (live session, User Story 6), not cross-reopen replay.

## User Scenarios & Testing *(mandatory)*

The direct operator of this feature is an **AI agent** (e.g. Claude Code or any agent that can call external tools). The beneficiary is a **creator** who delegates editing work to that agent in natural language. A secondary stakeholder is the **extension author** whose custom drawables/effects should be reachable by agents on the same footing as built-ins.

### User Story 1 - Author a project from a brief (Priority: P1)

A creator describes a video in plain language — for example, "a 10-second 1080p clip: a title that fades in over a background image for the first 3 seconds, then a small logo in the bottom-right for the rest." The agent turns that brief into a real Beutl project: it creates the project and scene with the right canvas size, frame rate, and duration; adds timeline elements (text, image, shape) at the correct start times, durations, and layers; sets their core properties; and saves a project the creator can immediately open in Beutl and keep editing.

**Why this priority**: This is the minimum viable slice. The single most valuable thing an agent can do is turn intent into a valid, editable project. Everything else (refinement, verification, packaged know-how) builds on the ability to produce a project that opens cleanly in the app. Delivered alone, this already lets a creator go from a sentence to a working starting point.

**Independent Test**: Give the agent a set of natural-language briefs, have it produce projects, and open each in Beutl. Success = the project opens without error and its structure (elements, times, layers, content) matches the brief. No other story needs to exist for this to deliver value.

**Acceptance Scenarios**:

1. **Given** an empty workspace and a brief that specifies canvas size, duration, and a few elements, **When** the agent runs the authoring flow, **Then** a saved project exists that opens in Beutl with the requested scene dimensions/duration and one timeline element per requested item at the requested time/layer.
2. **Given** a brief that references an existing local image and a piece of title text, **When** the agent authors the project, **Then** the image element points at the supplied media file and the text element renders the requested string.
3. **Given** a brief whose requested duration or element timing is internally inconsistent (e.g. an element starting after the scene ends), **When** the agent authors the project, **Then** the agent receives a clear, typed warning/error describing the conflict rather than silently producing a broken project.

---

### User Story 2 - Inspect and refine an existing project (Priority: P2)

The creator already has a project and asks the agent to change it — "move the title two seconds later, make it bigger, and add a fade-out." The agent opens the project, returns a structured description of its scenes, elements, properties, animations, and effects so it can reason about what is there, then applies the targeted edits — adjusting a property, retiming/moving/resizing an element, adding or editing a keyframe animation, attaching or tuning a filter effect — and saves the result without corrupting the file. Entities are addressable by stable handles so the agent can refer back to "that title element" across several steps, and a failed step does not leave the project half-edited.

**Why this priority**: Real editing is iterative. Once an agent can produce a project (P1), the next most valuable capability is changing one that already exists — whether the agent made it or a human did. This is what turns the toolkit from a one-shot generator into an editing assistant.

**Independent Test**: Take a representative set of existing projects, issue a scripted edit ("retime element X by +2s", "add a fade-out on Y"), and confirm the saved project reflects exactly that change, still opens in Beutl, and that nothing else in the project changed.

**Acceptance Scenarios**:

1. **Given** an existing project, **When** the agent requests its structure, **Then** it receives a machine-readable description listing each scene, each element with its time range and layer, the editable properties with their current values, and any animations/effects attached.
2. **Given** an element referenced by a stable handle, **When** the agent sets a property to a new valid value, **Then** the saved project reflects the new value and no other element or property is altered.
3. **Given** a multi-step edit where one step fails (e.g. an invalid value mid-batch), **When** the batch runs, **Then** the project is left exactly as it was before the batch (no partial edits persisted) and the agent is told which step failed and why.
4. **Given** a desired-state document or a JSON Merge Patch describing the intended change, **When** the agent requests a dry-run plan, **Then** it receives the exact set of changes that would be made plus any validation rejections/range coercions, with the project left unmodified; a subsequent apply commits the same change set atomically and undoably.

---

### User Story 3 - Discover capabilities and get safe, actionable feedback (Priority: P2)

Before and during editing, the agent can ask "what can I edit, and what are the valid values?" — it enumerates the available element/drawable types, filter effects, and encoders, and for each one the editable parameters with their type, unit, valid range, and default. When the agent attempts something invalid — a value outside a property's allowed range, a missing media file, an operation the target does not support — it gets an actionable, machine-readable error explaining what went wrong and how to fix it, instead of a silent no-op, a silently clamped value with no notice, or a crash.

**Why this priority**: Agents are only reliable when the surface is self-describing and fails loudly. Without capability discovery an agent guesses parameter names and ranges; without actionable errors it cannot self-correct and may corrupt a project. This story is what makes P1 and P2 trustworthy rather than fragile, so it shares P2 priority — the authoring/editing stories are not production-ready without it.

**Independent Test**: Query the capability surface and diff it against what the Beutl GUI exposes for the same types; then feed a battery of deliberately invalid edits and confirm each returns a typed, descriptive error and leaves the project uncorrupted.

**Acceptance Scenarios**:

1. **Given** a request to list editable content types, **When** the agent queries the capability surface, **Then** it receives every built-in element/drawable/effect/brush/encoder type that the GUI can edit, each with its parameters and the parameters' types, units, valid ranges, and defaults.
2. **Given** a property with a declared valid range, **When** the agent sets a value outside that range, **Then** the operation returns a typed error (or an explicit, reported coercion) rather than silently swallowing the value, and the agent can read the allowed range from the response.
3. **Given** a request that references a media file that does not exist or an effect type that is not installed, **When** the agent issues it, **Then** the agent receives a specific error naming the missing file/type and the project is not modified.

---

### User Story 4 - Render a project to verify the result (Priority: P3)

After authoring or editing, the agent renders the project — a single still frame at a chosen time, a short range, or the whole timeline — to an image or video file without launching the full editor UI, so the agent (and the creator) can visually confirm the result before handing it back. Still-frame/image rendering needs no GPL-licensed component; video export reaches the GPL encoder only through Beutl's existing process boundary.

**Why this priority**: Verification closes the loop for an autonomous agent. It is sequenced after the editing core (hence P3 in build order) because a project that opens in the GUI can already be previewed by a human, and rendering carries the most environmental risk (GPU availability, codecs). It is nonetheless **in scope for the first release — including video export** (see Clarifications 2026-06-27); the priority reflects sequencing, not exclusion.

**Independent Test**: Have the agent author a project, request a still frame at a given time, and confirm an image file is produced that depicts the expected composition; separately request a short video export and confirm a playable file is produced via the existing encoder path.

**Acceptance Scenarios**:

1. **Given** a project and a target time, **When** the agent requests a still frame, **Then** an image file is produced depicting the scene at that time, without the editor UI running.
2. **Given** a project and a time range, **When** the agent requests a video export, **Then** a playable video file is produced through the existing encoder path and the request never required an MIT component to link the GPL encoder directly.
3. **Given** an environment with no usable GPU or codec, **When** the agent requests a render/export, **Then** it receives a clear, typed "rendering unavailable" error instead of an unhandled crash.

---

### User Story 5 - Packaged editing know-how and specialists (Priority: P3)

The toolkit ships reusable, discoverable guidance for common editing tasks (recipes/playbooks — the "Skill" pillar) and specialized delegated workers for scoped editing sub-tasks (the "Subagent" pillar), such as "lay out a timeline from a shot list" or "apply a consistent look/effect chain." An orchestrating agent (and the human configuring it) get correct, repeatable results that follow Beutl's conventions without re-deriving them from source each time.

**Why this priority**: The raw editing surface (P1–P3) is enough for a capable agent to operate Beutl, but packaged know-how and specialists are what make results *consistent and low-effort* across agents and sessions. They multiply the value of the underlying surface, so they follow it rather than precede it.

**Independent Test**: With only the shipped guidance available (no Beutl source reading), have an agent complete a representative editing task end-to-end and confirm it follows the documented recipe and produces a correct result; invoke a packaged specialist on a scoped sub-task and confirm it completes that sub-task in isolation.

**Acceptance Scenarios**:

1. **Given** a common editing task with a shipped recipe, **When** an agent follows that recipe without reading Beutl source, **Then** it completes the task correctly and consistently across repeated runs.
2. **Given** a scoped sub-task (e.g. "build the timeline from this shot list"), **When** an orchestrating agent delegates it to the corresponding specialist, **Then** the specialist completes just that sub-task and returns a result the orchestrator can use.

---

### User Story 6 - Watch the agent edit live in the editor (Priority: P2)

A creator opens a project in the Beutl editor and asks the agent — connected to the editor's in-process endpoint — to make changes. As the agent applies edits, the creator sees them appear live: the preview repaints, the timeline updates, property panels reflect the new values, and each change lands on the editor's normal undo stack, so the creator can undo/redo or take over at any moment.

**Why this priority**: Live observation turns the agent from a batch tool into a *supervisable collaborator* — the creator can watch, trust, and intervene mid-edit. It is sequenced after the headless editing core (US1–US3) because it reuses that core's reconcile/apply path against a *live* session instead of a file-opened one; it is not the MVP, but it is an experience the project explicitly wants, so it ranks above render/skills polish.

**Independent Test**: Open a project in the editor, drive an edit through the in-process endpoint, and confirm the preview/timeline/property UI update without a manual reload and that the change is a single undoable history entry — all without simulating any UI input.

**Acceptance Scenarios**:

1. **Given** a project open in the editor with the in-process endpoint active, **When** the agent applies a property change, **Then** the preview and the relevant property panel update live (no reload) and the change is one entry on the editor's undo stack.
2. **Given** the agent adds or retimes an element via the endpoint, **When** the edit commits, **Then** the timeline reflects the new/moved element immediately.
3. **Given** the creator presses undo after an agent edit, **When** the editor processes it, **Then** the agent's change reverts through the normal history, identical to undoing a human edit.

---

### Edge Cases

- **Missing or unsupported media**: the agent references a media file that does not exist or whose format Beutl cannot decode → specific error, project unchanged.
- **Out-of-range / coerced values**: a property has a declared valid range (model-enforced) and the agent supplies a value outside it → the agent is told (typed error or an explicit, reported coercion), never a silent clamp with no signal.
- **Schema/version skew**: a project was written by a different Beutl version than the runtime loading it → behavior follows Beutl's existing serialization compatibility; the agent gets a clear error if the project cannot be loaded.
- **Concurrent access**: the live editor has the project open while the agent edits the same file → the toolkit must not silently clobber unsaved GUI state; stale/conflicting writes surface as an error rather than data loss.
- **No GPU / no codec**: verification render requested in a headless or GPU-less environment → typed "unavailable" error, no native crash.
- **GPL boundary**: a video-export request must never cause an MIT component to link the GPL encoder directly; it must route through the existing process boundary, or be refused.
- **Uninstalled extension type**: the agent references a drawable/effect/encoder provided by an extension that is not installed → specific error naming the type, with a hint that the package is missing.
- **Stale handle**: the agent refers to an element/property that was removed earlier in the session → typed "no longer exists" error, not a crash or a wrong-target edit.
- **Atomic batch failure**: a multi-step edit fails partway → the whole batch rolls back to the pre-batch state.
- **Large projects**: very large projects (many elements/keyframes) must not produce unbounded payloads or hang; structure queries should remain usable (e.g. scoping/pagination) and operations stay responsive.
- **Destructive intent**: a request that would delete content or overwrite an existing project file is treated as explicit and confirmable, never an implicit side effect of an unrelated edit.
- **Write outside the workspace**: a save or render/export target that resolves outside the configured workspace root → rejected with a typed error; reads from arbitrary paths remain allowed.
- **In-app, no active session**: an agent calls a live-session tool while no project/scene is open in the editor → typed "no active editor session" error.
- **In-app concurrent input**: an agent edit arrives while the human is mid-drag or has the same element selected → the edit is serialized onto the editor's single writer thread, producing no torn state and one clean undo entry.

## Requirements *(mandatory)*

### Functional Requirements

**Authoring & structure**

- **FR-001**: The toolkit MUST let an agent create a new project with a specified canvas size, frame rate, and duration, and persist it as a valid Beutl project that the editor opens without error.
- **FR-002**: The toolkit MUST let an agent create and add a scene to a project, specifying its frame size, start, and duration.
- **FR-003**: The toolkit MUST let an agent add, remove, and reposition timeline elements with a start time, duration/length, and layer (Z-order).
- **FR-004**: The toolkit MUST let an agent set an element's content — visual (image, video, text, shape, group) and audio (sound source / audio track) — and bind it to a local media source where applicable.

**Query & capability discovery**

- **FR-005**: The toolkit MUST return a structured, machine-readable description of a project: its scenes, each element's time range and layer, the editable properties with current values, and any attached animations and effects.
- **FR-006**: The toolkit MUST let an agent enumerate the available element/drawable types, filter effects, brushes, and encoders, including for each one the editable parameters and each parameter's type, unit, valid range, default value, and collection element type where applicable.

**Editing**

- **FR-007**: The toolkit MUST let an agent read and write any editable property of a scene, element, drawable, brush, or effect — visual or audio — enforcing the property's declared validity (range/units) and reporting — never silently swallowing — any rejection or coercion.
- **FR-008**: The toolkit MUST let an agent add, edit, and remove keyframe animations on animatable properties, including keyframe time, value, and easing.
- **FR-009**: The toolkit MUST let an agent attach, configure, reorder, and remove effects — visual filter effects and audio effects — on a drawable or element.
- **FR-010**: The toolkit MUST expose the structural editing operations the GUI offers for elements — at minimum move/retime, resize, duplicate, split, and group/ungroup.

**Integrity, addressability & feedback**

- **FR-011**: Every project entity an agent can edit MUST be addressable by a stable identifier that remains valid for the lifetime of an editing session, so the agent can refer back to the same entity across operations.
- **FR-012**: A multi-step edit MUST be applied atomically — a failure partway through leaves the project in its exact pre-batch state, with no partially applied edits persisted.
- **FR-013**: Opening a project and saving it back without edits MUST preserve all content that the current Beutl runtime can deserialize (round-trip fidelity). Unknown or incompatible schema/content MUST surface as an error instead of being carried forward outside the current schema.
- **FR-014**: Every operation MUST return an actionable, machine-readable result — either success with a description of what changed, or a typed error stating the reason and how to correct it. The toolkit MUST NOT fail silently or surface an unhandled crash to the agent.
- **FR-015**: Edits an agent performs MUST be reconcilable with Beutl's existing undo/redo model so that a human can undo agent changes through the normal editor flow **within the same editing session** (the toolkit MUST NOT bypass the change-tracking the editor relies on). Beutl's undo history is per-`HistoryManager` and is **not** persisted across a close/reopen — agent edits behave exactly like human edits in this respect, so cross-reopen undo replay is out of scope (consistent with the "Undo scope is the Scene" assumption).

**Rendering & verification**

- **FR-016**: The toolkit MUST let an agent render a still frame of a scene at a given time to an image file without running the editor UI, using a path that requires no GPL-licensed component.
- **FR-017**: The toolkit MUST let an agent export a time range or the whole timeline to a video file through Beutl's existing encoder path (in scope for the first release), reaching the GPL encoder only across the existing process boundary (never via a direct compile-time link from an MIT component).
- **FR-018**: When rendering or export is unavailable in the current environment (no usable GPU or codec), the toolkit MUST return a clear, typed error rather than crashing.

**Guidance & specialists**

- **FR-019**: The toolkit MUST ship discoverable, reusable guidance (editing recipes/playbooks) for common editing tasks, so an agent can produce correct results without reading Beutl source.
- **FR-020**: The toolkit MUST ship specialized delegated workers for scoped editing sub-tasks that an orchestrating agent can invoke and compose.
- **FR-021**: The editing/query/render surface MUST be consumable by a standards-based agent tool host (the Model Context Protocol the request names), with Claude Code as the first-class reference host, since the repository's existing AI tooling is Claude-centric.

**Extensibility & boundaries**

- **FR-022**: Custom drawables, effects, and encoders provided by installed extensions MUST be reachable through the same discovery and editing surface as built-ins — no separate, second-class path.
- **FR-023**: The toolkit MUST live within the MIT side of the codebase and MUST NOT blur the MIT/GPL boundary; any reliance on the GPL FFmpeg worker happens only through the existing inter-process boundary.
- **FR-024**: Destructive operations (deleting content, overwriting an existing project file) MUST be explicit and confirmable; the toolkit MUST NOT overwrite or delete a project the agent did not explicitly open or create.

**Audio**

- **FR-025**: The toolkit MUST treat audio as a first-class editing concern alongside visual content — an agent can add and replace audio sources, edit audio properties (e.g. volume/gain, offset, looping/trim), attach and configure audio effects, and control basic per-element mixing levels — through the same generic discovery/edit/query surface used for visual content (no second-class audio path).

**Filesystem & workspace safety**

- **FR-026**: The toolkit MAY read media and project files from arbitrary local paths, but MUST restrict all writes — project saves and render/export outputs — to a configured workspace root, rejecting any write target that resolves outside it with a typed error.

**Declarative editing model (primary interaction)**

- **FR-027**: The toolkit's public editing interaction MUST be declarative — an agent reads the project (or any subtree) as a normalized, identity-anchored document and submits a desired end-state rather than being limited to a long sequence of imperative calls.
- **FR-028**: Partial declarative edits MUST be expressible as a JSON Merge Patch — a partial subtree in which a null member deletes — applied against the current document, and the toolkit MUST also accept a full desired-state document. Object members follow RFC 7396; **arrays of identity-bearing entities** (elements, content objects, keyframes) MUST be applied by **id-keyed merge** (members matched by `Id`, unmentioned members left untouched, an explicit deletion marker deletes one — deleting an already-absent member is an idempotent no-op) so changing one element never drops its siblings — only scalar / non-identified arrays follow strict RFC 7396 wholesale replacement. Ordering MUST be deterministic: existing member order is preserved, explicit position directives (`$index`/`$after`/`$before`) reposition, and keyframes are ordered by time. An agent MUST NOT have to reconstruct unrelated state in order to change one field.
- **FR-029**: A declarative submission MUST be reconciled by matching entities on their stable identifiers (FR-011) and translated into the editor's undoable operations, applied atomically within a single **commit-or-rollback** history transaction (FR-012/FR-015) — a mid-reconcile failure MUST roll the live model back, not leave partial mutations applied. The toolkit MUST NOT bypass change-tracking by writing model state directly. Reconciliation MUST emit a minimal change set: update only changed properties, and insert/remove/move collection members by identity rather than replacing collections wholesale. The undoable transaction is scoped to a **Scene** (the unit that carries a history); **project-level** changes — creating a project, adding/removing scenes, project variables (frame rate, sample rate) — are separate, coarser, file-level operations and need not participate in a scene's undo history.
- **FR-030**: The toolkit MUST expose `apply_edit` as the single public edit entry point. It MUST compute validation and the change set before mutating, apply the edit atomically, and return the exact applied change set plus validation results so agents do not need a separate preview/apply exchange.
- **FR-031**: Declarative documents MUST carry a schema/version stamp. When a submitted document's version does not match the runtime's known schema, the toolkit MUST surface a typed error rather than accepting or preserving unrecognized content outside the current schema.

**Real-time reflection (in-app hosting)**

- **FR-032**: The editing core MUST support two session sources behind one reconcile/apply surface: a **file-opened session** (headless) and a **live session** bound to a running editor's in-memory scene + history. The same tools MUST behave identically against either source.
- **FR-033**: When hosted **in-process within the running Beutl editor**, agent edits MUST be applied to the same live model the UI is bound to, so they reflect in real time (preview, timeline, property panels) and appear as normal, human-undoable entries on the editor's existing history — no separate, duplicate, or shadow history.
- **FR-034**: In-app edits MUST be marshaled onto the editor's single writer thread and MUST share the editor's one history/change-tracking pipeline (the agent is an additional command source, not a second writer), so there is no concurrent-writer corruption or torn UI state.
- **FR-035**: The in-app endpoint MUST be reachable by an external agent host over a local connectable transport (a running GUI cannot be stdio-spawned), MUST bind to loopback only, and MUST NOT relax any editing guarantee — validation (FR-007), atomicity (FR-012), and the workspace write-boundary (FR-026) still hold. When no project/scene is open, tools that need a live session MUST return a typed "no active editor session" error.

### Key Entities

- **Project**: The top-level document an agent authors/edits; holds one or more scenes and project-level variables (frame rate, sample rate). Persisted as a Beutl project file.
- **Scene**: A timeline container with a frame size, start, and duration; holds elements and timeline metadata.
- **Element**: The atomic timeline unit, with a start, length, and layer (Z-order); holds the content objects that produce its visuals/audio.
- **Content (drawable / audio)**: The visual or audio content inside an element — image, video, text, shape, group, or audio source — carrying its own editable properties (transform, opacity, blend mode, volume/gain, attached effects).
- **Property**: A named, typed, editable attribute of a scene/element/drawable/brush/effect, carrying validity metadata (type, unit, valid range, default) and optionally an animation.
- **Keyframe Animation**: A time-ordered set of keyframes (time, value, easing) bound to an animatable property, sampled at composition time.
- **Effect (visual filter / audio)**: A post-processing pass (single or grouped) attached to a drawable/element and applied during composition — visual filter effects and audio effects on the same footing.
- **Capability Descriptor**: The machine-readable schema of what is editable — the enumerated types and their parameters/ranges/defaults — that an agent reads to plan edits.
- **Edit Operation / Transaction**: A single addressable change or an atomic batch of changes, reconcilable with the editor's undo/redo history.
- **Render / Export Job**: A request to produce an image (still frame) or video (range/timeline) artifact for verification or delivery.
- **Editing Session**: The agent's stateful working context over one or more projects, within which entity handles remain stable. A session has a **source** — either *file-opened* (headless: the toolkit loads its own in-memory copy) or *live* (in-app: bound to the running editor's existing scene + history) — behind the same edit surface.
- **Editing Recipe (Skill)** / **Editing Specialist (Subagent)**: Packaged, discoverable know-how and scoped delegated workers that operate the surface above on the agent's behalf.
- **Declarative Document / Patch**: The identity-anchored JSON desired-state an agent submits to express edits — either a full document or an RFC 7396 merge-patch (partial subtree, null deletes) — mirroring the project's own serialization so the agent reads and writes the same shape.
- **Reconciliation (plan / apply)**: The diff of a desired-state (or merge-patch-expanded) document against the current project, matched by stable identity, into a minimal set of undoable operations. "plan" computes and previews it (with validation results) without mutating; "apply" commits it atomically within one history transaction.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a one-paragraph brief drawn from a representative brief set, an agent produces a project that opens in Beutl without errors on the first attempt in at least 90% of cases.
- **SC-002**: Of all invalid edit attempts (out-of-range value, missing media, unsupported operation) in a test battery, 100% return an actionable, typed error and 0% result in a corrupted project or an unhandled crash.
- **SC-003**: Across a representative current-format project set, opening a project and saving it back with no edits preserves 100% of runtime-deserializable project content.
- **SC-004**: For the **v1 capability scope** (defined in Assumptions — element types image/video/text/shape/group/audio-source; all schema-discoverable properties; keyframe add/edit/remove + easing; brush/fill edits including gradient stops; visual-filter and audio effects attach/configure/reorder/remove; structural move/resize/duplicate/split/group), an agent can perform at least 90% of the editing operations a human can perform through the GUI *within that scope*.
- **SC-005**: Capability discovery is complete — 100% of the built-in and installed-extension types editable in the GUI are also discoverable and editable through the toolkit.
- **SC-006**: A new integrator (a human plus an agent) can go from zero to a first valid generated project using only the shipped guidance — without reading Beutl source — in under 15 minutes.
- **SC-007**: For projects the agent authored, it can produce a verification artifact — a still frame, and a short video export through the encoder path — for at least 95% of them without manual intervention (in an environment where rendering and encoding are supported).
- **SC-008**: An agent receives the result of a single edit or query operation quickly enough to sustain an interactive loop — under 2 seconds for a typical project.
- **SC-009**: A declarative dry-run ("plan") predicts the applied result exactly — across a representative set of edits, 100% of the change-set entries and validation outcomes reported by plan match those produced by the subsequent apply (no surprise mutations, no divergence between preview and commit).
- **SC-010**: When hosted in-app, an agent edit reflects in the running editor (preview/timeline/property panels update with no manual reload) and registers as exactly one entry on the editor's undo stack — for 100% of edits in a representative set, with no torn UI state.

## Assumptions

- **Delivery shape**: The three mechanisms the request names map to (1) a programmatic editing/query/render surface exposed over the Model Context Protocol ("MCP"), (2) packaged editing recipes ("Skills"), and (3) scoped delegated workers ("Subagents"). This spec states the *capabilities* independently of any one mechanism; the mechanisms are the agreed delivery vehicles, not incidental implementation detail.
- **Headless/programmatic, not GUI automation** *(confirmed — Clarifications 2026-06-27)*: Scope is programmatic authoring, editing, querying, and rendering of Beutl project files. Driving the *running* GUI app by simulating clicks/pixels is out of scope. This is chosen because Beutl already has a non-UI editing layer (`Beutl.Editor` services + the undo/redo history/operation stack), so building on the non-UI layer is the lower-risk, more testable path.
- **Two hosting modes share one core** *(confirmed — Clarifications 2026-06-27)*: the same editing core is hosted (a) as a standalone headless server over a stdio-spawned process (file-opened sessions), and (b) **in-process inside the running editor** over a loopback connectable endpoint (live sessions) so edits reflect in the UI in real time (User Story 6). In-app hosting is *live observation of the shared model*, not GUI/pixel automation.
- **Builds on existing infrastructure**: The toolkit reuses Beutl's existing non-UI editing services, change-tracking/undo history, project serialization, and rendering engine rather than introducing a parallel editing engine.
- **Current on-disk format**: Projects use Beutl's current project/scene/element file format. Cross-version migration is governed by Beutl's existing serialization compatibility, not added by this feature.
- **Media is pre-existing**: Media assets the agent references already exist on the local filesystem; this feature does not source or generate media (an agent may combine it with separate media-generation tools).
- **Local editing only**: Cloud features (accounts, marketplace, sync via the server API) are out of scope; this is local project editing.
- **Render licensing split**: Still-frame/image rendering uses the MIT rendering path and requires no GPL component; video export reuses the existing encoder path and reaches the GPL worker only across the existing process boundary.
- **Agent host**: Any MCP-capable agent host is supported; Claude Code is the first-class reference host because the repository's AI tooling is already Claude-centric.
- **Workspace boundary** *(confirmed — Clarifications 2026-06-27)*: A configured workspace root scopes all writes (project saves, render/export outputs); reads may reference media/projects at arbitrary local paths. This least-privilege default suits an autonomous agent driving the toolkit.
- **Audio is first-class** *(confirmed — Clarifications 2026-06-27)*: Audio sources, per-element mixing, and audio effects are in scope on the same footing as visual content, reached through the same generic edit/query surface; no audio-specific path is special-cased.
- **v1 capability scope** (makes SC-004 measurable): the agent-editable surface for v1 is — *element types*: image, video, text, shape, group, audio source; *properties*: every schema-discoverable property of a scene/element/drawable/brush/effect (visual + audio); *animation*: keyframe add/edit/remove with easing; *brushes*: fill/brush properties including gradient stops; *effects*: visual-filter and audio effects attach/configure/reorder/remove; *structure*: element move/retime/resize/duplicate/split/group/ungroup. **Excluded from v1**: node-graph-specific authoring, 3D-scene authoring, and any plugin-supplied custom property-editor UI semantics (the generic surface still edits their underlying properties).
- **Undo scope is the Scene**: the unit that carries an undo history (one `HistoryManager`) is a Scene. Declarative reconcile/apply operates against a Scene root. Project-level operations (create project, add/remove scenes, project variables) are separate, coarser, file-level actions and are not part of a scene's undo stack.

## Out of Scope

- **GUI/pixel automation** of the running Beutl application (driving the live UI by simulated clicks/keystrokes/pixels). *(Note: in-process **live hosting** — where the agent edits the shared model and the GUI observes the result — is IN scope per Clarifications and User Story 6. What stays out is the agent operating the UI by simulated input.)*
- **The agent/LLM itself** — the consuming agent is assumed to exist; this feature builds the tools it calls, not the model.
- **Media generation** — creating images, audio, or video assets is a separate concern.
- **Real-time collaborative editing** or multiple agents writing to the same project concurrently.
- **Cloud sync / marketplace / account operations** (the existing server API).
- **A new on-disk format or a new rendering engine** — this feature consumes the existing ones.
- **Developer-facing AI tooling** for the Beutl codebase itself (build/test/review/spec) — that already exists and is a different audience.

## Delivered Extensions

The toolkit shipped five design extensions after the original spec was approved. Each is fully implemented in this branch; their design notes were consolidated here from `docs/superpowers/specs/` and `docs/benchmarks/` to keep `docs/` aligned with the Spec-Kit layout (`docs/specs/<NNN>-<slug>/`). The benchmark briefs and vision-scoring baselines that exercise these extensions live under `checklists/briefs/` and `checklists/visual-quality-baselines/`.

### Visual-Quality Improvement (2026-07-02)

**Problem**: AI-authored motion graphics regressed to amateur-looking output — clashing palettes, weak typography, cluttered composition, unnatural motion, flat backgrounds, and sparse scenes.

**Delivered approach** — three layers plus an evaluation harness:

| Root cause | Layer | Implementation |
|---|---|---|
| No vocabulary of good palettes/backgrounds/motion — the model invents from scratch | **L1 Parametric design system** | `ColorHarmonyEngine` (828 lines) — `derive_palette` expands a brief-derived seed into a role-tagged palette with guaranteed contrast; background-recipe grammar in `VideoTypeCatalog`; anti-repeat feeds `CreativeMemoryStore` fingerprints into the derivation step |
| The agent never sees its own rendered output | **L2 Visual feedback loop** | `render_still` / `render_storyboard` gain `returnImageContent` (MCP `ImageContent`); contact-sheet compositing via `StillRenderer`; `beutl-agent-visual-review` skill with the 6-axis rubric |
| Sparseness / disharmony not measurable numerically | **L3 Heuristic upgrades** | `QualityAnalyzer` (4171 lines) — layer-density/depth metrics, color-harmony scoring, background-richness check |

**L0 Evaluation harness** lives under `checklists/briefs/` (10 fixed briefs) and `checklists/visual-quality-baselines/` (recorded vision-model axis averages); see `checklists/visual-quality-baseline.md` for the generation + scoring procedure.

**Key invariant**: no fixed style packs — variety comes from the brief's seed, the quality floor from the rules. Prior art of fixed packs converging to one look was explicitly rejected.

### Video-Type-Aware Workflows (2026-07-03)

**Problem**: the original single workflow assumed motion-graphics (BPM beat grids, background grammar, 2-3 foreground layers); other video types tripped inapplicable gates or missed type-specific steps.

**Delivered**: `VideoTypeCatalog` (`src/Beutl.AgentToolkit/Design/VideoTypeCatalog.cs`, 288 lines) — five first-class `videoType` profiles (`motion-graphics`, `footage-cut`, `slideshow`, `lyric-captions`, `logo-intro`). The one `beutl-agent-timeline-from-shotlist` skill gained a Phase -1 classification step and a per-type flow matrix; no per-type skill forks. The `videoType` parameter threads through `evaluate_edit_quality`, `preview_quality_risks`, `suggest_quality_fixes`, `final_preflight`, and `get_started`, applying implied intent flags + analyzer applicability. A new advisory `timelineCoverage` reports gaps for footage-cut/slideshow. Backward compatibility: omitted `videoType` is byte-for-byte `motion-graphics` (characterized by tests).

### Autonomous Asset Sourcing (2026-07-03)

**Problem**: the toolkit assumed media files already existed; agents either refused footage-driven briefs or fetched files ad hoc with no licensing discipline and no provenance trail.

**Delivered**: `beutl-agent-asset-sourcing` skill — skill-driven, no server-side providers (v1). The agent uses its own web capabilities following a binding contract: source-or-generate decision per asset, recommended sources (Openverse, Pexels, Pixabay, Freesound, Google Fonts, ...), license policy (CC0/CC-BY/OFL allowed; CC-BY-SA recorded; NC/ND forbidden autonomously), provenance manifest at `<workspace>/assets/manifest.json`, download conventions, quality criteria, and a failure path back to procedural generation. The skill is registered in `get_started`'s `CreateRecommendedSkills()` and referenced from footage-cut/slideshow/lyric-captions workflow steps.

### Low-Effort Brief Pipeline (2026-07-03)

**Problem**: output quality correlated with how carefully the brief was written. Terse prompts ("かっこいいロゴイントロ作って") produced weak videos because the weakest axes (`layerDensityDepth` 2.5, `backgroundRichness` 2.7 in the 2026-07-03 baseline) were exactly the qualities under-specified prompts fail to request.

**Delivered**: three accepted directions (B/D/E); preset libraries, template scaffolds, and creative-memory defaulting were **rejected by the user** for converging every run onto the same look.

- **B — Brief expansion**: `beutl-agent-brief-expansion` skill. Trigger: terse prompt missing two or more of subject/duration/mood/style/asset inventory. Mechanism: record literal constraints, sketch three structurally divergent concept candidates (checked against `recentToAvoid`), emit an Expanded Brief block feeding `derive_palette`/background grammar/plan sheets.
- **E — Reference-based direction**: same skill, second intake path. User-supplied reference images/video are fetched, stored under `references/` with a `use: "direction-only"` manifest, and abstract attributes (hue family, tonal seed, layer-density profile, motion vocabulary, ...) are extracted via vision. Prohibited: reproducing logos/marks/characters/illustrations or reconstructing the composition wholesale.
- **D — Quality convergence loop**: `beutl-agent-visual-review` extension. Loop: score six axes → concrete directives → smallest coherent revision pass → re-render → `compare_revisions` → rescore. Exit: every axis ≥ 3 or `maxPasses` (default 3) exhausted. Anti-genericization (directives phrased in the piece's own concept vocabulary; stock-particle/glow/grain purely to raise a score is forbidden) and anti-oscillation (an axis ≥ 4 is only edited to repair a regression) guardrails.

**Server-side**: registration only — `Beutl.AgentToolkit.csproj` EmbeddedResource + `BundledAgentToolkitAssets` + `QueryTools.CreateRecommendedSkills`. No new MCP tools.

### Visual-Quality Backlog (2026-07-03)

A theory-grounded backlog of ten tasks (T1–T10), each naming the film/motion-design theory it operationalizes so the implementation has a measurable target instead of taste. Status: **T1–T8 delivered in this branch; T9–T10 remain backlog** (each task implemented only on explicit request).

| Task | Theory | Status |
|---|---|---|
| T1 Benchmark baseline run | Dailies / screening-room practice | ✅ Delivered — `checklists/visual-quality-baselines/2026-07-03-baseline-t2t8.md` + `2026-07-03-low-effort-bde.md` |
| T2 Audio-driven timing grid (`analyze_audio_rhythm`) | Eisenstein's metric/rhythmic montage; Chion's synchresis | ✅ Delivered — `AudioRhythmAnalyzer.cs` (625 lines) + tests |
| T3 Rendered text contrast | WCAG 2.x contrast measured against the rendered result | ✅ Delivered — `QualityAnalyzer.TypographyContrastSample` per-text sampling |
| T4 Easing & motion-monotony analysis | Disney's 12 principles (slow-in/slow-out, anticipation, follow-through) | ✅ Delivered — `MotionVariationAnalyzer.cs` + easing-diversity metric |
| T5 Eye-trace continuity across cuts | Murch's Rule of Six (eye-trace 7%) | ✅ Delivered — storyboard-subdivision cut-continuity pass |
| T6 Transition vocabulary + consistency classification | Bordwell & Thompson continuity-editing grammar | ✅ Delivered — `TransitionVocabularyMetrics` + `TransitionBoundaryClassification` |
| T7 Palette role-balance (60-30-10) | Itten's contrast-of-extension | ✅ Delivered — `PaletteBalanceMetrics` + `PaletteRoleShare` |
| T8 Revision diff review (before/after ledger) | Editorial QC regression discipline | ✅ Delivered — `compare_revisions` flow + per-axis delta ledger |
| T9 Quality-outcome feedback into creative memory | Ericsson's deliberate-practice / critique loops | ⏳ Backlog — `CreativeMemoryStore` is anti-repeat only; per-axis quality feedback not yet wired |
| T10 Export QC (decode-back + loudness) | EBU R128 / ITU-R BS.1770 broadcast QC | ⏳ Backlog — `export_video` does not yet decode-back or compute integrated loudness |

**Non-goals across the backlog**: no new blocking gates except T3 (folded into the existing read-time/typography family); no ML-trained aesthetic scorers in-process; no beat-tracking research project (T2 is peak-picking on a novelty curve).

## Dependencies

- Beutl's existing non-UI editing services and undo/redo history/operation infrastructure (`Beutl.Editor`).
- Beutl's project/scene/element serialization (`Beutl.ProjectSystem` + the core serializer).
- The engine renderer for verification stills, and the existing encoder path (with the GPL FFmpeg worker reached only over IPC) for video export.
- The extension/package system, so non-built-in drawables/effects/encoders are reachable.
- An MCP-capable agent host (Claude Code as the reference host).
