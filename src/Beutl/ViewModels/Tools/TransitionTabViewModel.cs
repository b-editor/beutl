using System.Collections.Specialized;
using System.Reactive;
using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

// Edits the transition at one edge of an element, opened from the timeline: its type, picked as an
// effect's is, and its own properties. It shows the side that decides how the boundary blends and follows
// it when an edit replaces or removes it.
public sealed class TransitionTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private Element? _element;
    private ElementEdge _edge;
    private IDisposable? _editability;

    public TransitionTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Scene scene = editViewModel.Scene;

        TypeName = Transition.Select(value => value == null ? null : TypeDisplayHelpers.GetLocalizedName(value.GetType()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // An edit anywhere can replace the transition, remove it, or remove the element.
        Observable.FromEventPattern(h => scene.Edited += h, h => scene.Edited -= h)
            .Select(_ => Unit.Default)
            .Merge(Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => scene.Children.CollectionChanged += h,
                    h => scene.Children.CollectionChanged -= h)
                .Select(_ => Unit.Default))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => Refresh())
            .DisposeWith(_disposables);
    }

    public ToolTabExtension Extension => TransitionTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(GraphicsStrings.ClipTransition);

    // Whether an edge is open; the tab shows a hint until one is.
    public ReactivePropertySlim<bool> HasTarget { get; } = new();

    // Which edge is open: the element's enter or exit transition.
    public ReactivePropertySlim<string?> EdgeName { get; } = new();

    public ReactivePropertySlim<ClipTransition?> Transition { get; } = new();

    // The name of the transition's type, or null when the edge has none.
    public ReadOnlyReactivePropertySlim<string?> TypeName { get; }

    public ReactivePropertySlim<bool> CanEdit { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public void Show(Element element, ElementEdge edge)
    {
        _element = element;
        _edge = edge;
        EdgeName.Value = edge == ElementEdge.Start ? Strings.EnterTransition : Strings.ExitTransition;
        _editability?.Dispose();
        _editability = ElementEditability.Observe(element).Subscribe(editable => CanEdit.Value = editable);
        Refresh();
    }

    // Changes the type through the element service, so both sides of the boundary change together as
    // one history entry; null removes the transition.
    public void ChangeType(Type? type)
    {
        if (_element is not { } element || !ElementEditability.IsEditable(element)) return;

        IElementAttributeService service = _editViewModel.GetRequiredService<IElementAttributeService>();
        if (type == null)
        {
            service.RemoveTransition(element, _edge);
        }
        else if (type != Transition.Value?.GetType())
        {
            service.ApplyTransition(element, _edge, type);
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_element is { HierarchicalParent: null })
        {
            _element = null;
            _editability?.Dispose();
            _editability = null;
        }

        HasTarget.Value = _element != null;
        ClipTransition? transition = _element == null ? null : ElementTransitionEdits.GetDecidingTransition(_element, _edge);
        if (ReferenceEquals(transition, Transition.Value)) return;

        Transition.Value = transition;
        Properties.Value?.Dispose();
        Properties.Value = null;
        if (transition != null)
        {
            var properties = new PropertiesEditorViewModel(transition, _editViewModel.ExtensionProvider);
            NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), null, properties);
            Properties.Value = properties;
        }
    }

    // The child editors follow the lock of the element that owns the transition they edit.
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
        {
            return Transition.Value?.FindHierarchicalParent<Element>() ?? _element;
        }

        return _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _editability?.Dispose();
        Properties.Value?.Dispose();
        Properties.Value = null;
    }
}
