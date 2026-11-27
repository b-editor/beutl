# Beutl editor features and MCP tools

The installed `beutl-agent` stdio server edits project files directly. Pass `instanceId` to forward a call to a running Beutl editor and `sceneId` to target a scene there. The stdio and in-app servers each expose `list_instances` for instance discovery.

Extensions can add live tools through `McpToolExtension`. The stdio server lists and forwards those tools when an editor that provides them is running. See the [MCP tool extension guide](../extension-authoring/mcp-tools.md).

## Extensions

| Tool | Usage |
| --- | --- |
| `search_extensions` | Search the Beutl Store with `query`. Use `offset` and `limit` for pagination. |
| `install_extension` | Pass a Store `packageId` with an optional exact `version`, an absolute `path` to a local `.nupkg`, or an HTTPS `url` serving the archive. The Store defaults to the latest compatible release. |
| `list_installed_extensions` | Reports versions installed in the profile, versions loaded by the target editor, and pending extension changes. |

Use `instanceId` from `list_instances` when calling from stdio. These tools work before a project is open. Installation returns `installed` when the extension is loaded, `already_installed` for the same version, or `restart_required` when restricted mode defers loading until the next normal start. A different installed version returns `extension_version_conflict`, and a pending install or uninstall for the same package returns `extension_change_pending`. Store ownership and required license acceptance use Beutl's existing records.

After an immediate install, refresh `tools/list` to discover any tools the extension provides.

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
| Ripple trim | `ripple_trim` | Moves a clip edge with the editor's ripple constraints. Can include group members with the same edge. |
| Close a gap after a clip | `close_gap` | Closes the first gap after the clip's contiguous run on its layer. |
| Find and close timeline gaps | `find_gaps`, `close_all_gaps` | Lists gaps by layer and closes gaps on all or selected layers, using the editor's ripple and lock rules. |
| Roll a cut or slide clips | `roll_elements`, `slide_elements` | Moves grouped cuts or clip blocks with the editor's adjacent trims and media-offset adjustments. |
| Duplicate timeline elements with their groups | `duplicate_elements` | Copies clips and their groups at a nearby free position and returns the new element IDs. |
| Copy, cut, or paste timeline elements | — | No tool for the editor's timeline clipboard workflow. |
| Save or reuse an element template | `list_element_templates`, `save_element_template`, `add_element_from_template` | Lists templates in the Beutl profile. Save by name with element IDs, or omit the IDs to use the live selection and its groups. Add by template ID, time in seconds, and zero-based layer. A set keeps relative timing, layers, and groups. Insertion has one undo step. |
| Edit timeline markers | `apply_edit` | Edit the scene's marker list. |
| Analyze and split clips on silence | `split_on_silence` | Analyzes audio on the selected clips or layers and applies the Silence Split tab's split, delete, or ripple-delete mode. |

For split, duplication, and ripple trim, `timeSeconds` is a position measured from timeline zero. For slip, roll, and slide, `deltaSeconds` is a signed shift. Times are rounded to the scene's frame rate. Pass all intended clip IDs explicitly for split, slip, or ripple delete. Each edit that changes the scene has one undo step. An operation that cannot make a change can return `changed: false`.

An element batch that skips locked targets reports their IDs in `skippedLockedElementIds`. An element batch with only locked targets returns a validation error. Ripple delete can remove a recovery-protected element while retaining its original file, as in the timeline.

`duplicate_elements` takes `elementIds`, `timeSeconds`, and `layer` (the destination ZIndex for the copy on the lowest-indexed layer). The editor includes group members, skips locked sources, and searches for a free position on unlocked layers. `createdElementIds` identifies the copies.

`ripple_trim` takes `elementId`, `edge` (`start` or `end`), and `timeSeconds`. Pass `includeLinked: true` to also trim unlocked group members that share that start or end. `roll_elements` takes `elementId`, `edge`, and `deltaSeconds` and includes the group's cuts at the same boundary. `slide_elements` takes `elementIds` and `deltaSeconds`. It slides the selected clips and their group members, which must form one contiguous block per affected layer with a neighboring clip on each side. A locked participant rejects a roll or slide. `changedElementIds` includes the neighbors changed by these edits.

