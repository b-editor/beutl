# Tool-tab extension guide

`ToolTabExtension` adds a dockable tool to the Beutl editor. The public contract is defined in `src/Beutl.Extensibility/SceneEditorTabExtension.cs`; existing in-tree examples live under `src/Beutl.Editor.Components/*Tab/`.

## Extension and context responsibilities

A tool tab has two cooperating types:

- A `ToolTabExtension` supplies metadata and creates the view and context through `TryCreateContent` and `TryCreateContext`.
- An `IToolContext` owns per-tab state, subscriptions, persistence, services, and deterministic cleanup.

The extension defines:

- `CanMultiple`: whether more than one context may be open.
- `Header`: the add-tab menu label; `null` hides the tool from that menu.
- `DefaultAnchor`: the initial `Left`, `Right`, `Bottom`, or `Player` dock.
- `DefaultOrder`: ordering among tools with the same anchor.
- `OpenByDefault`: whether a new editor opens the tool automatically.
- `ReuseContentAcrossActivation`: whether the host may reuse the control across activation cycles.

The context implements `Extension`, `IsSelected`, and the reactive per-instance `Header`, together with `IDisposable`, `IJsonSerializable`, and `IServiceProvider`. A multi-instance tool must give each context a distinguishable tab title.

## Registration

In-tree primitive tabs use `[PrimitiveImpl]`, expose a stable singleton `Instance`, and add that instance to `LoadPrimitiveExtensionTask.PrimitiveExtensions` in `src/Beutl/Services/StartupTasks/LoadPrimitiveExtensionTask.cs`.

Out-of-tree extensions use `[Export]` and are discovered by the extension loader. They should inject the current `ToolTabExtension` instance into their `IToolContext` instead of defining a global singleton.

Use localized resources for `DisplayName`, menu `Header`, and the context's tab title. Keep `Name` stable and non-localized.

## Lifecycle and persistence

- The `IToolContext` owns subscriptions and state that must survive view unload/reload. Dispose them from `IToolContext.Dispose` when the dockable closes.
- A reused content control may still leave and re-enter the visual tree; do not use view attachment as the lifetime of editor services.
- Save per-tab state in `WriteToJson` and restore it in `ReadFromJson`.
- Resolve editor services through the supplied `IEditorContext`; forward `GetService` when the context itself is used as a service provider.
- Push reactive UI state, including a changing tab title, on the UI thread.

## Docking and opening

`DefaultAnchor` only selects the initial dock. Users may re-dock the tab afterward. The host filters menu entries by non-null `ToolTabExtension.Header` and orders them by extension name. Programmatic callers open and close contexts through `IEditorContext.OpenToolTab` and `IEditorContext.CloseToolTab`.

## Implementation references

- `src/Beutl.Editor.Components/ProxiesTab/ProxiesTabExtension.cs` and `ViewModels/ProxiesTabViewModel.cs`: single-instance right-side tool with service subscriptions and persisted context state.
- `src/Beutl.Editor.Components/FileBrowserTab/`: multi-instance context pattern.
- `src/Beutl/Services/PrimitiveImpls/VersionControlTabExtension.cs`: conditional content/context creation.
- `src/Beutl/ViewModels/Dock/BeutlDockFactory.cs` and `DockHostViewModel.cs`: host placement, opening, and closing behavior.

New views must enable Avalonia compiled bindings with both `x:CompileBindings="True"` and `x:DataType`.
