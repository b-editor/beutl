# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

**Build System:** NUKE build automation with .NET 9.0
```bash
# Primary build command (cross-platform)
./build.sh                           # Linux/macOS
build.cmd                           # Windows

# Standard .NET commands
dotnet restore Beutl.slnx
dotnet build Beutl.slnx --no-restore -f net9.0
dotnet test Beutl.slnx --no-build --verbosity normal -f net9.0

# Run the main application
dotnet run --project src/Beutl/Beutl.csproj
```

**Linting/Analysis:**
- Code analysis is integrated via Roslynator analyzers in Directory.Build.props
- No separate lint command - use `dotnet build` which includes analysis

**Testing:**
- Framework: NUnit with 80% code coverage threshold
- Run tests: `dotnet test` (included in build verification)
- Benchmarks available in `tests/Beutl.Benchmarks/`

## Architecture Overview

**Beutl** is a cross-platform video editing/compositing application built with .NET 9 and Avalonia UI, using a modular layered architecture:

### Core Libraries (Foundation)
- **Beutl.Core**: Reactive object model, serialization, command/undo system, property framework
- **Beutl.Engine**: Graphics rendering pipeline, animation system, media processing, **3D rendering system**
- **Beutl.ProjectSystem**: Project management, node-based operations, scene composition

### Extension System
- **Beutl.Extensibility**: Plugin framework for effects, codecs, UI extensions
- **Platform Extensions**: FFmpeg, MediaFoundation (Windows), AVFoundation (macOS)
- **Beutl.Operators**: Built-in operations and effects

### Application Layer
- **Beutl**: Main MVVM application using Avalonia UI and ReactiveProperty
- **Beutl.Controls**: Custom UI controls and property editors
- **Beutl.Api**: Cloud services integration for extension marketplace

### Key Design Patterns
1. **Command Pattern**: Comprehensive undo/redo via `CommandRecorder`/`CommandStack`
2. **Reactive Properties**: Core property system using System.Reactive  
3. **Node-based Processing**: Graph-based operations in `NodeTree` namespace
4. **Plugin Architecture**: Dynamic extension loading system
5. **MVVM**: Clean separation using ReactiveProperty ViewModels

## Code Conventions

**Follow .NET Coding Style guidelines:**
- C# 12+ with nullable reference types enabled
- Four-space indentation in XAML files
- Use compiled XAML bindings for performance:
```xaml
<UserControl x:CompileBindings="True" x:DataType="viewModel:MyViewModel">
    <TextBox Text="{Binding Text.Value}" />
</UserControl>
```
- When `x:CompileBindings="True"` is set, use `{Binding}` syntax instead of `{CompiledBinding}`

**UI Implementation:**
- Complex event handlers → separate into Behaviors or `partial` classes
- Property alignment: first property on element line, subsequent aligned below

## Key Dependencies
- **UI Framework**: Avalonia UI (cross-platform)
- **Graphics**: SkiaSharp for 2D rendering
- **Media Processing**: FFmpeg, OpenCV, platform-specific media frameworks
- **Reactive**: System.Reactive for property and event handling
- **Package Management**: Central package management via Directory.Packages.props

## Project Structure Notes
- `src/` contains all production code organized by layer
- `tests/` includes unit tests, benchmarks, and test applications
- Build outputs to `artifacts/` directory
- Extensions follow plugin architecture in `Beutl.Extensions.*` projects
- Version management via Nerdbank.GitVersioning (currently v1.0.6)

## Development Workflow
- Open issues before major PRs to avoid duplication
- **Rebase and force push** to keep git history clean
- CI validates builds, tests, and code coverage on all platforms
- Daily builds with automated versioning
- Multi-platform targeting: Windows x64, Linux x64, macOS x64/ARM64