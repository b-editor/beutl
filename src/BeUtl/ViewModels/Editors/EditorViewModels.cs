using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

#pragma warning disable IDE0001, IDE0049

namespace BeUtl.ViewModels.Editors
{
    // Vector2
    public sealed class PixelPointEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelPoint>
    {
        public PixelPointEditorViewModel(IWrappedProperty<BeUtl.Media.PixelPoint> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelPoint> Value { get; }
    }
    public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelSize>
    {
        public PixelSizeEditorViewModel(IWrappedProperty<BeUtl.Media.PixelSize> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelSize> Value { get; }
    }
    public sealed class PointEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Point>
    {
        public PointEditorViewModel(IWrappedProperty<BeUtl.Graphics.Point> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Point> Value { get; }
    }
    public sealed class SizeEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Size>
    {
        public SizeEditorViewModel(IWrappedProperty<BeUtl.Graphics.Size> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Size> Value { get; }
    }
    public sealed class VectorEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Vector>
    {
        public VectorEditorViewModel(IWrappedProperty<BeUtl.Graphics.Vector> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Vector> Value { get; }
    }
    public sealed class RelativePointEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.RelativePoint>
    {
        public RelativePointEditorViewModel(IWrappedProperty<BeUtl.Graphics.RelativePoint> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.RelativePoint> Value { get; }
    }
    public sealed class Vector2EditorViewModel : BaseEditorViewModel<System.Numerics.Vector2>
    {
        public Vector2EditorViewModel(IWrappedProperty<System.Numerics.Vector2> property)
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
        public Vector3EditorViewModel(IWrappedProperty<System.Numerics.Vector3> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector3> Value { get; }
    }

    // Vector4
    public sealed class PixelRectEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelRect>
    {
        public PixelRectEditorViewModel(IWrappedProperty<BeUtl.Media.PixelRect> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelRect> Value { get; }
    }
    public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<BeUtl.Media.CornerRadius>
    {
        public CornerRadiusEditorViewModel(IWrappedProperty<BeUtl.Media.CornerRadius> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.CornerRadius> Value { get; }
    }
    public sealed class ThicknessEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Thickness>
    {
        public ThicknessEditorViewModel(IWrappedProperty<BeUtl.Graphics.Thickness> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Thickness> Value { get; }
    }
    public sealed class RectEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Rect>
    {
        public RectEditorViewModel(IWrappedProperty<BeUtl.Graphics.Rect> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Rect> Value { get; }
    }
    public sealed class Vector4EditorViewModel : BaseEditorViewModel<System.Numerics.Vector4>
    {
        public Vector4EditorViewModel(IWrappedProperty<System.Numerics.Vector4> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector4> Value { get; }
    }
}
