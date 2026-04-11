using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Media;
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

    public ReadOnlyReactivePropertySlim<Geometry?> Geometry { get; }

    public ReadOnlyReactivePropertySlim<(PathGeometry.Resource, int)?> GeometryResource { get; }

    public IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    public IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    public IReadOnlyReactiveProperty<Element?> Element { get; }

    public IReactiveProperty<PathSegment?> SelectedOperation { get; } = new ReactiveProperty<PathSegment?>();

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public ReadOnlyReactiveProperty<bool> IsPlaying { get; }

    public IReactiveProperty<bool> Symmetry { get; } = new ReactiveProperty<bool>(true);

    public IReactiveProperty<bool> Asymmetry { get; } = new ReactiveProperty<bool>(false);

    public IReactiveProperty<bool> Separately { get; } = new ReactiveProperty<bool>(false);

    public ToolTabExtension Extension { get; } = PathEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.PathEditor;

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
        if (PathFigure.Value is IHierarchical h)
            h.DetachedFromHierarchy -= OnPathFigureDetached;
        _disposables.Dispose();
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
