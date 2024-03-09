using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.ViewModels.Editors;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class PathEditorViewModel : IDisposable
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
            .Select(v => v as PathFigure)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Element = Context.Select(v => v?.GetService<Element>())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        SourceOperator = Context.Select(v => v?.GetService<SourceOperator>() as StyledSourcePublisher)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Drawable = SourceOperator.Select(v => v?.Instance?.Target as Drawable)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Matrix = Drawable
            .Select(d => d != null
                ? Observable.FromEventPattern<RenderInvalidatedEventArgs>(h => d.Invalidated += h, h => d.Invalidated -= h)
                    .Select(_ => CalculateMatrix(d, PathGeometry.Value))
                    .Publish(CalculateMatrix(d, PathGeometry.Value)).RefCount()
                : Observable.Return(Graphics.Matrix.Identity))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);
        AvaMatrix = Matrix.Select(v => v.ToAvaMatrix())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        editViewModel.SelectedObject.Subscribe(obj =>
        {
            if (!ReferenceEquals(obj, Element.Value))
            {
                FigureContext.Value = null;
            }
        }).DisposeWith(_disposables);

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

        IsClosed = PathFigure.Select(g => g?.GetObservable(Media.PathFigure.IsClosedProperty) ?? Observable.Return(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);
    }

    private Matrix CalculateMatrix(Drawable drawable, Geometry? geometry)
    {
        Size frameSize = EditViewModel.Scene.FrameSize.ToSize(1);
        Matrix matrix = Graphics.Matrix.Identity;

        // Shape.cs
        if (drawable is Shape shape && geometry != null)
        {
            var requestedSize = new Size(shape.Width, shape.Height);
            Rect shapeBounds = geometry.GetCurrentBounds();
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

            Matrix mat = drawable.GetTransformMatrix(frameSize, size);
            matrix *= mat;
        }

        return matrix;
    }

    public EditViewModel EditViewModel { get; }

    public PlayerViewModel PlayerViewModel { get; }

    public ReactiveProperty<PathFigureEditorViewModel?> FigureContext { get; } = new();

    public ReadOnlyReactivePropertySlim<GeometryEditorViewModel?> Context { get; }

    public ReadOnlyReactivePropertySlim<PathGeometry?> PathGeometry { get; }

    public ReadOnlyReactivePropertySlim<PathFigure?> PathFigure { get; }

    public ReadOnlyReactivePropertySlim<Element?> Element { get; }

    public ReadOnlyReactivePropertySlim<StyledSourcePublisher?> SourceOperator { get; }

    public ReadOnlyReactivePropertySlim<Drawable?> Drawable { get; }

    public ReadOnlyReactiveProperty<Matrix> Matrix { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Matrix> AvaMatrix { get; }

    public ReadOnlyReactivePropertySlim<int> SceneWidth { get; }

    public ReactiveProperty<PathSegment?> SelectedOperation { get; } = new();

    public ReadOnlyReactiveProperty<bool> IsVisible { get; }

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public ReactiveProperty<bool> Symmetry { get; } = new(true);

    public ReactiveProperty<bool> Asymmetry { get; } = new(false);

    public ReactiveProperty<bool> Separately { get; } = new(false);

    public void StartEdit(Shape shape, GeometryEditorViewModel context, Avalonia.Point point)
    {
        Avalonia.Matrix matrix = CalculateMatrix(shape, context.Value.Value).ToAvaMatrix();
        if (matrix.TryInvert(out Avalonia.Matrix inverted)
            && context.Value.Value is PathGeometry geometry
            && context.Group.Value is { } group)
        {
            point = inverted.Transform(point);
            PathFigure? figure = geometry.HitTestFigure(point.ToBtlPoint(), shape.Pen);
            if (figure != null)
            {
                var figContext = group.Items.FirstOrDefault(v => v.Context is PathFigureEditorViewModel f && f.Value.Value == figure)
                    ?.Context as PathFigureEditorViewModel;
                if (figContext != null)
                {
                    StartEdit(figContext);
                }
            }
        }
    }

    public void StartEdit(PathFigureEditorViewModel context)
    {
        if (FigureContext.Value == context)
        {
            FigureContext.Value = null;
            var group = context.Group.Value;
            if (group != null)
            {
                foreach (var item in group.Items)
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
            FigureContext.Value = context;
        }

        SelectedOperation.Value = null;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        FigureContext.Dispose();
    }
}
