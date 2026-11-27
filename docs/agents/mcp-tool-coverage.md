# Beutl editor features and MCP tools

The installed `beutl-agent` stdio server edits project files directly. Pass `instanceId` to forward a call to a running Beutl editor and `sceneId` to target a scene there. The stdio and in-app servers each expose `list_instances` for instance discovery.

Extensions can add live tools through `McpToolExtension`. The stdio server lists and forwards those tools when an editor that provides them is running. See the [MCP tool extension guide](../extension-authoring/mcp-tools.md).

## Projects and scenes

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Discover running editors and scenes | `list_instances`, `list_scenes` | Scene listing is live. `open_project` and `create_project` also return project and scene information. |
| Open, create, and save projects | `open_project`, `create_project`, `save_project`, `read_operation_status` | File-backed and live sessions. |
| Add a scene | `add_scene` | Adds a scene to the targeted project and returns its ID. |
| Read or edit scene settings | `read_document_summary`, `read_document`, `apply_edit` | Includes serialized scene settings such as frame size, start, and duration. |
| Remove a scene from its project | `remove_scene` | Takes the scene ID and keeps its file. Refuses the last scene or a scene with unsaved edits. Live removal persists immediately. With a file session, follow with `save_project`. |
| Undo, redo, and view history | `undo`, `redo`, `read_history` | Live calls use the editor's history. |
| Version control | `read_version_control_status`, `read_version_control_history`, `commit_project` | Reads local Git state and paginated project history. A commit saves pending edits and records one local snapshot for a project with Beutl tracking and a Git identity. |

Use the Version Control tab for restore and branch checkout because they require confirmation. The tab also handles Git setup, signed commits, and remote commands that may need credentials.

## Timeline

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Inspect timeline elements, layers, and markers | `read_document_summary`, `read_document` | Use the returned element IDs for subsequent edits. |
| Add, delete, move, or resize elements | `apply_edit` | Set the intended element membership, layer, and timing in the scene document. |
| Change element attributes or edit layers and groups | `apply_edit` | Covers serialized element and layer attributes and group membership. Agents can set locks. Unlocking requires the user. |
| Split one or more clips at a cut | `split_elements` | Returns the new right-half element IDs. |
| Slip source media | `slip_elements` | Applies a shared shift to the source windows without changing element geometry. |
| Ripple delete | `ripple_delete` | Removes clips and shifts following clips on each affected layer. |
| Ripple trim | `ripple_trim` | Moves the start or end of one clip with the editor's ripple constraints. |
| Close a gap after a clip | `close_gap` | Closes the first gap after the clip's contiguous run on its layer. |
| Find and close timeline gaps | `find_gaps`, `close_all_gaps` | Lists gaps by layer and closes gaps on all or selected layers, using the editor's ripple and lock rules. |
| Roll a cut or slide a clip | — | No operation tool for the editor's coupled trim and media-offset adjustments. |
| Duplicate timeline elements, copy, cut, or paste | `apply_edit`, `duplicate_object` | Partial. A patch can assemble new elements, and `duplicate_object` copies an engine object within an element. The timeline duplication and clipboard workflows have no tool. |
| Save or reuse an element template | `list_element_templates`, `save_element_template`, `add_element_from_template` | Lists templates in the Beutl profile. Save by name with element IDs, or omit the IDs to use the live selection and its groups. Add by template ID, time in seconds, and zero-based layer. A set keeps relative timing, layers, and groups. Insertion has one undo step. |
| Edit timeline markers | `apply_edit` | Edit the scene's marker list. |
| Analyze and split clips on silence | `split_on_silence` | Analyzes audio on the selected clips or layers and applies the Silence Split tab's split, delete, or ripple-delete mode. |

For split and ripple trim, `timeSeconds` is a position measured from timeline zero. For slip, `deltaSeconds` is a signed shift. Times are rounded to the scene's frame rate. Pass all intended clip IDs explicitly for split, slip, or ripple delete. A successful change has one undo step. An operation that cannot make a change can return `changed: false`.

An element batch that skips locked targets reports their IDs in `skippedLockedElementIds`. An element batch with only locked targets returns a validation error. Ripple delete can remove a recovery-protected element while retaining its original file, as in the timeline.

