# Project structure and module boundaries

| Project | Responsibility |
|---|---|
| `Beutl.Utilities`, `Beutl.Threading` | Low-level helpers, and the `Dispatcher` with its scheduling primitives. |
| `Beutl.Core` | `CoreObject`/`CoreProperty`, serialization, file sources, collections, and the Beutl home directory layout. |
| `Beutl.Configuration` | Application settings (`GlobalConfiguration` and its sections). |
| `Beutl.Language` | Localized string resources; see `src/Beutl.Language/README.md` for which file to use. |
| `Beutl.Engine` | Core rendering, animation, audio, composition, and media abstractions. It must not depend on UI projects. |
| `Beutl.Engine.SourceGenerators` | Roslyn source generators and analyzers consumed by the engine and downstream projects. |
| `Beutl.ProjectSystem` | Project, scene, and element model, its persistence and recovery, and scene rendering. |
| `Beutl.Editor` | Non-UI editor logic, including undo/redo, packaging, and editing-pipeline services. |
| `Beutl.Editor.Components`, `Beutl.Controls`, `Beutl` | Avalonia views, controls, view models, and the application shell. |
| `Beutl.Extensibility` | Plugin-facing abstractions. |
| `Beutl.NodeGraph` | Node editor model and evaluation. |
| `Beutl.Api` | Server API and AI service clients, package installation, and extension loading (`ExtensionProvider`). |
| `Beutl.AgentToolkit`, `Beutl.AgentToolkit.Mcp` | Agent-editing product functionality and its MCP host. Bundled skills and subagents are product assets under the toolkit project, not repository-wide development configuration. |
| `Beutl.Extensions.FFmpeg`, `Beutl.Extensions.FFmpeg.Core` | MIT FFmpeg decoding and encoding that runs the worker through IPC; `.Core` is the headless encoding part. |
| `Beutl.Extensions.MediaFoundation`, `Beutl.Extensions.AVFoundation` | Platform media backends for Windows and macOS. |
| `Beutl.FFmpegIpc` | MIT IPC transport and adapters used to communicate with the FFmpeg worker. |
| `Beutl.FFmpegWorker` | GPL-3.0-or-later worker process. MIT projects reach it only through `Beutl.FFmpegIpc`. |
| `Beutl.ExceptionHandler`, `Beutl.PackageTools.UI` | Separate processes that show crash reports and install, update, or remove packages. |

More detailed invariants live beside the modules in `src/Beutl.Engine/README.md`, `src/Beutl.Editor/README.md`, `src/Beutl.NodeGraph/README.md`, and `src/Beutl.FFmpegWorker/README.md`. `tests/README.md` maps each module to its test project.
