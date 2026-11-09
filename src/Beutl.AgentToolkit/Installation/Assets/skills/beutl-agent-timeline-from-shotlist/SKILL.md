---
name: beutl-agent-timeline-from-shotlist
description: Make a video in Beutl from a brief or a shot list, even a one-line request such as a teaser, trailer, promo, or title sequence, and edit timeline elements, timing, transforms, groups, and keyframes through the Agent Editing Toolkit MCP tools. Read this before the first edit.
---

# Beutl Timeline Editing

Beutl provides editing operations and rendered output. The agent decides the visual design and evaluates the result against the user's request. Operation success and numerical measurements do not establish visual quality or completion.

## Reviewing renders

Look at frames at full size as well as on the contact sheet, including frames in the middle of transitions, and say what is wrong before what works: text that collides or clips, plates and boxes nobody designed, a damaged logo. Fix what you find and render again.

## Session and document mechanics

- The `beutl-agent` server edits either a running Beutl editor or project files. To edit a running editor, use `list_instances` and `list_scenes`, then pass `instanceId` and `sceneId` on each scene read, edit, history, or render call. Calls preserve the visible editor selection; no attach or previous call selects a target. To work headlessly on files, omit `instanceId` and use `create_project`/`open_project` for a file-backed session. `read_operation_status` reports persistence behavior. File sessions use `save_project`; live edits use the editor's normal save behavior.
- `read_document_summary` returns compact handles. `read_document` returns serialized data. Query `get_schema(type=...)` for unfamiliar types or properties; enumerating catalogs is optional. Examples and presets are syntax aids and reusable edits, not the range of possible expressions.
- To survey several types, pass `get_schema` a comma-separated `type` with `includeUsageHints:false`. Browse fonts with `list_fonts(includeTypefaces:false)`, then pass `nameFilter` for the families you choose. These describe the running build, so use them before reading Beutl's source, and keep the source for behavior they do not cover.
- `apply_edit` accepts `schemaVersion: "1"`. An Id-based merge patch preserves unmentioned siblings; a full desired document is authoritative. Use PascalCase property names and the `$type` discriminators returned by the runtime. Read `validation` and `createdIds` to resolve technical errors and target newly created objects.
- New timeline entries use `$type: "[Beutl.ProjectSystem]:Element"` with drawables under `Objects`. Read `get_examples(name: "insert-new-element-skeleton")` when the container shape is unfamiliar.
- Element `Start` and `Length` define its time range; the end is exclusive. Tool sample times are relative to the scene's visible window. Keyframe `UseGlobalClock=false` means Element-local `KeyTime`; `true` uses the scene clock. Do not add the Element's Start to local keyframes.
- Give every object its own Element, even when there are over a hundred, so a person can select and retime it on the timeline. An Element holds one object, plus the `PortalObject` and flow operator that act on it. When a group or other flow operator takes several inputs, put them on separate rows with a `PortalObject` instead of listing them in its Element.
- Name elements and objects after their visible content or purpose in the user's language. Use compact `ZIndex` values and reuse rows across non-overlapping shots. Preserve overlapping draw order.

## Groups and transforms

`Element.Objects` is a flow chain evaluated in array order. `IFlowOperator` types such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, `DrawableObject3D`, and `Scene3D` consume compatible upstream objects. When authoring their input, prefer flow over adding objects directly to `Children` or similar content properties (`Scene3D.Objects`/`Lights`). Choose by the number of inputs:

- **Several inputs, on separate timeline layers:** put a `PortalObject` immediately before the operator in its `Element.Objects`. For an operator Element at `ZIndex = z`, place the source Elements on the rows below it, within `z+1` through `z+Count` inclusive. `Count` is a layer span, not an object count. Each source keeps its own `Start`/`Length`. Update `Count` when moving layers and keep the operator active for the period that needs grouping; sources render ungrouped outside that period.
- **One input, in the same Element:** add the source earlier in `Element.Objects`, in the order `[source, PortalObject(Count=0, Clear=false), operator]`. Keep the portal immediately before the operator and leave its nested content empty. The source shares the Element's timing. `Count=0` captures no timeline rows but retains earlier same-Element flow; `Clear=true` would discard those inputs.

Groups consume all compatible upstream inputs, including an earlier background. When grouping only a subset, give its input/operator chain its own Element and keep unrelated content in separate Elements outside the portal span, preserving timing and draw order. `DrawableTimeController` consumes only the first upstream drawable; to retime several drawables together, group them first, then place another `PortalObject(Count=0, Clear=false)` and the controller after the group.

Use `get_examples` with `insert-camera-rig-portal` or `insert-camera-rig-push-in` for these two arrangements. Existing nested content remains editable; do not migrate it just to follow the authoring preference. Ordinary collections such as `TransformGroup.Children` and `FilterEffectGroup.Children` still use direct children because they are not flow operators.

Start with `AlignmentX=Center` and `AlignmentY=Center`, then choose each axis independently to suit the object, layout, and intended motion: `Left`/`Center`/`Right` on X and `Top`/`Center`/`Bottom` on Y. Preserve existing intentional alignment. Use `TranslateTransform.X/Y` to offset the chosen alignment, and check for unintended anchor shifts when animating alignment or drawable size. With both axes centered and only translation, `(0, 0)` centers a normal text/shape drawable; an intended center `(cx, cy)` uses `X=cx-frameWidth/2`, `Y=cy-frameHeight/2`. `ScaleTransform` uses percentages: `100` is 1x. Use `measure_object_bounds` to resolve placement, especially with paths or compound transforms. The source-grounding skill describes runtime measurement details; a source checkout is optional.

## Output

`render_still` samples a frame. `render_storyboard` samples explicit times or derived Element midpoints; `subdivisionLevel` adds intermediate samples. `returnImageContent:true` includes a preview image for foreground calls. `export_video` creates playable output, with `background:true` and `read_render_job` available for long operations. Choose samples and playback checks that exercise the actual edit; still images do not show all motion behavior.

File paths passed to tools must be absolute; relative paths and bare filenames are rejected. Existing output files require `confirmOverwrite:true`. `measure_frame_differences` reports pixel changes and coverage without an aesthetic verdict. Report the requested edit, output location, and any behavior that remains unverified; do not substitute a tool success flag for reviewing the requested result.
