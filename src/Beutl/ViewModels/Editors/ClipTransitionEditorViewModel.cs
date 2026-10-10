using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

// One entry of the transition type list; Type is null for having no transition.
public sealed record ClipTransitionTypeItem(Type? Type, string Name);

// Edits an element's transition at one edge: which type it is, picked from the built-in and registered
// transitions or none, and its own properties beneath. A type change goes through the element service,
// so both sides of a boundary change together as one history entry.
public sealed class ClipTransitionEditorViewModel : ValueEditorViewModel<ClipTransition?>
{
    public ClipTransitionEditorViewModel(IPropertyAdapter<ClipTransition?> property)
        : base(property)
    {
        Types =
        [
            new ClipTransitionTypeItem(null, Strings.Null),
            .. ElementTransitionEdits.GetTransitionTypes()
                .Select(type => new ClipTransitionTypeItem(type, TypeDisplayHelpers.GetLocalizedName(type))),
        ];

        SelectedType = Value.Select(value => Types.FirstOrDefault(item => item.Type == value?.GetType()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CanChangeType = (GetSide() is { } side ? ElementEditability.Observe(side.Element) : CanEdit)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        // The child editors are built once Accept has supplied the editor session's ExtensionProvider.
        Value.CombineLatest(ObserveExtensionProvider())
            .Subscribe(t => UpdateProperties(t.First, t.Second))
            .DisposeWith(Disposables);
    }

    public IReadOnlyList<ClipTransitionTypeItem> Types { get; }

    public ReadOnlyReactivePropertySlim<ClipTransitionTypeItem?> SelectedType { get; }

    public ReadOnlyReactivePropertySlim<bool> CanChangeType { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public void ChangeType(Type? type)
    {
        if (IsDisposed || type == Value.Value?.GetType()) return;

        if (GetSide() is { } side && GetService(typeof(IElementAttributeService)) is IElementAttributeService service)
        {
            if (!ElementEditability.IsEditable(side.Element)) return;

            if (type == null)
                service.RemoveTransition(side.Element, side.Edge);
            else
                service.ApplyTransition(side.Element, side.Edge, type);
            return;
        }

        if (!IsElementEditable) return;
        SetValue(Value.Value, type == null ? null : ElementTransitionEdits.CreateTransition(type, ClipTransition.DefaultDuration));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            AcceptChildren();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Properties.Value = null;
    }

    // The element and edge this editor's property holds the transition of, when it is one of an
    // element's transition properties.
    private (Element Element, ElementEdge Edge)? GetSide()
    {
        return PropertyAdapter is CorePropertyAdapter<ClipTransition?> { Object: Element element } adapter
               && ElementTransitionEdits.GetEdge(adapter.Property) is { } edge
            ? (element, edge)
            : null;
    }

    private void UpdateProperties(ClipTransition? transition, Beutl.Api.Services.ExtensionProvider extensionProvider)
    {
        Properties.Value?.Dispose();
        Properties.Value = transition == null ? null : new PropertiesEditorViewModel(transition, extensionProvider);
        AcceptChildren();
    }

    private void AcceptChildren()
    {
        NestedEditorContextHelper.AcceptChildren(new Visitor(this), null, Properties.Value);
    }

    // Hands the child editors the element that owns the transition, so they follow its lock.
    private sealed record Visitor(ClipTransitionEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Element) && Obj.GetSide() is { } side)
                return side.Element;

            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
