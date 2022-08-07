using BeUtl.Framework;
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
        public PixelPointEditorViewModel(IAbstractProperty<BeUtl.Media.PixelPoint> property)
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
        public PixelSizeEditorViewModel(IAbstractProperty<BeUtl.Media.PixelSize> property)
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
        public PointEditorViewModel(IAbstractProperty<BeUtl.Graphics.Point> property)
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
        public SizeEditorViewModel(IAbstractProperty<BeUtl.Graphics.Size> property)
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
        public VectorEditorViewModel(IAbstractProperty<BeUtl.Graphics.Vector> property)
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
        public RelativePointEditorViewModel(IAbstractProperty<BeUtl.Graphics.RelativePoint> property)
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
    public sealed class PixelRectEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelRect>
    {
        public PixelRectEditorViewModel(IAbstractProperty<BeUtl.Media.PixelRect> property)
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
        public CornerRadiusEditorViewModel(IAbstractProperty<BeUtl.Media.CornerRadius> property)
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
        public ThicknessEditorViewModel(IAbstractProperty<BeUtl.Graphics.Thickness> property)
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
        public RectEditorViewModel(IAbstractProperty<BeUtl.Graphics.Rect> property)
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
