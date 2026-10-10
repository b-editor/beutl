---
name: beutl-agent-timeline-from-shotlist
description: Edit Beutl timeline elements, timing, transforms, groups, and keyframes through the Agent Editing Toolkit MCP tools.
---

# Beutl Timeline Editing

Beutl provides editing operations and rendered output. The agent decides the visual design and evaluates the result against the user's request. Operation success and numerical measurements do not establish visual quality or completion.

## Session and document mechanics

- The `beutl-agent` server edits either a running Beutl editor or project files. To edit a running editor, use `list_instances` and `list_scenes`, then pass `instanceId` and `sceneId` on each scene read, edit, history, or render call. Calls preserve the visible editor selection; no attach or previous call selects a target. To work headlessly on files, omit `instanceId` and use `create_project`/`open_project` for a file-backed session. `read_operation_status` reports persistence behavior. File sessions use `save_project`; live edits use the editor's normal save behavior.
- `read_document_summary` returns compact handles. `read_document` returns serialized data. Query `get_schema(type=...)` for unfamiliar types or properties; enumerating catalogs is optional. Examples and presets are syntax aids and reusable edits, not the range of possible expressions.
- `apply_edit` accepts `schemaVersion: "1"`. An Id-based merge patch preserves unmentioned siblings; a full desired document is authoritative. Use PascalCase property names and the `$type` discriminators returned by the runtime. Read `validation` and `createdIds` to resolve technical errors and target newly created objects.
- New timeline entries use `$type: "[Beutl.ProjectSystem]:Element"` with drawables under `Objects`. Read `get_examples(name: "insert-new-element-skeleton")` when the container shape is unfamiliar.
- Element `Start` and `Length` define its time range; the end is exclusive. Tool sample times are relative to the scene's visible window. Keyframe `UseGlobalClock=false` means Element-local `KeyTime`; `true` uses the scene clock. Do not add the Element's Start to local keyframes.
- Name elements and objects after their visible content or purpose in the user's language. Use compact `ZIndex` values and reuse rows across non-overlapping shots. Preserve overlapping draw order.

## Groups and transforms

Multiple `Objects` form a flow chain. `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, and `Scene3D` consume upstream objects; they are not ordinary independent layers. To combine timeline layers, place a `PortalObject` before the consuming operator. For a rig at ZIndex `z`, `Count` pulls active layers in the inclusive range `z+1` through `z+Count`; it is a layer span, not an object count. Update it when moving layers. `Count: 0` captures no timeline rows and leaves the operator's nested children as its content. `Clear: true` discards earlier same-Element flow.

Default drawable alignment is centered. With only translation, `(0, 0)` centers a normal text/shape drawable; `(x, y)` offsets that center. `ScaleTransform` uses percentages: `100` is 1x. Use `measure_object_bounds` to resolve placement, especially with paths or compound transforms. The source-grounding skill describes runtime measurement details; a source checkout is optional.

## Output

`render_still` samples a frame. `render_storyboard` samples explicit times or derived Element midpoints; `subdivisionLevel` adds intermediate samples. `returnImageContent:true` includes a preview image for foreground calls. `export_video` creates playable output, with `background:true` and `read_render_job` available for long operations. Choose samples and playback checks that exercise the actual edit; still images do not show all motion behavior.

Project and output paths must be absolute; relative paths and bare filenames are rejected. Existing output files require `confirmOverwrite:true`. `measure_frame_differences` reports pixel changes and coverage without an aesthetic verdict. Report the requested edit, output location, and any behavior that remains unverified; do not substitute a tool success flag for reviewing the requested result.
