# ToolTabExtension implementation patterns

## Table of contents

- [Simple pattern (extension)](#simple-pattern-extension)
- [Selection-aware pattern](#selection-aware-pattern)
- [State-persistence pattern](#state-persistence-pattern)
- [Context-command pattern](#context-command-pattern)
- [Hidden-from-menu pattern](#hidden-from-menu-pattern)

---

## Simple pattern (extension)

Minimal implementation: a tab that just shows a static UI.

```csharp
// Extension
[Export]
[Display(Name = nameof(Strings.SimpleTab), ResourceType = typeof(Strings))]
public sealed class SimpleTabExtension : ToolTabExtension
{
    public override bool CanMultiple => true;

    public override string? Header => Strings.SimpleTab;

    public override IconSource GetIcon()
        => new SymbolIconSource { Symbol = Symbol.Info };

    public override bool TryCreateContent(IEditorContext editorContext, out Control? control)
    {
        control = new TextBlock { Text = "Hello!" };
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, out IToolContext? context)
    {
        context = new SimpleContext(this);
        return true;
    }
}

// ViewModel
public sealed class SimpleContext : IToolContext
{
    public SimpleContext(ToolTabExtension extension)
    {
        Extension = extension;
    }

    public ToolTabExtension Extension { get; }
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public string Header => Strings.SimpleTab;
    public IReactiveProperty<TabPlacement> Placement { get; } =
        new ReactivePropertySlim<TabPlacement>(TabPlacement.RightUpperBottom);
    public IReactiveProperty<TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<TabDisplayMode>();

    public void Dispose() { }
    public object? GetService(Type t) => null;
    public void ReadFromJson(JsonObject j) { }
    public void WriteToJson(JsonObject j) { }
}
```

---

## Selection-aware pattern

A tab that reacts to the editor's current selection.

```csharp
public sealed class SelectionAwareViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;

    public SelectionAwareViewModel(ToolTabExtension extension, IEditorContext editorContext)
    {
        Extension = extension;
        _editorContext = editorContext;

        // Observe the selection
        editorContext.GetRequiredService<IEditorSelection>().SelectedObject
            .Subscribe(OnSelectionChanged)
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension { get; }

    public ReactiveProperty<ICoreObject?> CurrentSelection { get; } = new();

    private void OnSelectionChanged(ICoreObject? obj)
    {
        CurrentSelection.Value = obj;
        // React to the selection
    }

    // ... IToolContext implementation ...

    public void Dispose()
    {
        _disposables.Dispose();
        CurrentSelection.Dispose();
    }
}
```

---

## State-persistence pattern

A tab whose state is saved and restored.

```csharp
public sealed class StatefulViewModel : IToolContext
{
    public ReactiveProperty<string> SearchText { get; } = new("");
    public ReactiveProperty<int> SelectedIndex { get; } = new(0);

    // ... other properties ...

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("searchText", out var st))
            SearchText.Value = st?.GetValue<string>() ?? "";

        if (json.TryGetPropertyValue("selectedIndex", out var si))
            SelectedIndex.Value = si?.GetValue<int>() ?? 0;
    }

    public void WriteToJson(JsonObject json)
    {
        json["searchText"] = SearchText.Value;
        json["selectedIndex"] = SelectedIndex.Value;
    }
}
```

---

## Context-command pattern

Bind keyboard shortcuts to commands.

### Step 1: Define `ContextCommandDefinition`s in the Extension

```csharp
[Export]
[Display(Name = nameof(Strings.CommandAwareTab), ResourceType = typeof(Strings))]
public sealed class CommandAwareExtension : ToolTabExtension
{
    // Declare the context commands (name, display name, description, key gestures)
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition(
            "DoSomething",
            Strings.DoSomething,
            Strings.DoSomething_Description,
        [
            new ContextCommandKeyGesture("Ctrl+D"),
            new ContextCommandKeyGesture("Cmd+D", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition(
            "Reset",
            Strings.Reset,
            Strings.Reset_Description,
        [
            new ContextCommandKeyGesture("Ctrl+R"),
            new ContextCommandKeyGesture("Cmd+R", OSPlatform.OSX),
        ]),
    ];

    // ... rest of the implementation ...
}
```

### Step 2: Implement `IContextCommandHandler` in the ViewModel

```csharp
public sealed class CommandAwareViewModel : IToolContext, IContextCommandHandler
{
    // ... fields, constructor ...

    // Command execution
    public void Execute(ContextCommandExecution execution)
    {
        // When invoked via a key event, mark it handled
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;

        switch (execution.CommandName)
        {
            case "DoSomething":
                // DoSomething logic
                break;

            case "Reset":
                // Reset logic
                break;

            default:
                // Unknown commands: undo the Handled flag
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }

    // ... IToolContext implementation ...
}
```

### Alternative: `[ContextCommand]` attribute

You can also annotate methods with `[ContextCommand]`.

```csharp
public sealed class CommandAwareViewModel : IToolContext
{
    [ContextCommand]
    public void DoSomething(KeyEventArgs e)
    {
        // logic
        e.Handled = true;
    }

    [ContextCommand]
    public void Reset()
    {
        // logic
    }
}
```

---

## Hidden-from-menu pattern

A tab that does not appear in the menu and is opened only from code.

```csharp
[Export]
[Display(Name = nameof(Strings.HiddenTab), ResourceType = typeof(Strings))]
public sealed class HiddenTabExtension : ToolTabExtension
{
    public override bool CanMultiple => false;

    // Returning null for Header hides the entry from the menu
    public override string? Header => null;

    public override IconSource GetIcon()
        => new SymbolIconSource { Symbol = Symbol.Code };

    // ... rest of the implementation ...
}

// Open the tab from code
public void OpenHiddenTab(IEditorContext editorContext)
{
    var extension = ExtensionProvider.Current.AllExtensions
        .OfType<HiddenTabExtension>()
        .FirstOrDefault();

    if (extension?.TryCreateContext(editorContext, out var context) == true)
    {
        editorContext.OpenToolTab(context);
    }
}
```

---

## Best practices

1. **Display name**: use `[Display(Name = nameof(Strings.XXX), ResourceType = typeof(Strings))]`.
2. **Singleton**: core implementations define an `Instance` field; extensions do not need one.
3. **Extension injection**: extensions receive their Extension instance via the constructor.
4. **CompositeDisposable**: manage subscriptions in a shared `_disposables`.
5. **ReactivePropertySlim**: prefer it for plain values (lightweight).
6. **ReactiveProperty**: use when validation or conversion is required.
7. **CanMultiple**: set to `false` when only a single instance makes sense.
8. **Header**: return `null` to keep the tab out of the menu.
9. **Localization**: route every visible string through `Strings.XXX`.
