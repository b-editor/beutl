---
name: beutl-agent-source-grounding
description: Verify Beutl coordinates, units, bounds, and session behavior using runtime schemas and render probes; source inspection is optional.
---

# Beutl Runtime Grounding

Resolve a concrete editing uncertainty with `get_schema`, `read_document`, `measure_object_bounds`, and a small render probe. Production agents may have only the installed application and MCP tools; a source checkout is optional. Prefer the running application's contract over a checkout from another version.

## Multiple Beutl instances

To edit a running Beutl editor, call `list_instances` to identify the requested process by its PID, project and active scene. Pass its `instanceId` on every subsequent call, including scene discovery/open/create, document/schema queries, edits, history, rendering and render-job polling. There is no shared selected instance: through the installed `beutl-agent` server a call without `instanceId` works headlessly on project files, and on a direct connection to an editor it operates on that process. An `instance_unavailable` error requires rediscovery; never silently continue on another process or fall back to file editing. Call `list_scenes` on that instance and pass its `sceneId` on each scene read, edit, history, or render call. Scene calls preserve the visible editor selection; there is no attach step or shared selected scene. A missing scene ID is rejected instead of falling back to the UI selection. Catalog queries and job polling need no scene ID. Headless file sessions take neither `instanceId` nor `sceneId`.

## Coordinates and time

- Normal drawables default to `AlignmentX=Center`, `AlignmentY=Center`, and a centered transform origin. With a pure translation, the intended center `(cx, cy)` uses `X=cx-frameWidth/2`, `Y=cy-frameHeight/2`. `(0,0)` is centered. Set Left/Top alignment explicitly when using top-left placement.
- `ScaleTransform.Scale`, `ScaleX`, and `ScaleY` use percentages: `60` is 0.6x and `100` is 1x.
- `GeometryShape` retains its geometry bounds origin when drawing. Paths with top-left `(0,0)` align normally; paths centered on `(0,0)` carry negative bounds and shift. `measure_object_bounds` reports `geometryBoundsOrigin` and compensation. Normalize coordinates or compensate by `(-bounds.X, -bounds.Y)` before compound transforms.
- Closed Pen-only paths are valid when the path has nonzero bounds and its pen is visible. Inspect geometry, brush, thickness, and bounds if the render is empty.
- `measure_object_bounds` supports direct `Element.Objects`, not nested children. Measure the containing group, or use a separate temporary probe without changing the production hierarchy.
- `UseGlobalClock=false` uses Element-local KeyTime values; `true` uses the scene clock. Render sample times are relative to the scene's visible window. Active time ranges exclude their end.

## Runtime evidence

A schema validates serialized types and properties; it does not establish the visible result. Check relevant frames and, for temporal behavior, playable output. `render_still` reports dimensions, active elements, and pixel measurements. `measure_frame_differences` reports changes and coverage. Dark frames, small occupied areas, and low motion are measurements, not failures.

If results contradict these contracts, verify the active session and running application version before inventing a workaround. For development only, a matching checkout can help resolve an uncertainty in `Drawable.cs`, `TransformGroup.cs`, `Shape.cs`, `KeyFrameAnimation.cs`, or the toolkit renderer. Production editing continues through runtime evidence. Describe any remaining uncertainty in the conversation; no separate investigation document is required.
