# Property editor registration migration

`PropertyEditorExtension` has one entry point now: an extension registers its
editors in `RegisterEditors`, and Beutl chooses among the registrations of all
loaded extensions. The nine virtual methods that matched properties and made
contexts and controls for each place an editor appears are gone. There are no
compatibility shims, so extensions must update their sources and rebuild.

| Before | After |
| --- | --- |
| `override MatchProperty(properties)` | `PropertyEditorOptions.Match`, or the value type of the registration |
| `override TryCreateContext(...)`, `TryCreateContextForNode`, `TryCreateContextForSettings`, `TryCreateContextForListItem` | the `createContext` of the registration; every place uses it |
| `override TryCreateControl(...)` | the `createControl` of the registration |
| `override TryCreateControlForNode`, `TryCreateControlForListItem`, `TryCreateControlForSettings` | `PropertyEditorOptions.CreateNodeControl`, `CreateListItemControl`, `CreateSettingsControl` |
| `base.TryCreateContext` and `base.TryCreateControl` to wrap the built-in editor | `registry.TryCreateDefault(property, out context)` and `registry.TryCreateControl(context, out control)` |
| `context.Extension.TryCreateControl(context, out control)` for a context from `IPropertyEditorFactory` | `registry.TryCreateControl(context, out control)` with the registry the extension kept |
| `IPropertyEditorFactory.MatchProperty(properties)` followed by `TryCreateContextForNode` or `TryCreateContextForListItem` | `IPropertyEditorFactory.CreateNodeEditor(property)` and `CreateListItemEditor(property)` |

## Registering editors

```csharp
[Export]
public sealed class MyPropertyEditorExtension : PropertyEditorExtension
{
    public override string Name => "My property editors";

    public override string DisplayName => "My property editors";

    protected override void RegisterEditors(PropertyEditorRegistry registry)
    {
        // Every property whose value is a MyLyrics.
        registry.Register<MyLyrics, LyricsContext>(p => new LyricsContext(p), c => new LyricsSummary(c));

        // Only the properties Match accepts.
        registry.Register<string, LayerStateContext>(
            p => new LayerStateContext(p),
            c => new LayerTreeEditor(c),
            new PropertyEditorOptions { Match = p => p.GetAttributes().OfType<LayerStateAttribute>().Any() });

        // Several properties in one editor; match picks them from the properties not yet taken.
        registry.RegisterGroup(
            properties => [.. properties.Where(p => p.GetEngineProperty()?.GetOwnerObject() is MyEffect)],
            properties => new MyEffectContext(properties),
            c => new MyEffectEditor(c));
    }
}
```

`RegisterEditors` runs once, the first time Beutl looks for an editor. The
registrations belong to the extension and go away when its package unloads.

- `Register<TValue, TContext>` takes an editor with its own context and control.
  Without `Match`, it takes every property whose value is a `TValue`.
- `RegisterGroup<TContext>` takes an editor of several properties.
- `RegisterValue<TValue, TEdit>` takes an editor with the standard look; see
  below.
- `PropertyEditorOptions.CreateNodeControl`, `CreateListItemControl` and
  `CreateSettingsControl` give the editor another control in a node of the node
  graph, in a list and on a settings page. Without them the editor's control is
  used there too; before, those places showed the built-in editor unless the
  extension overrode their methods. A list shows a control that is not an
  `IListItemEditor` in its item host, with the reorder handle and the delete
  button.

Beutl chooses each property's editor in this order: a registration whose `Match`
accepts the property or a group, `[ChoicesProvider]`, the exact value type, the
nearest base class, interfaces, and the `CoreObject` editor. Within each step a
package loaded later goes first, so an extension can replace a built-in editor;
within one package, the registration made first goes first. A `createContext` that returns `null` passes the property on to the next
registration. In the property panel, editors that match particular properties
come first and the others follow the order of the properties; an editor chosen
by its value type is no longer moved to the top.

## Editors with the standard look

`RegisterValue<TValue, TEdit>` pairs a view model with one of Beutl's controls.
Beutl binds the header, the description, the read-only state and the keyframe
buttons, and adds the property menu with keyframes, expressions and reset.

```csharp
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;

public sealed class MyPointEditorViewModel(IPropertyAdapter<MyPoint> property)
    : PropertyEditorViewModel<MyPoint, (float X, float Y)>(property)
{
    protected override (float X, float Y) ToEdit(MyPoint value) => (value.X, value.Y);

    protected override MyPoint FromEdit((float X, float Y) edit) => new(edit.X, edit.Y);
}

registry.RegisterValue<MyPoint, (float, float)>(p => new MyPointEditorViewModel(p), () => new Vector2Editor<float>());
```

The view model edits values of type `TEdit`, and the control must implement
`IValueEditorControl<TEdit>`; a pair that does not fit does not compile. The
controls in `Beutl.Controls.PropertyEditors` edit these types:

| Control | `TEdit` |
| --- | --- |
| `NumberEditor<T>`, `RationalEditor`, `TimeSpanEditor` | the number, `Rational`, `TimeSpan` |
| `StringEditor` | `string` |
| `BooleanEditor` | `bool` |
| `EnumEditor` | `int`, the index of the selected item |
| `Vector2Editor<T>`, `Vector3Editor<T>`, `Vector4Editor<T>` | `(T, T)`, `(T, T, T)`, `(T, T, T, T)` |
| `RelativePointEditor`, `RelativeRectEditor` | `RelativePoint`, `RelativeRect` |
| `ColorEditor`, `GradingColorEditor` | `Avalonia.Media.Color`, `GradingColor` |
| `AlignmentXEditor`, `AlignmentYEditor`, `TextAlignmentEditor` | the alignment |
| `FontFamilyEditor` | `FontFamily?` |
| `EasingEditor` | `Easing?` |
| `StorageFileEditor` | `FileInfo?` |

`PropertyEditorViewModel<TValue, TEdit>` writes the values, keyframes and
expressions as the built-in editors do. Override `PreviewEdit` and `ConfirmEdit`
to change how a value the user is still changing and a finished change are
applied. A view model can also override `Accept` to set up its control, such as
the items of an `EnumEditor`, before Beutl connects the value.

`StringEditor`, `NumberEditor<T>`, `RationalEditor`, `TimeSpanEditor` and
`StorageFileEditor` now derive from `StringEditorBase`, which holds the text
box and `TextProperty`. Code that looked for any of them as a `StringEditor`
looks for `StringEditorBase`.

## Wrapping the built-in editor

```csharp
registry.Register<string, TtsControllerViewModel>(
    p => registry.TryCreateDefault(p, out IPropertyEditorContext? inner) ? new TtsControllerViewModel(inner, this) : null!,
    c => new TtsControllerView { Inner = { Content = registry.TryCreateControl(c.Inner, out Control? control) ? control : null } },
    new PropertyEditorOptions { Match = IsTtsText });
```

`TryCreateDefault` makes the context of the built-in editor of a property.
`TryCreateControl` makes a new control for a context each time it is called, as
the property panel shows it; call it from `createControl` so that each control
of the wrapping editor gets its own. An extension that keeps its registry can
also call `TryCreateControl` later, for a context that
`IPropertyEditorFactory.CreateEditor` made.

## Tests

Tests that asked `MatchProperty` which properties an extension takes can test
the function passed as `Match`, or the value type, directly.
