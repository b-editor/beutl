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
public sealed class TransitionTabViewModel : IPinnableToolContext
{
    private const string PinnedElementJsonKey = "pinnedElementId";
    private const string PinnedEdgeJsonKey = "pinnedEdge";
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private readonly ToolTabPin _pin;
    private readonly ReactivePropertySlim<Element?> _shownElement = new();
    private Element? _element;
    private ElementEdge _edge;
    private IDisposable? _editability;
    private Element? _editabilityOwner;

    public TransitionTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Scene scene = editViewModel.Scene;
        _disposables.Add((IDisposable)IsSelected);
        _disposables.Add(HasTarget);
        _disposables.Add(_shownElement);
        _disposables.Add(EdgeName);
        _disposables.Add(Transition);
        _disposables.Add(CanEdit);
        _disposables.Add(Properties);

        _pin = new ToolTabPin(HasTarget).DisposeWith(_disposables);
        // A pinned tab names its edge, since both edges of one element can be pinned side by side.
        Header = _pin.IsPinned
            .CombineLatest(_shownElement, (pinned, element) => pinned ? element : null)
            .Select(ToolTabHeaderHelper.ObserveElementLabel)
            .Switch()
            .CombineLatest(EdgeName, (label, edge) => string.IsNullOrWhiteSpace(label)
                ? GraphicsStrings.ClipTransition
                : ToolTabHeaderHelper.Compose(edge ?? GraphicsStrings.ClipTransition, label))
            .ToReadOnlyReactivePropertySlim(GraphicsStrings.ClipTransition)
            .DisposeWith(_disposables)!;

        TypeName = Transition.Select(value => value == null ? null : TypeDisplayHelpers.GetLocalizedName(value.GetType()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        CanDelete = Transition.CombineLatest(CanEdit, (transition, editable) => transition != null && editable)
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

    public IReadOnlyReactiveProperty<string> Header { get; }

    public IReactiveProperty<bool> IsPinned => _pin.IsPinned;

    // Whether an edge is open; the tab shows a hint until one is.
    public ReactivePropertySlim<bool> HasTarget { get; } = new();

    IReadOnlyReactiveProperty<bool> IPinnableToolContext.HasTarget => _pin.HasTarget;

    // Which edge is open: the element's enter or exit transition.
    public ReactivePropertySlim<string?> EdgeName { get; } = new();

    public ReactivePropertySlim<ClipTransition?> Transition { get; } = new();

    // The name of the transition's type, or null when the edge has none.
    public ReadOnlyReactivePropertySlim<string?> TypeName { get; }

    public ReactivePropertySlim<bool> CanEdit { get; } = new();

    // Whether there is a transition to delete and it can be edited.
    public ReadOnlyReactivePropertySlim<bool> CanDelete { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    /// <summary>
    /// Finds the tab to show an edge in: the one already showing it, else an idle or unpinned one.
    /// </summary>
    public static TransitionTabViewModel? FindReusable(EditViewModel editViewModel, Element element, ElementEdge edge)
    {
        return ToolTabReuse.Find<TransitionTabViewModel>(
            editViewModel,
            t => t.IsShowing(element, edge),
            t => !t.HasTarget.Value,
            retargetAnyOpen: true);
    }

    public bool IsShowing(Element element, ElementEdge edge)
    {
        return _element == element && _edge == edge;
    }

    public void Show(Element element, ElementEdge edge)
    {
        _element = element;
        _edge = edge;
        EdgeName.Value = edge == ElementEdge.Start ? Strings.EnterTransition : Strings.ExitTransition;
        _editability?.Dispose();
        _editability = null;
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
        }

        HasTarget.Value = _element != null;
        _shownElement.Value = _element;
        ClipTransition? transition = _element == null ? null : ElementTransitionEdits.GetDecidingTransition(_element, _edge);
        if (!ReferenceEquals(transition, Transition.Value))
        {
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

        UpdateEditability();
    }

    // The type and the transition change through both the open element and the element that owns the
    // shown transition, which is the one across the edge when its side decides how the boundary blends;
    // either being locked refuses the change.
    private void UpdateEditability()
    {
        Element? owner = Transition.Value?.FindHierarchicalParent<Element>() ?? _element;
        if (_editability != null && ReferenceEquals(owner, _editabilityOwner)) return;

        _editability?.Dispose();
        _editability = null;
        _editabilityOwner = owner;
        if (_element is not { } element)
        {
            CanEdit.Value = false;
            return;
        }

        _editability = ElementEditability.Observe(element)
            .CombineLatest(ElementEditability.Observe(owner), (open, owning) => open && owning)
            .Subscribe(editable => CanEdit.Value = editable);
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

    // Only a pinned tab is saved with its edge; an unpinned one waits for the timeline to open one.
    public void ReadFromJson(JsonObject json)
    {
        if (ToolTabPin.WasPinned(json)
            && json.TryGetPropertyValueAsJsonValue(PinnedElementJsonKey, out Guid id)
            && json.TryGetPropertyValueAsJsonValue(PinnedEdgeJsonKey, out string? edgeName)
            && Enum.TryParse(edgeName, out ElementEdge edge)
            && _editViewModel.Scene.FindById(id) is Element element)
        {
            Show(element, edge);
            _pin.ReadFromJson(json);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        _pin.WriteToJson(json);
        if (IsPinned.Value && _element is { } element)
        {
            json[PinnedElementJsonKey] = element.Id;
            json[PinnedEdgeJsonKey] = _edge.ToString();
        }
        else
        {
            json.Remove(PinnedElementJsonKey);
            json.Remove(PinnedEdgeJsonKey);
        }
    }

    public void Dispose()
    {
        _editability?.Dispose();
        Properties.Value?.Dispose();
        Properties.Value = null;
        _disposables.Dispose();
    }
}
