using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.ViewModels;

public sealed class PathEditorTabViewModel : IDisposable, IPathEditorContext, IPinnableToolContext
{
    private const string PinnedFigureJsonKey = "pinnedFigureId";
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorClock _clock;
    private readonly ToolTabPin _pin;
    // The editors a pinned tab detached from the property tab that lent FigureContext, or built when restored.
    private IGeometryEditorContext? _ownedGeometry;
    private IPathFigureEditorContext? _ownedFigure;

    public PathEditorTabViewModel(IEditorContext editorContext)
    {
        EditorContext = editorContext;
        _clock = editorContext.GetRequiredService<IEditorClock>();
        var player = editorContext.GetRequiredService<IPreviewPlayer>();

        IsPlaying = player.IsPlaying
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        var chain = PathFigureContextChain.Create(FigureContext, _disposables);
        Context = chain.Context;
        PathGeometry = chain.PathGeometry;
        PathFigure = chain.PathFigure;
        Element = chain.Element;

        _pin = new ToolTabPin(FigureContext.Select(context => context != null)).DisposeWith(_disposables);
        _pin.IsPinned.Where(pinned => pinned)
            .Subscribe(_ => DetachFigureContext())
            .DisposeWith(_disposables);
        FigureContext.Where(context => _ownedFigure != null && !ReferenceEquals(context, _ownedFigure))
            .Subscribe(_ => ReleaseOwnedEditors())
            .DisposeWith(_disposables);

        Header = _pin.IsPinned
            .CombineLatest(Element, (pinned, element) => pinned ? element : null)
            .Select(ToolTabHeaderHelper.ObserveElementLabel)
            .Switch()
            .Select(label => ToolTabHeaderHelper.Compose(Strings.PathEditor, label))
            .ToReadOnlyReactivePropertySlim(Strings.PathEditor)
            .DisposeWith(_disposables)!;

        GeometryResource = PathGeometry
            .SwitchToEngineVersionedResource(_clock.CurrentTime, (o, c) => o.ToResource(c))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsClosed = PathFigure.Select(f => f != null
                ? f.IsClosed.SubscribeEngineProperty(f, _clock.CurrentTime)
                : Observable.ReturnThenNever(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        FigureContext.Subscribe(_ => SelectedOperation.Value = null)
            .DisposeWith(_disposables);

        var figureChanges = PathFigure.Select(figure => figure != null
                ? Observable.FromEventPattern(h => figure.Edited += h, h => figure.Edited -= h)
                    .Select(_ => 0).StartWith(0)
                : Observable.ReturnThenNever(0))
            .Switch();
        Observable.Merge(SelectedOperation.Select(_ => 0), Element.Select(_ => 0),
                _clock.CurrentTime.Select(_ => 0), figureChanges)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdatePointProperties())
            .DisposeWith(_disposables);

        // PathFigure の DetachedFromHierarchy を購読し、detach 時に FigureContext をクリア
        PathFigure.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical old)
                    old.DetachedFromHierarchy -= OnPathFigureDetached;
                if (v.NewValue is IHierarchical @new)
                    @new.DetachedFromHierarchy += OnPathFigureDetached;
            })
            .DisposeWith(_disposables);
    }

    public IEditorContext EditorContext { get; }

    public IReactiveProperty<IPathFigureEditorContext?> FigureContext { get; } =
        new ReactiveProperty<IPathFigureEditorContext?>();

    public ReadOnlyReactivePropertySlim<IGeometryEditorContext?> Context { get; }

    public ReadOnlyReactivePropertySlim<EngineResourceHandle<PathGeometry.Resource>?> GeometryResource { get; }

    public IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    public IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    public IReadOnlyReactiveProperty<Element?> Element { get; }

    public IReactiveProperty<PathSegment?> SelectedOperation { get; } = new ReactiveProperty<PathSegment?>();

    public ReactivePropertySlim<IReadOnlyList<IPropertyEditorContext>> PointProperties { get; } = new([]);

    private Element? _propertiesElement;
    private PathPointProperty[] _pointProperties = [];

    private void UpdatePointProperties()
    {
        Element? element = Element.Value;
        PathPointProperty[] properties = SelectedOperation.Value is { } anchor
            && PathFigure.Value is { } figure && element != null
            ? PathPointProperties.Get(figure, anchor, new CompositionContext(_clock.CurrentTime.Value)) : [];
        if (ReferenceEquals(_propertiesElement, element) && _pointProperties.SequenceEqual(properties)) return;

        ClearPointProperties();
        _propertiesElement = element;
        _pointProperties = properties;
        if (properties.Length == 0 || element == null) return;

        var factory = EditorContext.GetRequiredService<IPropertyEditorFactory>();
        var visitor = new PropertyServices(EditorContext, element);
        var editors = new List<IPropertyEditorContext>(properties.Length);
        foreach (PathPointProperty point in properties)
        {
            string label = point.Role switch
            {
                PathPointPropertyRole.Position => GraphicsStrings.Position,
                PathPointPropertyRole.Incoming => Strings.PathEditor_IncomingControlPoint,
                PathPointPropertyRole.Outgoing => Strings.PathEditor_OutgoingControlPoint,
                _ => GraphicsStrings.QuadraticBezierSegment_ControlPoint
            };
            IPropertyAdapter adapter = point.Property is AnimatableProperty<BtlPoint> animated
                ? new AnimatedPathPointPropertyAdapter(animated, point.Owner, label)
                : new PathPointPropertyAdapter(point.Property, point.Owner, label);
            if (factory.CreateEditor(adapter) is { } editor)
            {
                editor.Accept(visitor);
                editors.Add(editor);
            }
        }
        PointProperties.Value = editors;
    }

    private void ClearPointProperties()
    {
        var previous = PointProperties.Value;
        PointProperties.Value = [];
        foreach (var editor in previous) editor.Dispose();
        _pointProperties = [];
        _propertiesElement = null;
    }

    private sealed record PropertyServices(IEditorContext Editor, Element Element)
        : IPropertyEditorContextVisitor, IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(Element)
            ? Element : Editor.GetService(serviceType);

        public void Visit(IPropertyEditorContext context) { }
    }

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public ReadOnlyReactiveProperty<bool> IsPlaying { get; }

    public IReactiveProperty<bool> Symmetry { get; } = new ReactiveProperty<bool>(true);

    public IReactiveProperty<bool> Asymmetry { get; } = new ReactiveProperty<bool>(false);

    public IReactiveProperty<bool> Separately { get; } = new ReactiveProperty<bool>(false);

    public ToolTabExtension Extension { get; } = PathEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public IReactiveProperty<bool> IsPinned => _pin.IsPinned;

    public IReadOnlyReactiveProperty<bool> HasTarget => _pin.HasTarget;

    /// <summary>
    /// Finds the tab to edit <paramref name="figure"/> in: the one already editing it, else an unpinned one.
    /// </summary>
    public static PathEditorTabViewModel? FindReusable(IEditorContext editorContext, PathFigure? figure)
    {
        return ToolTabReuse.Find<PathEditorTabViewModel>(
            editorContext,
            t => t.IsEditing(figure),
            t => t.FigureContext.Value is null,
            retargetAnyOpen: true);
    }

    public bool IsEditing(PathFigure? figure)
    {
        return figure != null && FigureContext.Value != null && PathFigure.Value == figure;
    }

    // FigureContextがcontext引数と同じ場合、編集を終了
    public void StartOrFinishEdit(IPathFigureEditorContext context)
    {
        PathFigureContextChain.ToggleEditing(FigureContext, context);
    }

    public void FinishEdit()
    {
        if (FigureContext.Value is { } context)
        {
            StartOrFinishEdit(context);
        }
    }

    // The property tab disposes the editors it lent when it moves on to another selection, so a pinned tab
    // switches to editors of its own for the same figure.
    private void DetachFigureContext()
    {
        if (FigureContext.Value is not { } lent || ReferenceEquals(lent, _ownedFigure)) return;
        if (PathFigure.Value is not { } figure || Element.Value is not { } element) return;
        if (lent.GetParentContext()?.CreateDetached(new PropertyServices(EditorContext, element)) is not { } geometry)
            return;

        EditWithOwnedEditors(geometry, figure);
    }

    // A restored tab has no property tab to lend it editors, so it builds them for the property that holds
    // the figure's geometry, as the property tab would.
    private bool RestoreFigureContext(PathFigure figure)
    {
        if (figure.HierarchicalParent is not PathGeometry pathGeometry
            || pathGeometry.HierarchicalParent is not EngineObject owner
            || owner.Properties.FirstOrDefault(p => ReferenceEquals(p.CurrentValue, pathGeometry)) is not { } property
            || pathGeometry.FindHierarchicalParent<Element>() is not { } element)
        {
            return false;
        }

        var factory = EditorContext.GetRequiredService<IPropertyEditorFactory>();
        IPropertyEditorContext? editor = factory.CreateEditor(PropertyAdapterFactory.CreateAdapter(property, owner));
        if (editor is not IGeometryEditorContext geometry)
        {
            editor?.Dispose();
            return false;
        }

        editor.Accept(new PropertyServices(EditorContext, element));
        return EditWithOwnedEditors(geometry, figure);
    }

    // Takes over `geometry`, or disposes it when it has no editor for `figure`.
    private bool EditWithOwnedEditors(IGeometryEditorContext geometry, PathFigure figure)
    {
        // Expanding creates the figure editors.
        geometry.ExpandForEditing();
        if (geometry.FindPathFigureContext(figure) is not { } owned)
        {
            (geometry as IDisposable)?.Dispose();
            return false;
        }

        owned.ExpandForEditing();
        PathSegment? selected = SelectedOperation.Value;
        _ownedGeometry = geometry;
        _ownedFigure = owned;
        FigureContext.Value = owned;
        SelectedOperation.Value = selected;
        return true;
    }

    private void ReleaseOwnedEditors()
    {
        IGeometryEditorContext? geometry = _ownedGeometry;
        _ownedGeometry = null;
        _ownedFigure = null;
        (geometry as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        if (PathFigure.Value is IHierarchical h)
            h.DetachedFromHierarchy -= OnPathFigureDetached;
        _disposables.Dispose();
        ReleaseOwnedEditors();
        ClearPointProperties();
        PointProperties.Dispose();
        FigureContext.Dispose();
    }

    private void OnPathFigureDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        FigureContext.Value = null;
    }

    public void WriteToJson(JsonObject json)
    {
        _pin.WriteToJson(json);
        if (IsPinned.Value && PathFigure.Value is { } figure)
            json[PinnedFigureJsonKey] = figure.Id;
        else
            json.Remove(PinnedFigureJsonKey);
    }

    // Only a pinned tab is saved with its figure; an unpinned one waits for a property editor to open one.
    public void ReadFromJson(JsonObject json)
    {
        if (ToolTabPin.WasPinned(json)
            && json.TryGetPropertyValueAsJsonValue(PinnedFigureJsonKey, out Guid id)
            && EditorContext.GetService<Scene>()?.FindById(id) is PathFigure figure
            && RestoreFigureContext(figure))
        {
            _pin.ReadFromJson(json);
        }
    }

    public object? GetService(Type serviceType)
    {
        return EditorContext.GetService(serviceType);
    }
}
