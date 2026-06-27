# Quickstart: Agent Editing Toolkit

How to build the toolkit, wire it into an MCP-capable agent host, and drive a project through the declarative loop. (This describes the *target* developer experience the plan delivers; the projects below are created by `/speckit-tasks` → `/speckit-implement`.)

## Build

```bash
dotnet build src/Beutl.AgentToolkit.Mcp/Beutl.AgentToolkit.Mcp.csproj   # or the whole solution
dotnet build Beutl.slnx
```

Both `Beutl.AgentToolkit` (core lib) and `Beutl.AgentToolkit.Mcp` (stdio server) target `net10.0`, are MIT, and are registered in `Beutl.slnx`. The packages `ModelContextProtocol` and `Microsoft.Extensions.Hosting` are pinned in `Directory.Packages.props`.

## Wire it into an agent host (`.mcp.json`)

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
{ "servers": { "beutl-live": { "type": "http", "url": "http://127.0.0.1:<port>/mcp" } } }
```

Then `attach_active_editor` binds a **live session** to the open project; edits flow through the same scene + history the UI is bound to, so the preview/timeline/property panels update in real time and each change is on the editor's undo stack. (The endpoint binds loopback only; the write-boundary and validation guarantees are unchanged.)

## The declarative loop (worked example)

A creator asks the agent: *"10-second 1080p clip: a title that fades in over a background image for the first 3 s, then a logo bottom-right."*

1. **Discover** what's editable:
   ```
   get_schema { "category": "Drawable" }      → text/image/shape types, their params, ranges, defaults, animatable flags
   ```
2. **Create** the project:
   ```
   create_project { "path": "promo.bep", "frameSize": [1920,1080], "frameRate": 30, "duration": "00:00:10" }  → { session }
   ```
3. **Read** the (empty) document, then **plan** the full desired state:
   ```
   read_document { session }                  → current document
   plan_edit { session, "desired": <document with the image, title (with an Opacity fade-in keyframe animation), and logo elements> }
                                              → changeSet + validation (e.g. any value clamped to its [Range])
   ```
4. **Apply** atomically once the plan looks right:
   ```
   apply_edit { session, "desired": <same>, "expectedChangeSet": <from plan> }  → applied, historyEntry
   ```
   For a later tweak — *"make the title bigger"* — send a tiny **merge-patch** instead of the whole document:
   ```
   plan_edit { session, "patch": { "Children": [ { "Id": "<title-el>", "Objects": [ { "Id": "<text>", "Size": 140 } ] } ] } }
   apply_edit { session, "patch": <same> }
   ```
5. **Verify** by rendering a still (and optionally export):
   ```
   render_still { session, sceneId, "time": "00:00:01.5", "outputPath": "preview.png" }   → imagePath
   export_video { session, sceneId, "outputPath": "promo.mp4" }                            → videoPath   (needs FFmpeg native libs)
   ```
6. **Save**:
   ```
   save_project { session }                   → savedPath (under BEUTL_WORKSPACE)
   ```

Undo is available (`undo`/`redo`) and an agent's edits show up as normal, human-undoable history entries when the project is later opened in the Beutl GUI.

## What an agent can rely on

- **Declarative-first**: express the whole desired scene at once, or a small merge-patch; the toolkit computes the minimal undoable change set.
- **Safe by construction**: out-of-range values are reported (coerced/rejected), not silently applied; a failed multi-step edit rolls back wholly; writes can't escape the workspace.
- **Plan == apply**: `plan_edit` predicts `apply_edit` exactly (pass `expectedChangeSet` to enforce).

## Tests

- **GPU-free unit tests** (`tests/Beutl.AgentToolkit.Tests`): schema generation, RFC 7396 merge-patch (null-delete, nested, array-replace), reconciliation→operations (property set, keyframe add/remove keeping time-sort, collection insert/remove/move by Id), the workspace guard (in-root ok, `..` escape rejected, in-root symlink-to-outside rejected — symlink fixtures self-skip on Windows without Developer Mode), and capability discovery completeness.
- **GPU-gated render/export tests**: self-skip via `VulkanTestEnvironment`/`GpuTestEnvironment` (`Assert.Ignore` when no Vulkan/MoltenVK). Export orchestration is testable worker-free over a fake `System.IO.Pipes` host (as `tests/Beutl.FFmpegIpc.Tests`).

Run:
```bash
dotnet test tests/Beutl.AgentToolkit.Tests --settings coverlet.runsettings
```

## The guidance pillar (Skills / Subagents)

Beyond the MCP surface, the toolkit ships discoverable editing recipes (Skills) and scoped specialists (Subagents) — e.g. "lay out a timeline from a shot list", "apply a look/effect chain" — so agents follow Beutl's conventions (PascalCase property keys, merge-patch array-replace caveat, in-range values) without re-deriving them. These are authored under `.claude/skills/*` and `.claude/agents/*` during implementation.
