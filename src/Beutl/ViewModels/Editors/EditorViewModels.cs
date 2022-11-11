using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors
{
    // Vector2
    public sealed class PixelPointEditorViewModel : BaseEditorViewModel<Media.PixelPoint>
    {
        public PixelPointEditorViewModel(IAbstractProperty<Media.PixelPoint> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Media.PixelPoint> Value { get; }
    }
    public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<Media.PixelSize>
    {
        public PixelSizeEditorViewModel(IAbstractProperty<Media.PixelSize> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Media.PixelSize> Value { get; }
    }
    public sealed class PointEditorViewModel : BaseEditorViewModel<Graphics.Point>
    {
        public PointEditorViewModel(IAbstractProperty<Graphics.Point> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Graphics.Point> Value { get; }
    }
    public sealed class SizeEditorViewModel : BaseEditorViewModel<Graphics.Size>
    {
        public SizeEditorViewModel(IAbstractProperty<Graphics.Size> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Graphics.Size> Value { get; }
    }
    public sealed class VectorEditorViewModel : BaseEditorViewModel<Graphics.Vector>
    {
        public VectorEditorViewModel(IAbstractProperty<Graphics.Vector> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Graphics.Vector> Value { get; }
    }
    public sealed class RelativePointEditorViewModel : BaseEditorViewModel<Graphics.RelativePoint>
    {
        public RelativePointEditorViewModel(IAbstractProperty<Graphics.RelativePoint> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Graphics.RelativePoint> Value { get; }
    }
    public sealed class Vector2EditorViewModel : BaseEditorViewModel<System.Numerics.Vector2>
    {
        public Vector2EditorViewModel(IAbstractProperty<System.Numerics.Vector2> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector2> Value { get; }
    }

    // Vector3
    public sealed class Vector3EditorViewModel : BaseEditorViewModel<System.Numerics.Vector3>
    {
        public Vector3EditorViewModel(IAbstractProperty<System.Numerics.Vector3> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector3> Value { get; }
    }

    // Vector4
    public sealed class PixelRectEditorViewModel : BaseEditorViewModel<Beutl.Media.PixelRect>
    {
        public PixelRectEditorViewModel(IAbstractProperty<Beutl.Media.PixelRect> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Beutl.Media.PixelRect> Value { get; }
    }
    public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<Beutl.Media.CornerRadius>
    {
        public CornerRadiusEditorViewModel(IAbstractProperty<Beutl.Media.CornerRadius> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Beutl.Media.CornerRadius> Value { get; }
    }
    public sealed class ThicknessEditorViewModel : BaseEditorViewModel<Beutl.Graphics.Thickness>
    {
        public ThicknessEditorViewModel(IAbstractProperty<Beutl.Graphics.Thickness> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Beutl.Graphics.Thickness> Value { get; }
    }
    public sealed class RectEditorViewModel : BaseEditorViewModel<Beutl.Graphics.Rect>
    {
        public RectEditorViewModel(IAbstractProperty<Beutl.Graphics.Rect> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<Beutl.Graphics.Rect> Value { get; }
    }
    public sealed class Vector4EditorViewModel : BaseEditorViewModel<System.Numerics.Vector4>
    {
        public Vector4EditorViewModel(IAbstractProperty<System.Numerics.Vector4> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector4> Value { get; }
    }
}
