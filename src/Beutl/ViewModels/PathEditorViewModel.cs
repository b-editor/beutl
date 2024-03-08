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

        PathGeometry = Context.Select(v => v?.Value ?? Observable.Return<Geometry?>(null))
            .Switch()
            .Select(v => v as PathGeometry)
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
                    .Select(_ => CalculateMatrix(d))
                    .Publish(CalculateMatrix(d)).RefCount()
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
                Context.Value = null;
            }
        }).DisposeWith(_disposables);

        IsVisible = editViewModel.CurrentTime
            .CombineLatest(Element
                .Select(e => e?.GetObservable(ProjectSystem.Element.StartProperty)
                    .CombineLatest(e.GetObservable(ProjectSystem.Element.LengthProperty))
                    .Select(t => new TimeRange(t.First, t.Second)) ?? Observable.Return<TimeRange>(default))
                .Switch())
            .Select(t => t.Second.Contains(t.First))
            .CombineLatest(PlayerViewModel.IsPlaying)
            .Select(t => t.First && !t.Second)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        IsClosed = PathGeometry.Select(g => g?.GetObservable(Media.PathGeometry.IsClosedProperty) ?? Observable.Return(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);
    }

    private Matrix CalculateMatrix(Drawable drawable)
    {
        Size frameSize = EditViewModel.Scene.FrameSize.ToSize(1);
        Matrix matrix = Graphics.Matrix.Identity;

        // Shape.cs
        if (drawable is Shape shape && PathGeometry.Value is { } geometry)
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

    public ReactiveProperty<GeometryEditorViewModel?> Context { get; } = new();

    public ReadOnlyReactivePropertySlim<PathGeometry?> PathGeometry { get; }

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

    public void StartEdit(GeometryEditorViewModel context)
    {
        if (Context.Value == context)
        {
            Context.Value = null;
        }
        else
        {
            Context.Value = context;
        }

        SelectedOperation.Value = null;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Context.Dispose();
    }
}