For `split_on_silence`, pass either `elementIds` or `layers` (timeline ZIndex values). The defaults are `thresholdDb: -40`, `minimumMilliseconds: 300`, `paddingMilliseconds: 100`, `mode: split_only`, and `includeLinked: true`. With linked clips enabled, the first eligible audio clip in each group supplies the cuts. The tool returns `createdElementIds` and reports `removedRanges` in the analyzed clips' original timeline coordinates. Use `delete_silence` to leave the gaps or `ripple_delete` to close them where the editor can.

`find_gaps` returns the layer and the start and end of each gap in timeline seconds, including gaps blocked by locks. Both gap tools accept an optional `layers` filter. `close_all_gaps` returns `closedGapCount` and leaves gaps open where locks prevent movement.

## Objects, animation, and effects

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Author or inspect text, shapes, transforms, audio, and effects | `get_schema`, `read_document`, `apply_edit`, `duplicate_object`, `measure_object_bounds` | Inspect registered types and modify serialized objects and properties. |
| Save or reuse an object template | `list_templates`, `save_object_template`, `apply_object_template` | Browse templates in the Beutl profile, save an object by its document ID, and replace an object property or insert a list item in one undo step. |
| Browse fonts, effects, recipes, and examples | `list_fonts`, `list_effects`, `list_effect_recipes`, `get_effect_recipe`, `list_examples`, `get_examples` | Provides authoring references for use in scene edits. |
| Edit clip transitions, keyframes, and easing | `get_schema`, `read_document`, `apply_edit` | Covers serialized transition and animation settings. |
| Edit a shader or node graph | `validate_shader`, `get_node_graph_guide`, `list_node_types`, `read_node_graph`, `edit_node_graph` | Covers shader validation, graph inspection, and graph operations. Add the graph's owning object or effect with `apply_edit`. |
| Generate a composition from a built-in recipe | `list_compositions`, `get_composition`, `plan_composition`, `render_composition_patch`, `apply_composition` | Covers the MCP composition catalog and preview workflow. |
| Authoring workflow and conventions | `get_started` | Summarizes the API workflow and links to installed agent skills. |

`list_templates` returns each template's `templateId`, `name`, `baseType`, and concrete `type`. Filter by the
returned `baseType` or a base type name such as `Brush` or `FilterEffect`. With `save_object_template`, pass
the `objectId` from `read_document` and a `name` to save a template to the profile.

`apply_object_template` takes a `templateId`, the `objectId` of the property owner, and the `propertyName`
as written in `read_document`. Omit `index` to replace an object value, such as `Fill` on a shape. Pass a
zero-based `index` to insert into an object list, such as `Children` on a filter effect group. Use the
list length to append. For a flow operator in `Element.Objects`, the tool inserts its paired portal at
`index` and the operator immediately after it. The template's concrete type must fit the property or list
item type. The tool returns the new object's `objectId` and uses the same locks as `apply_edit`.
Pass `sceneId` for live saves and applications. For a file-backed session, use `save_project` to persist the scene edit.

## Media, captions, and output

| Editor feature | Built-in MCP tools | Coverage |
| --- | --- | --- |
| Use existing media in a scene | `get_schema`, `apply_edit` | Add or edit the file source of a media object. |
| Scan and relink missing media | `scan_missing_media`, `relink_missing_media` | Lists missing media and fonts by scene. Relink a batch by absolute path pairs or search a folder using the editor's matching rules. |
| Organize project assets | `inspect_project_assets`, `organize_project_assets` | Lists the Project Assets view's external, unused, and quarantined files and the referenced assets inside the project. Collects external assets, moves unused files aside, restores them, and permanently deletes quarantined files. File-backed and live. |
| Manage proxy media | `generate_proxies`, `read_proxy_status`, `remove_proxies` | Live editor tools. Queue generation for elements or video paths, poll jobs, and remove cached proxies. |
| Analyze audio rhythm | `analyze_audio_rhythm` | Reports estimated beats and onsets. |
| Generate or edit media with AI and follow jobs | `list_ai_models`, `generate_image`, `edit_image`, `generate_video`, `edit_video`, `list_ai_jobs`, `read_ai_job`, `cancel_ai_job` | Live editor tools. |
| Transcribe speech | `transcribe_audio`, `read_ai_job` | Live tool that produces a timed transcript. |
| Import, inspect, edit, or export subtitles | `import_subtitles`, `list_subtitles`, `apply_edit`, `export_subtitles` | Imports SRT, ASS, and WebVTT as editable text clips. Lists cues with timing and element IDs. Exports clips or layers to a subtitle file. |
| Preview frames or compare rendered frames | `render_still`, `render_storyboard`, `measure_frame_differences` | Covers frame output and measurement. |
| Control preview playback and playhead | — | No tool for interactive preview transport. |
| Export video and manage render jobs | `export_video`, `read_render_job`, `cancel_render_job` | File-backed and live export. |

