# Project structure and module boundaries

| Project | Responsibility |
|---|---|
| `Beutl.Engine` | Core rendering, scene, animation, audio, composition, and track abstractions. It must not depend on UI projects. |
| `Beutl.Engine.SourceGenerators` | Roslyn source generators consumed by the engine and downstream projects. |
| `Beutl.ProjectSystem` | Project and document persistence. |
| `Beutl.Editor` | Non-UI editor logic, including undo/redo, packaging, and editing-pipeline services. |
| `Beutl.Editor.Components`, `Beutl.Controls`, `Beutl` | Avalonia views, controls, view models, and the application shell. |
| `Beutl.Extensibility` | Plugin-facing abstractions. |
| `Beutl.NodeGraph` | Node editor model and evaluation. |
| `Beutl.FFmpegIpc` | MIT IPC transport and adapters used to communicate with the FFmpeg worker. |
| `Beutl.FFmpegWorker` | GPL-3.0-or-later worker process. MIT projects reach it only through `Beutl.FFmpegIpc`. |
| `Beutl.Api` | Server API client. |
| `Beutl.AgentToolkit`, `Beutl.AgentToolkit.Mcp` | Agent-editing product functionality and its MCP host. Bundled skills and subagents are product assets under the toolkit project, not repository-wide development configuration. |

More detailed invariants live beside the modules in `src/Beutl.Engine/README.md`, `src/Beutl.Editor/README.md`, `src/Beutl.NodeGraph/README.md`, and `src/Beutl.FFmpegWorker/README.md`.
