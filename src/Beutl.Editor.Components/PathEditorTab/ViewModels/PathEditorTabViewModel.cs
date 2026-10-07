using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.ViewModels;

public sealed class PathEditorTabViewModel : IDisposable, IPathEditorContext, IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorClock _clock;

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

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.PathEditor);

    // FigureContextがcontext引数と同じ場合、編集を終了
    public void StartOrFinishEdit(IPathFigureEditorContext context)
    {
        PathFigureContextChain.ToggleEditing(FigureContext, context);
    }

    public void Dispose()
    {
        if (PathFigure.Value is IHierarchical h)
            h.DetachedFromHierarchy -= OnPathFigureDetached;
        _disposables.Dispose();
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
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return EditorContext.GetService(serviceType);
    }
}