For `split_on_silence`, pass either `elementIds` or `layers` (timeline ZIndex values). The defaults are `thresholdDb: -40`, `minimumMilliseconds: 300`, `paddingMilliseconds: 100`, `mode: split_only`, and `includeLinked: true`. With linked clips enabled, the first eligible audio clip in each group supplies the cuts. The tool returns `createdElementIds` and reports `removedRanges` in the analyzed clips' original timeline coordinates. Use `delete_silence` to leave the gaps or `ripple_delete` to close them where the editor can.

`find_gaps` returns the layer and the start and end of each gap in timeline seconds, including gaps blocked by locks. Both gap tools accept an optional `layers` filter. `close_all_gaps` returns `closedGapCount` and leaves gaps open where locks prevent movement.

## Objects, animation, and effects

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Author or inspect text, shapes, transforms, audio, and effects | `get_schema`, `read_document`, `apply_edit`, `duplicate_object`, `measure_object_bounds` | Inspect registered types and modify serialized objects and properties. |
| Browse fonts, effects, recipes, and examples | `list_fonts`, `list_effects`, `list_effect_recipes`, `get_effect_recipe`, `list_examples`, `get_examples` | Provides authoring references for use in scene edits. |
| Edit clip transitions, keyframes, and easing | `get_schema`, `read_document`, `apply_edit` | Covers serialized transition and animation settings. |
| Edit a shader or node graph | `validate_shader`, `get_node_graph_guide`, `list_node_types`, `read_node_graph`, `edit_node_graph` | Covers shader validation, graph inspection, and graph operations. Add the graph's owning object or effect with `apply_edit`. |
| Generate a composition from a built-in recipe | `list_compositions`, `get_composition`, `plan_composition`, `render_composition_patch`, `apply_composition` | Covers the MCP composition catalog and preview workflow. |
| Authoring workflow and conventions | `get_started` | Summarizes the API workflow and links to installed agent skills. |

## Media, captions, and output

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Use existing media in a scene | `get_schema`, `apply_edit` | Add or edit the file source of a media object. |
| Scan and relink missing media | `scan_missing_media`, `relink_missing_media` | Lists missing media and fonts by scene. Relink a batch by absolute path pairs or search a folder using the editor's matching rules. |
| Organize project assets | — | No tool for scanning or reorganizing project assets. |
| Manage proxy media | `generate_proxies`, `read_proxy_status`, `remove_proxies` | Live editor tools. Queue generation for elements or video paths, poll jobs, and remove cached proxies. |
| Analyze audio rhythm | `analyze_audio_rhythm` | Reports estimated beats and onsets. |
| Generate or edit media with AI and follow jobs | `list_ai_models`, `generate_image`, `edit_image`, `generate_video`, `edit_video`, `list_ai_jobs`, `read_ai_job`, `cancel_ai_job` | Live editor tools. |
| Transcribe speech | `transcribe_audio`, `read_ai_job` | Live tool that produces a timed transcript. |
| Import, edit, or export a caption document | — | No tool for the editor's subtitle file and cue workflow, including subtitle export. |
| Preview frames or compare rendered frames | `render_still`, `render_storyboard`, `measure_frame_differences` | Covers frame output and measurement. |
| Control preview playback and playhead | — | No tool for interactive preview transport. |
| Export video and manage render jobs | `export_video`, `read_render_job`, `cancel_render_job` | File-backed and live export. |

Proxy tools use the running editor's queue and cache. Pass `sceneId` and, from the installed stdio server, `instanceId`. Pass `elementIds`, `mediaPaths`, or both to generate or remove proxies. Generation uses the configured preset unless you pass `half`, `quarter`, or `eighth`. Poll `read_proxy_status` with the returned `jobIds` for progress and recent outcomes. With no selectors, status lists the scene's video sources. Removing proxies cancels matching jobs and removes all presets for the selected sources unless you specify a preset.

`relink_missing_media` takes `replacements` (objects with `missingPath` and `newPath`) or `searchFolder`. Folder search follows the Missing Media tab's saved-fingerprint and unique-name matching rules. The tool validates replacements, saves the repaired scene, and reports `relinked` and `remainingMissing`. Repairs leave the edit history intact, as in the Missing Media tab. File-backed calls have the same save conflict check as `save_project`.

The tool registrations are in the [shared AgentToolkit server](../../src/Beutl.AgentToolkit/AgentToolkitMcpServer.cs) and the [in-app host](../../src/Beutl/AgentHost/AgentHostEndpoint.cs).
