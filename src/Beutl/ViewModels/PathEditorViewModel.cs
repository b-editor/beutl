using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class PathEditorViewModel : IDisposable, IPathEditorViewModel
{
    private readonly CompositeDisposable _disposables = [];

    public PathEditorViewModel(EditViewModel editViewModel, PlayerViewModel playerViewModel)
    {
        EditViewModel = editViewModel;
        PlayerViewModel = playerViewModel;
        SceneWidth = editViewModel.Scene.GetObservable(Scene.FrameSizeProperty)
            .Select(v => v.Width)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Context = FigureContext.Select(v => v?.ParentContext ?? Observable.Return<GeometryEditorViewModel?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PathGeometry = Context.Select(v => v?.Value ?? Observable.Return<Geometry?>(null))
            .Switch()
            .Select(v => v as PathGeometry)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        PathFigure = FigureContext.Select(v => v?.Value ?? Observable.Return<PathFigure?>(null))
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
                d?.SubscribeEngineVersionedResource(EditViewModel.CurrentTime, (o, c) => o.ToResource(c))
                    .Select(t => ((Drawable.Resource, int)?)t) ??
                Observable.Return<(Drawable.Resource, int)?>(null))
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

        IsVisible = editViewModel.CurrentTime
            .CombineLatest(Element
                .Select(e => e?.GetObservable(ProjectSystem.Element.StartProperty)
                    .CombineLatest(e.GetObservable(ProjectSystem.Element.LengthProperty))
                    .Select(t => new TimeRange(t.First, t.Second)) ?? Observable.Return<TimeRange>(default))
                .Switch())
            .Select(t => t.Second.Contains(t.First))
            .CombineLatest(PlayerViewModel.IsPlaying, Context)
            .Select(t => t.First && !t.Second && t.Third != null)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        IsClosed = PathFigure.Select(g =>
                g?.IsClosed.SubscribeEngineProperty(g, EditViewModel.CurrentTime) ?? Observable.Return(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        FigureContext.Subscribe(_ => SelectedOperation.Value = null)
            .DisposeWith(_disposables);
    }

    private Matrix CalculateMatrix(Drawable.Resource drawable)
    {
        Size frameSize = EditViewModel.Scene.FrameSize.ToSize(1);
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

    public EditViewModel EditViewModel { get; }

    public PlayerViewModel PlayerViewModel { get; }

    public IReactiveProperty<PathFigureEditorViewModel?> FigureContext { get; } =
        new ReactiveProperty<PathFigureEditorViewModel?>();

    public ReadOnlyReactivePropertySlim<GeometryEditorViewModel?> Context { get; }

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

    public void StartEdit(Shape shape, GeometryEditorViewModel context, Avalonia.Point point)
    {
        // Groupプロパティを初期化
        if (!context.IsExpanded.Value)
        {
            context.IsExpanded.Value = true;
        }

        var shapeResource = shape.ToResource(new RenderContext(EditViewModel.CurrentTime.Value));
        Avalonia.Matrix matrix = CalculateMatrix(shapeResource).ToAvaMatrix();
        if (matrix.TryInvert(out Avalonia.Matrix inverted)
            && shapeResource is GeometryShape.Resource { Data: not null } geometryShapeResource
            && context.Value.Value is PathGeometry geometry
            && context.Group.Value is { } group)
        {
            point = inverted.Transform(point);
            PathFigure.Resource? figure = geometry.HitTestFigure(
                point.ToBtlPoint(), geometryShapeResource.Pen, geometryShapeResource.Data);
            if (figure != null
                && group.Items.FirstOrDefault(v =>
                        v.Context is PathFigureEditorViewModel f && f.Value.Value == figure.GetOriginal())
                    ?.Context is PathFigureEditorViewModel figContext)
            {
                StartEdit(figContext);
            }
        }
    }

    public void StartEdit(PathFigureEditorViewModel context)
    {
        if (FigureContext.Value == context)
        {
            FigureContext.Value = null;
            ListEditorViewModel<PathSegment>? group = context.Group.Value;
            if (group != null)
            {
                foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
                {
                    if (item.Context is PathOperationEditorViewModel opEditor
                        && opEditor.ProgrammaticallyExpanded)
                    {
                        opEditor.IsExpanded.Value = false;
                    }
                }
            }
        }
        else
        {
            // Groupプロパティを初期化
            if (!context.IsExpanded.Value)
            {
                context.IsExpanded.Value = true;
            }

            FigureContext.Value = context;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        FigureContext.Dispose();
    }
}
