using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PathEditorTab.ViewModels;

public sealed class PathEditorViewModel : IDisposable, IPathEditorContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorClock _clock;
    private readonly Scene _scene;

    public PathEditorViewModel(IEditorContext editorContext, IPreviewPlayer player)
    {
        EditorContext = editorContext;
        _clock = editorContext.GetRequiredService<IEditorClock>();
        _scene = editorContext.GetRequiredService<Scene>();

        SceneWidth = _scene.GetObservable(Scene.FrameSizeProperty)
            .Select(v => v.Width)
            .ToReadOnlyReactivePropertySlim()
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
        Drawable = SourceOperator.Select(v => v?.Value as Drawable)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        var drawableResource = Drawable
            .Select(d =>
                d?.SubscribeEngineVersionedResource(_clock.CurrentTime, (o, c) => o.ToResource(c))
                    .Select(t => ((Drawable.Resource, int)?)t) ??
                Observable.ReturnThenNever<(Drawable.Resource, int)?>(null))
            .Switch()
            .Publish(null).RefCount();

        GeometryResource = drawableResource
            .Select(t =>
                t is { Item1: GeometryShape.Resource { Data: PathGeometry.Resource pathGeometry } }
                    ? (pathGeometry, pathGeometry.Version)
                    : ((PathGeometry.Resource, int)?)null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Matrix = drawableResource.Select(r => r != null ? CalculateMatrix(r.Value.Item1) : Graphics.Matrix.Identity)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);
        AvaMatrix = Matrix.Select(v => v.ToAvaMatrix())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsVisible = _clock.CurrentTime
            .CombineLatest(Element
                .Select(e => e?.GetObservable(ProjectSystem.Element.StartProperty)
                    .CombineLatest(e.GetObservable(ProjectSystem.Element.LengthProperty))
                    .Select(t => new TimeRange(t.First, t.Second)) ?? Observable.ReturnThenNever<TimeRange>(default))
                .Switch())
            .Select(t => t.Second.Contains(t.First))
            .CombineLatest(player.IsPlaying, Context)
            .Select(t => t.First && !t.Second && t.Third != null)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        IsClosed = PathFigure.Select(g =>
                g?.IsClosed.SubscribeEngineProperty(g, _clock.CurrentTime) ?? Observable.ReturnThenNever(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        FigureContext.Subscribe(_ => SelectedOperation.Value = null)
            .DisposeWith(_disposables);
    }

    private Matrix CalculateMatrix(Drawable.Resource drawable)
    {
        Size frameSize = _scene.FrameSize.ToSize(1);
        Matrix matrix = Graphics.Matrix.Identity;

        // Shape.cs
        if (drawable is GeometryShape.Resource { Data: PathGeometry.Resource geometry } shape)
        {
            var requestedSize = new Size(shape.Width, shape.Height);
            Rect shapeBounds = geometry.Bounds;
            Vector scale = Shape.CalculateScale(requestedSize, shapeBounds, shape.Stretch);
            //matrix = Graphics.Matrix.CreateTranslation(-shapeBounds.Position);
            Size size = shapeBounds.Size * scale;

            if (shape.Pen != null)
            {
                float thickness = PenHelper.GetRealThickness(shape.Pen.StrokeAlignment, shape.Pen.Thickness);
                size = size.Inflate(thickness);

                matrix *= Graphics.Matrix.CreateTranslation(thickness, thickness);
            }

            matrix *= Graphics.Matrix.CreateScale(scale);

            Matrix mat = drawable.GetOriginal().GetTransformMatrix(frameSize, size, drawable);
            matrix *= mat;
        }

        return matrix;
    }

    public IEditorContext EditorContext { get; }

    public IReactiveProperty<IPathFigureEditorContext?> FigureContext { get; } =
        new ReactiveProperty<IPathFigureEditorContext?>();

    public ReadOnlyReactivePropertySlim<IGeometryEditorContext?> Context { get; }

    public ReadOnlyReactivePropertySlim<Geometry?> Geometry { get; }

    public IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    public IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    public IReadOnlyReactiveProperty<Element?> Element { get; }

    public ReadOnlyReactivePropertySlim<IPublishOperator?> SourceOperator { get; }

    public ReadOnlyReactivePropertySlim<Drawable?> Drawable { get; }

    public ReadOnlyReactivePropertySlim<(PathGeometry.Resource Resource, int Version)?> GeometryResource { get; }

    public ReadOnlyReactiveProperty<Matrix> Matrix { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Matrix> AvaMatrix { get; }

    public ReadOnlyReactivePropertySlim<int> SceneWidth { get; }

    public IReactiveProperty<PathSegment?> SelectedOperation { get; } = new ReactiveProperty<PathSegment?>();

    public ReadOnlyReactiveProperty<bool> IsVisible { get; }

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public IReactiveProperty<bool> Symmetry { get; } = new ReactiveProperty<bool>(true);

    public IReactiveProperty<bool> Asymmetry { get; } = new ReactiveProperty<bool>(false);

    public IReactiveProperty<bool> Separately { get; } = new ReactiveProperty<bool>(false);

    public void StartEdit(Shape shape, IGeometryEditorContext context, Avalonia.Point point)
    {
        // Groupプロパティを初期化
        context.ExpandForEditing();

        var shapeResource = shape.ToResource(new RenderContext(_clock.CurrentTime.Value));
        Avalonia.Matrix matrix = CalculateMatrix(shapeResource).ToAvaMatrix();
        if (matrix.TryInvert(out Avalonia.Matrix inverted)
            && shapeResource is GeometryShape.Resource { Data: not null } geometryShapeResource
            && context.Value.Value is PathGeometry geometry)
        {
            point = inverted.Transform(point);
            PathFigure.Resource? figure = geometry.HitTestFigure(
                point.ToBtlPoint(), geometryShapeResource.Pen, geometryShapeResource.Data);
            if (figure != null)
            {
                var figContext = context.FindPathFigureContext(figure.GetOriginal());
                if (figContext != null)
                {
                    StartEdit(figContext);
                }
            }
        }
    }

    public void StartEdit(IPathFigureEditorContext context)
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
}
