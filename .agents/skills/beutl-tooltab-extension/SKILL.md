---
name: beutl-tooltab-extension
description: Implementation guide for Beutl's ToolTabExtension (tool-tab extension). Use when adding a custom tool tab to the editor. Triggers when ToolTabExtension, IToolContext, or docking-tab implementations are needed.
---

# Beutl ToolTabExtension implementation guide

## Overview

`ToolTabExtension` is the extension point that adds a dockable tool tab to the Beutl editor. It follows the MVVM pattern with two classes: the Extension (metadata + factory) and the ViewModel (an `IToolContext` implementation).

## Class hierarchy

```
Extension (base)
  └─ ViewExtension
       └─ ToolTabExtension (tool tab)
```

## Core implementation vs extension implementation

| Item | Core implementation | Extension implementation |
|---|---|---|
| Attribute | `[PrimitiveImpl]` | `[Export]` |
| Singleton | Define an `Instance` field | Not used (omit) |
| Registration | Add to `LoadPrimitiveExtensionTask.PrimitiveExtensions` | Registered automatically |
| Injecting Extension into ViewModel | Reference `Instance` | Inject via constructor |

---

## Core-implementation pattern

### Step 1: Subclass `ToolTabExtension`

```csharp
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Extensibility;
using Beutl.Language;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class MyToolTabExtension : ToolTabExtension
{
    public static readonly MyToolTabExtension Instance = new();

    // Whether multiple instances are allowed
    public override bool CanMultiple => false;

    // Stable identifier (not localized)
    public override string Name => "My tool tab";

    // Localized display name (shown in the "add tool tab" menu)
    public override string DisplayName => Strings.MyToolTab;

    // Tab header (returning null hides it from the menu).
    // Use null when the tab should only be opened from code.
    public override string? Header => Strings.MyToolTab;

    // Default docking position: None / Left / Right / Bottom / Player
    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    // Sort order among tabs sharing the same anchor (lower = earlier)
    public override int DefaultOrder => 0;

    // Open automatically when a new editor opens
    public override bool OpenByDefault => false;

    // Create the view (the UI control)
    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        control = new MyToolTabView();
        return true;
    }

    // Create the ViewModel (the IToolContext)
    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        context = new MyToolTabViewModel(editorContext);
        return true;
    }
}
```

### Step 2: ViewModel implementing `IToolContext` (core)

```csharp
using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class MyToolTabViewModel : IToolContext
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = [];

    public MyToolTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
    }

    // Reference the singleton
    public ToolTabExtension Extension => MyToolTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.MyToolTab;

    public void Dispose() => _disposables.Dispose();

    public void ReadFromJson(JsonObject json) { }
    public void WriteToJson(JsonObject json) { }

    public object? GetService(Type serviceType)
        => _editorContext.GetService(serviceType);
}
```

> `IToolContext` itself only requires `Extension`, `IsSelected`, and `Header`
> (plus `IDisposable` / `IJsonSerializable` / `IServiceProvider`). Docking
> placement is declared on the **Extension** via `DefaultAnchor` /
> `DefaultOrder` / `OpenByDefault`, not on the ViewModel.

### Step 3: Register with `PrimitiveExtensions`

```csharp
// Add Instance to PrimitiveExtensions in LoadPrimitiveExtensionTask.cs
public static readonly Extension[] PrimitiveExtensions =
[
    // ... existing extensions ...
    MyToolTabExtension.Instance,
];
```

### Step 4: Add string resources

```xml
<!-- src/Beutl.Language/Strings.resx -->
<data name="MyToolTab" xml:space="preserve">
  <value>My Tool Tab</value>
</data>

<!-- src/Beutl.Language/Strings.ja.resx -->
<data name="MyToolTab" xml:space="preserve">
  <value>マイツールタブ</value>
</data>
```

---

## Extension-implementation pattern

### Step 1: Subclass `ToolTabExtension`

```csharp
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Extensibility;

namespace MyExtension;

[Export]  // Extensions use the [Export] attribute
[Display(Name = nameof(Strings.MyToolTab), ResourceType = typeof(Strings))]
public sealed class MyToolTabExtension : ToolTabExtension
{
    // No singleton (omit it)

    public override bool CanMultiple => false;

    public override string? Header => Strings.MyToolTab;

    // Default docking position: None / Left / Right / Bottom / Player
    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        control = new MyToolTabView();
        return true;
    }

    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        // Inject the Extension instance (this) via the constructor
        context = new MyToolTabViewModel(this, editorContext);
        return true;
    }
}
```

### Step 2: ViewModel implementing `IToolContext` (extension)

```csharp
using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace MyExtension;

public sealed class MyToolTabViewModel : IToolContext
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = [];

    // Accept the Extension in the constructor
    public MyToolTabViewModel(ToolTabExtension extension, IEditorContext editorContext)
    {
        Extension = extension;
        _editorContext = editorContext;
    }

    // Return the instance injected in the constructor
    public ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.MyToolTab;

    public void Dispose() => _disposables.Dispose();

    public void ReadFromJson(JsonObject json) { }
    public void WriteToJson(JsonObject json) { }

    public object? GetService(Type serviceType)
        => _editorContext.GetService(serviceType);
}
```

### Step 3: Create string resources

Add `Strings.resx` and `Strings.ja.resx` inside the extension project, generated via ResXFileCodeGenerator.

---

## Create the View

```xml
<!-- MyToolTabView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyExtension.MyToolTabView">
    <TextBlock Text="Hello from My Tool Tab!"
               VerticalAlignment="Center"
               HorizontalAlignment="Center"/>
</UserControl>
```

## DockAnchor options

`ToolTabExtension.DefaultAnchor` returns a `DockAnchor` (see
`src/Beutl.Extensibility/DockAnchor.cs`). It is the default docking position
when the tab is first opened; the user can re-dock afterwards.

| Value | Meaning |
|---|---|
| None | No fixed anchor — falls back to the first available tool dock |
| Left | Left sidebar |
| Right | Right sidebar |
| Bottom | Bottom panel |
| Player | The player's own dock area (reserved; don't use for ordinary tabs) |

## Services available from `IEditorContext`

| Service | Description |
|---|---|
| `IEditorSelection` | Observe the currently selected object |
| `IEditorClock` | Observe the playback clock |
| `IPreviewPlayer` | Control preview playback |
| `IElementAdder` | Add elements |
| `IPropertyEditorFactory` | Build property editors |
| `IPropertiesEditorFactory` | Build property-list editors |
| `HistoryManager` | Undo/redo history |
| `Scene` | Current scene |

## Required NuGet packages

- `Beutl.Extensibility`
- `Beutl.Editor`
- `Reactive.Bindings`
- `FluentAvalonia` (for icons)

## Reference

For detailed implementation patterns, see `references/implementation-patterns.md`.
