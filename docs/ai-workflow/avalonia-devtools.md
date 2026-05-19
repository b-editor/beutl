# Avalonia DevTools MCP

Beutl wires an Avalonia DevTools MCP server in `.mcp.json` so any AI agent (Claude Code, Codex, Cursor, etc.) can attach to a running Beutl instance and inspect the visual tree, styles, bindings, and dependency-property values without screenshots.

## Prerequisite: restore the dotnet tool

`.mcp.json` invokes the MCP server via `dotnet tool run avalonia-mcp`. The tool is pinned in `.config/dotnet-tools.json` as [`AvaloniaMcp`](https://www.nuget.org/packages/AvaloniaMcp) (MIT, [`adirh3/AvaloniaMcp`](https://github.com/adirh3/AvaloniaMcp)). It is **not** restored automatically by `dotnet build`; on a fresh checkout run:

```bash
dotnet tool restore
```

Once the tool is restored, the MCP server starts on demand whenever an AI agent issues an `mcp__avalonia_devtools__*` call. If the tool was never restored you will see `Could not execute because the specified command or file was not found` — re-run `dotnet tool restore` and the MCP entry recovers without restarting the agent.

## What it gives you

- `tree` / `search` — locate visuals by name, type, or attached property
- `props` / `set-prop` — read / write properties on a visual at runtime (great for narrowing down DataContext mismatches)
- `styles` / `pseudo-class` — inspect or toggle pseudo-classes (`:pointerover`, `:selected`, etc.)
- `screenshot` — capture a specific node or the whole window
- `attach-to-file` — design-time preview of a single `.axaml`

The full list lives in `.mcp.json`'s server description; tools surface under names like `mcp__avalonia_devtools__*`.

## Two attach modes

| Mode | When to use | Requires |
|---|---|---|
| `attach-to-app` | Beutl is already running and you want to debug live behaviour | Beutl built with the dev-tools package referenced and `AttachDeveloperTools()` called at startup |
| `attach-to-file` | You are iterating on one `.axaml` in isolation and want a headless preview | Nothing app-side; the MCP server hosts the file |

Prefer `attach-to-app` for any state-dependent issue (data binding, command wiring, focus, conditional styles). Prefer `attach-to-file` for pure layout work.

## Enabling app-side support

Beutl already references `Avalonia.Diagnostics` (Debug only) for the F12 overlay. The MCP additionally needs the `AvaloniaUI.DiagnosticsSupport` package (2.2.1+) and an explicit `AttachDeveloperTools()` call.

This package is **not** referenced by default — it adds startup cost we do not want in normal Debug runs. Enable it only when you intend to drive Beutl from the MCP:

1. In `src/Beutl/Beutl.csproj`, add (Debug-only):
   ```xml
   <PackageReference Include="AvaloniaUI.DiagnosticsSupport"
                     Condition="'$(Configuration)' == 'Debug'" />
   ```
   Pin the version in `Directory.Packages.props`.
2. In `src/Beutl/App.axaml.cs`, inside `OnFrameworkInitializationCompleted` (or equivalent), gate the attach behind `#if DEBUG`:
   ```csharp
   #if DEBUG
       this.AttachDeveloperTools();
   #endif
   ```
3. Rebuild and run Beutl. The MCP server will discover the process via the standard Avalonia DevTools protocol.

Do **not** commit step 1 + 2 unless the team agrees to take the extra startup cost as a permanent dependency in Debug builds.

## Caveats

- Tool calls fail silently if no Avalonia process is reachable. The MCP server cannot ask the user to press F12 — that only opens the in-window overlay and has no effect on the MCP attach channel.
- Node IDs returned by `tree` / `search` are invalidated whenever the visual tree changes meaningfully (window navigation, panel reload, hot-reload). Re-query when in doubt.
- The MCP can read network / process state of the attached Avalonia app. Treat it like any other developer-tools surface: do not point it at someone else's machine.
