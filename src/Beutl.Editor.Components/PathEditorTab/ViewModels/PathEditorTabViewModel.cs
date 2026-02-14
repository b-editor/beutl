using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

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

        Context = FigureContext.Select(v => v?.GetParentContext() ?? null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Geometry = Context.Select(v => v?.Value ?? Observable.ReturnThenNever<Geometry?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PathGeometry = Geometry
            .Select(v => v as PathGeometry)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        PathFigure = FigureContext.Select(v => v?.Value ?? Observable.ReturnThenNever<PathFigure?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Element = Context.Select(v => v?.GetService<Element>())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        SourceOperator = Context.Select(v => v?.GetService<SourceOperator>() as IPublishOperator)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        GeometryResource = PathGeometry
            .Select(d =>
                d?.SubscribeEngineVersionedResource(_clock.CurrentTime, (o, c) => o.ToResource(c))
                    .Select(t => ((PathGeometry.Resource, int)?)t) ??
                Observable.ReturnThenNever<(PathGeometry.Resource, int)?>(null))
            .Switch()
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
    }

    public IEditorContext EditorContext { get; }

    public IReactiveProperty<IPathFigureEditorContext?> FigureContext { get; } =
        new ReactiveProperty<IPathFigureEditorContext?>();

    public ReadOnlyReactivePropertySlim<IGeometryEditorContext?> Context { get; }

    public ReadOnlyReactivePropertySlim<Geometry?> Geometry { get; }

    public ReadOnlyReactivePropertySlim<(PathGeometry.Resource, int)?> GeometryResource { get; }

    public IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    public IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    public IReadOnlyReactiveProperty<Element?> Element { get; }

    public ReadOnlyReactivePropertySlim<IPublishOperator?> SourceOperator { get; }

    public IReactiveProperty<PathSegment?> SelectedOperation { get; } = new ReactiveProperty<PathSegment?>();

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public ReadOnlyReactiveProperty<bool> IsPlaying { get; }

    public IReactiveProperty<bool> Symmetry { get; } = new ReactiveProperty<bool>(true);

    public IReactiveProperty<bool> Asymmetry { get; } = new ReactiveProperty<bool>(false);

    public IReactiveProperty<bool> Separately { get; } = new ReactiveProperty<bool>(false);

    public ToolTabExtension Extension { get; } = PathEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.PathEditor;

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    // FigureContextがcontext引数と同じ場合、編集を終了
    public void StartOrFinishEdit(IPathFigureEditorContext context)
    {
        if (FigureContext.Value == context)
        {
            FigureContext.Value = null;
            context.CollapseEditedOperations();
        }
        else
        {
            context.ExpandForEditing();
            FigureContext.Value = context;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        FigureContext.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
