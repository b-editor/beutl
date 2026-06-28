# Quickstart: Agent Editing Toolkit

How to build the toolkit, wire it into an MCP-capable agent host, and drive a project through the declarative loop. (This describes the *target* developer experience the plan delivers; the projects below are created by `/speckit-tasks` → `/speckit-implement`.)

## Build

```bash
dotnet build src/Beutl.AgentToolkit.Mcp/Beutl.AgentToolkit.Mcp.csproj   # or the whole solution
dotnet build Beutl.slnx
```

`Beutl.AgentToolkit` (core lib), `Beutl.AgentToolkit.Mcp` (stdio server), and the split-out non-UI `Beutl.Extensions.FFmpeg.Core` (headless export encoder) all target `net10.0`, are MIT, and are registered in `Beutl.slnx`. The packages `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, and `Microsoft.Extensions.Hosting` are pinned in `Directory.Packages.props`.

## Wire it into an agent host (`.mcp.json`)

### Install from the Beutl UI

Open **Settings → AI Agents** in the Beutl app. The page installs the bundled Skills, Subagents, and MCP configuration into a user-selected agent root.

- Pick an agent root. For Claude Code, select the home or workspace root and use the `Claude Code .claude folders` preset. For other MCP-capable agents, use `Generic skills/agents folders` or edit the relative Skills/Subagents folders directly.
- Choose whether to install Skills, Subagents, stdio MCP, live MCP, or any subset.
- Keep `.mcp.json` / `servers` for hosts that use the repository-local config shape, or change the config file and servers property name for hosts that use a different JSON layout such as `mcpServers`.
- Use the generated stdio command for headless project editing, or enable live MCP while Beutl is running to connect to the in-app loopback endpoint.

The installer preserves existing JSON properties and existing MCP servers, then updates only the `beutl-agent` and `beutl-live` entries.

### Manual `.mcp.json`

```jsonc
{
  "servers": {
    "beutl-agent": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "<abs>/src/Beutl.AgentToolkit.Mcp/Beutl.AgentToolkit.Mcp.csproj"],
      "env": { "BEUTL_WORKSPACE": "<abs>/my-video-workspace" }   // writes are confined here
    }
  }
}
```

In release, point `command` at the published exe. `BEUTL_WORKSPACE` is the write boundary: project saves and render/export outputs must resolve under it; reads (e.g. source media) may come from anywhere.

> **stdio rule**: the server logs to **STDERR only** — STDOUT is the JSON-RPC channel. (Handled by `LogToStandardErrorThreshold = LogLevel.Trace`.)

### In-app live mode (watch edits in the GUI)

To see edits appear live in the running Beutl editor, connect to the **in-app endpoint** instead of spawning the headless server. With a project open in the editor (which hosts a loopback HTTP/SSE endpoint), point the agent host at it:

```jsonc
{ "servers": { "beutl-live": { "type": "http", "url": "http://127.0.0.1:<port>/mcp?token=<token>" } } }
```

Then `attach_active_editor` binds a **live session** to the open project; edits flow through the same scene + history the UI is bound to, so the preview/timeline/property panels update in real time and each change is on the editor's undo stack. (The endpoint binds loopback only, issues a per-session token, and accepts the token either as `?token=` or `X-Beutl-Agent-Token`; the write-boundary and validation guarantees are unchanged.)

### Raw HTTP/SSE smoke test

Most MCP hosts hide the JSON-RPC/SSE details. For a raw client such as `curl`, request `text/event-stream` and read the `data:` payload:

```bash
curl -sS -X POST \
  -H 'Accept: application/json, text/event-stream' \
  -H 'Content-Type: application/json' \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"probe","version":"1"}}}' \
  'http://127.0.0.1:<port>/mcp?token=<token>'