Proxy tools use the running editor's queue and cache. Pass `sceneId` and, from the installed stdio server, `instanceId`. Pass `elementIds`, `mediaPaths`, or both to generate or remove proxies. Generation uses the configured preset unless you pass `half`, `quarter`, or `eighth`. Poll `read_proxy_status` with the returned `jobIds` for progress and recent outcomes. With no selectors, status lists the scene's video sources. Removing proxies cancels matching jobs and removes all presets for the selected sources unless you specify a preset.

`relink_missing_media` takes `replacements` (objects with `missingPath` and `newPath`) or `searchFolder`. Folder search follows the Missing Media tab's saved-fingerprint and unique-name matching rules. The tool validates replacements, saves the repaired scene, and reports `relinked` and `remainingMissing`. Repairs leave the edit history intact, as in the Missing Media tab. File-backed calls have the same save conflict check as `save_project`.

`inspect_project_assets` reports absolute paths, file kinds, sizes in bytes, element IDs, and issues for the whole project. Pass eligible row paths to `organize_project_assets` with `action` set to `collect`, `quarantine`, `restore`, or `delete`. Use the row's main path for a bundle. Collection copies files into `resources/` and saves all affected references across the project. Quarantine and restore move files and preserve project documents. Permanent deletion takes one quarantined path per call and requires `confirmDelete: true`. The response lists the affected files and their destinations.

Both asset tools use the open project in a live editor. Pass `sceneId` to identify that project by one of its scenes without opening the scene tab. From stdio, pass `instanceId` to target a running editor or use `open_project` or `create_project` to select the active file session. In a file session, call `save_project` after adding or removing a scene before collecting assets.

`import_subtitles` takes an absolute `path` ending in `.srt`, `.ass`, `.ssa`, or `.vtt`. It keeps the file's cue times and places the clips above the scene on unlocked layers, separating overlapping cues. Pass a saved text object, element, or element-set `templateId` from `list_templates` or `list_element_templates` to use it in place of Beutl's default subtitle styling. The import returns the created element IDs, the number of cues imported, and whether any cues were skipped. One undo removes the batch. Use `save_project` to persist a file-session import.

`list_subtitles` returns each text clip's `elementId`, `layer`, `startSeconds`, `endSeconds`, and extracted `text`. Pass `layers` to filter by timeline ZIndex. Use `read_document` with the clip ID as `rootId` to identify its text object. Use `apply_edit` to change `Text`, `Start`, or `Length`, or use the timeline tools for timing edits. These edits honor clip and layer locks.

`export_subtitles` takes a nonempty `elementIds` or `layers` list and an absolute `outputPath` ending in `.srt`, `.ass`, or `.vtt`. The editor's subtitle exporter extracts the text, sorts the cues, and counts clips without readable text in `skippedClipCount`. Replacing an existing file requires `confirmOverwrite: true`. Pass `sceneId` to target a live scene for any subtitle tool and `instanceId` to reach it from stdio.

The tool registrations are in the [shared AgentToolkit server](../../src/Beutl.AgentToolkit/AgentToolkitMcpServer.cs) and the [in-app host](../../src/Beutl/AgentHost/AgentHostEndpoint.cs).
