# External agent runtime

`Beutl.Agents` owns thread metadata, the normalized timeline, and host permission decisions. It communicates with `Beutl.Agents.Worker` over UTF-8, newline-delimited JSON-RPC 2.0. The worker embeds Node.js on its own thread. The TypeScript adapters exchange JSON strings with that thread. Worker diagnostics go to stderr.

## Build and verify

Use the repository's .NET SDK and Node.js 24 or later with pnpm or npm on PATH. MSBuild builds the locked TypeScript project with esbuild and copies `bridge.mjs` into the worker output for both build and publish.

```sh
dotnet build src/Beutl.Agents.Worker
dotnet test tests/Beutl.Agents.Tests
```

The tests use local mock CLIs for the three provider protocols and have run on Windows x64 and Ubuntu x64 under WSL. Nuke places the worker beside the Beutl executable in release output and in `Beutl.app/Contents/MacOS` on macOS. Development builds copy it to the `Beutl.Agents.Worker` subdirectory. The installed .NET host can also launch a framework-dependent worker DLL.

The release smoke test checks that the published worker loads the bundled Node runtime and exits cleanly after a JSON-RPC shutdown. Run `python3 build/smoke_test_output.py output/Beutl` against a publish for the current platform on a machine without FFmpeg 8 installed.

## Embedding verification

On Windows x64, `Microsoft.JavaScript.NodeApi` 0.9.44 and `Microsoft.JavaScript.LibNode` 26.1.7 loaded Node.js 26.13.1 under .NET SDK 11.0.100. A standalone probe dynamically imported an ES module and awaited a managed callback with `runtime-async=on`. The worker also needed to replace Node's stdin with an empty stream. Importing `node:process` while the managed reader held the Windows stdin pipe delayed module initialization until EOF. [Embedding API source](https://github.com/microsoft/node-api-dotnet/tree/5b149325b1dad00b4c70446fc2a6564a01c152ca)

Inside the embedded runtime, `process.execPath` points to the .NET process. The Claude adapter supplies `pathToClaudeCodeExecutable` and launches the resolved Claude CLI through the shared provider process tracker. Use Claude Code 2.1.332 or newer for the SDK's stdin MCP configuration. Codex uses the App Server stdio protocol, including Codex's wire format without a `jsonrpc` field. ACP uses SDK 1.9.0 for `ClientSideConnection` and negotiates HTTP MCP and session loading with each provider. [Claude Agent SDK](https://github.com/anthropics/claude-agent-sdk-typescript) · [Codex App Server protocol](https://learn.chatgpt.com/docs/app-server) · [ACP TypeScript SDK](https://github.com/agentclientprotocol/typescript-sdk)

The app supplies the project path and MCP server configuration when starting a turn. The thread index and JSONL timeline are stored in `<BEUTL_HOME>/agent-threads/<project-hash>`. The index retains the external session ID for resuming. ACP mode mappings can be supplied with a custom provider definition when an agent advertises compatible mode IDs.