```

After `initialize`, send `notifications/initialized`, then `tools/list`, then `get_started` or `attach_active_editor`. `notifications/initialized` may have no response body; that is normal. Tool results are nested as JSON text under `result.content[0].text` in raw HTTP clients, so decode that text payload after reading the SSE `data:` line. For progress checks, prefer `read_document_summary` over `read_document` until you need the full JSON.

## The declarative loop (worked example)

A creator asks the agent: *"10-second 1080p clip: a title that fades in over a background image for the first 3 s, then a logo bottom-right."*

1. **Discover** what's editable:
   ```
   get_schema { "category": "Drawable" }      → text/image/shape types, their params, ranges, defaults, animatable flags
   ```
2. **Create** the project:
   ```
   create_project { "path": "promo.bep", "width": 1920, "height": 1080, "frameRate": 30, "duration": "00:00:10" }  → { session }
   ```
3. **Read** the empty document, then materialize a Remotion-style composition:
   ```
   read_document {}                           → { document, schemaVersion }   // echo schemaVersion back on every edit
   list_compositions { "tag": "empty-scene", "seed": "promo-a" }
                                               → shuffled compact templates + reusable seed
   get_composition { "name": "<first suitable composition name>" }
                                               → defaultProps, prop descriptors, calculated metadata, sequences, transitions
   plan_composition {
     "name": "<selected composition name>",
     "seed": "promo-a",
     "inputProps": { "title": "BEUTL MOTION", "subtitle": "SUMMER LAUNCH", "durationSeconds": 10 }
   }
                                               → metadata + sequences + validation + valid + expectedChangeSet
   get_schema { "category": "visualEffect", "includeProperties": false, "includeExamples": false }
                                               → compact effect type/discriminator catalog; category aliases are accepted
   ```
   To keep repeated raw-agent runs visually varied, `list_compositions` shuffles templates by seed and `plan_composition` / `apply_composition` use deterministic seeded random/noise internally. Reuse the returned seed for reproducibility; change it for a new layout, palette, and motion offsets. Use `render_composition_patch` only when the client explicitly needs the generated JSON patch. For smaller targeted snippets, `list_examples` / `get_examples` still provide compact declarative patches.
4. **Apply** atomically once the plan looks right:
   ```
   apply_composition {
     "name": "<selected composition name>",
     "seed": "promo-a",
     "inputProps": { "title": "BEUTL MOTION", "subtitle": "SUMMER LAUNCH", "durationSeconds": 10 },
     "expectedChangeSet": <plan_composition.plan.expectedChangeSet>
   }
                                              → { composition, result: { plan, document } }   // document includes minted Ids
   ```
   Use `apply_edit.document` (or call `read_document`) before follow-up edits so later patches reference existing `Id` values. A patch that supplies an unknown `Id` is rejected as `stale_handle`; omit `Id` to create a new node.

   For a later tweak — *"make the title bigger"* — send a tiny **merge-patch** instead of the whole document:
   ```
   plan_edit { "schemaVersion": "1", "patch": { "Elements": [ { "Id": "<title-el>", "Objects": [ { "Id": "<text>", "Size": 140 } ] } ] } }
   apply_edit { "schemaVersion": "1", "patch": <same> }
   ```
   Brushes and effects are ordinary declarative properties. Discover them with `get_schema { "category": "Brush" }` and `get_schema { "category": "FilterEffect" }`. For example, a shape or text object can receive a gradient fill and a filter chain in the same patch:
   ```
   plan_edit {
     "schemaVersion": "1",
     "patch": {
       "Elements": [
         {
           "Id": "<element-id>",
           "Objects": [
             {
               "Id": "<drawable-id>",
               "Fill": { "$type": "[Beutl.Engine]Beutl.Media:LinearGradientBrush", "GradientStops": [ { "$type": "[Beutl.Engine]Beutl.Media:GradientStop", "Offset": 0, "Color": "#FF1AD8FF" }, { "$type": "[Beutl.Engine]Beutl.Media:GradientStop", "Offset": 1, "Color": "#FFFF45B5" } ] },
               "FilterEffect": { "$type": "[Beutl.Engine]Beutl.Graphics.Effects:FilterEffectGroup", "Children": [ { "$type": "[Beutl.Engine]Beutl.Graphics.Effects:Blur", "Sigma": "8,8" } ] }
             }
           ]
         }
       ]
     }
   }
   ```
   In live mode, call `read_document_summary {}` between staged patches to check element count, object types, and which objects already have animations/effects without pulling the full document.
5. **Verify** by rendering a still (and optionally export):
   ```
   render_still { "timeSeconds": 1.5, "outputPath": "preview.png" }  → imagePath
   export_video { "outputPath": "promo.mp4" }                        → videoPath   (needs FFmpeg native libs)
   ```
   Bare output filenames are written under `agent-output/`; pass an explicit relative directory when a different workspace location is intentional.
6. **Save**:
   ```
   save_project { session }                   → savedPath (under BEUTL_WORKSPACE)
   ```

Undo is same-session: file sessions record normal history while open, and live editor sessions put edits on the active editor's undo stack.

## What an agent can rely on

- **Declarative-first**: express the whole desired scene at once, or a small merge-patch; the toolkit computes the minimal undoable change set.
- **Safe by construction**: out-of-range values are reported (coerced/rejected), not silently applied; a failed multi-step edit rolls back wholly; writes can't escape the workspace.
- **Plan == apply**: `plan_edit` predicts `apply_edit` exactly (pass `plan_edit.expectedChangeSet` to enforce).

## Tests

- **GPU-free unit tests** (`tests/Beutl.AgentToolkit.Tests`): schema generation; merge-patch (null-delete, nested object; **id-keyed array merge: `$delete` (+ `$delete` on a missing `Id` ⇒ no-op), omitted-`Id` insert, unknown-`Id` ⇒ `stale_handle`, same-`Id`/different-`$type` ⇒ `validation_rejected`, ordering `$index`/`$after`/`$before` ⇒ `move-child`, multiple directives ⇒ `validation_rejected`, bad sibling ⇒ `stale_handle`, keyframes by time**; scalar/non-id array wholesale-replace); reconciliation→operations (property set, keyframe add/remove keeping time-sort, collection insert/remove/move by Id) **plus a mid-reconcile failure that proves `ExecuteInTransaction` rolls live mutations back**; the workspace guard (in-root ok, `..` escape rejected, in-root symlink-to-outside rejected — symlink fixtures self-skip on Windows without Developer Mode); and capability discovery completeness.
- **GPU-gated render/export tests**: self-skip via `VulkanTestEnvironment`/`GpuTestEnvironment` (`Assert.Ignore` when no Vulkan/MoltenVK). Export orchestration is testable worker-free over a fake `System.IO.Pipes` host (as `tests/Beutl.FFmpegIpc.Tests`).

Run:
```bash
dotnet test tests/Beutl.AgentToolkit.Tests --settings coverlet.runsettings
```

## Live-Mode Manual Verification (SC-010)

1. Open a project in the Beutl editor.
2. Connect an MCP host to `http://127.0.0.1:<port>/mcp?token=<token>`.
3. Call `attach_active_editor`.
4. Call `read_document`, `plan_edit`, and `apply_edit` for one visible text/shape change.
5. Confirm the preview, timeline, and property panel update without reloading the project.
6. Confirm the edit is one normal undo entry in the active editor session.

## The guidance pillar (Skills / Subagents)

Beyond the MCP surface, the toolkit ships discoverable editing recipes (Skills) and scoped specialists (Subagents) so agents follow Beutl's conventions without re-deriving them:

- `.claude/skills/beutl-agent-timeline-from-shotlist/SKILL.md`
- `.claude/skills/beutl-agent-look-effect-chain/SKILL.md`
- `.claude/agents/beutl-agent-timeline-builder.md`
- `.claude/agents/beutl-agent-look-applier.md`

Use the timeline recipe/specialist for shot-list layout, retiming, splitting, grouping, and media placement. Use the look/effect recipe/specialist for color/effect chains, effect ordering, and cross-shot consistency. Both document PascalCase property keys, id-keyed array merge-patch rules, and in-range schema-driven values.
