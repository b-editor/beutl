using BeUtl.Services.Editors;
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

        public BeUtl.Media.PixelPoint Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Media.PixelPoint(System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelPoint Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Media.PixelPoint(System.Int32.MinValue, System.Int32.MinValue));
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

        public BeUtl.Media.PixelSize Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Media.PixelSize(System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelSize Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Media.PixelSize(System.Int32.MinValue, System.Int32.MinValue));
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

        public BeUtl.Graphics.Point Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Graphics.Point(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Point Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Graphics.Point(System.Single.MinValue, System.Single.MinValue));
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

        public BeUtl.Graphics.Size Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Graphics.Size(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Size Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Graphics.Size(System.Single.MinValue, System.Single.MinValue));
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

        public BeUtl.Graphics.Vector Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Graphics.Vector(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Vector Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Graphics.Vector(System.Single.MinValue, System.Single.MinValue));
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

        public System.Numerics.Vector2 Maximum => WrappedProperty.GetMaximumOrDefault(new System.Numerics.Vector2(System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector2 Minimum => WrappedProperty.GetMinimumOrDefault(new System.Numerics.Vector2(System.Single.MinValue, System.Single.MinValue));
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

        public System.Numerics.Vector3 Maximum => WrappedProperty.GetMaximumOrDefault(new System.Numerics.Vector3(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector3 Minimum => WrappedProperty.GetMinimumOrDefault(new System.Numerics.Vector3(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
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

        public BeUtl.Media.PixelRect Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Media.PixelRect(System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelRect Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Media.PixelRect(System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue));
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

        public BeUtl.Media.CornerRadius Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Media.CornerRadius(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Media.CornerRadius Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Media.CornerRadius(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
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

        public BeUtl.Graphics.Thickness Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Graphics.Thickness(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Thickness Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Graphics.Thickness(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
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

        public BeUtl.Graphics.Rect Maximum => WrappedProperty.GetMaximumOrDefault(new BeUtl.Graphics.Rect(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Rect Minimum => WrappedProperty.GetMinimumOrDefault(new BeUtl.Graphics.Rect(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
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

        public System.Numerics.Vector4 Maximum => WrappedProperty.GetMaximumOrDefault(new System.Numerics.Vector4(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector4 Minimum => WrappedProperty.GetMinimumOrDefault(new System.Numerics.Vector4(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }
}
