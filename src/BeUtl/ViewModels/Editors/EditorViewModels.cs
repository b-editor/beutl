using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

#pragma warning disable IDE0001, IDE0049

namespace BeUtl.ViewModels.Editors
{
    // Number
    public sealed class ByteEditorViewModel : BaseNumberEditorViewModel<System.Byte>
    {
        public ByteEditorViewModel(IWrappedProperty<System.Byte> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Byte> Value { get; }

        public override System.Byte Maximum => WrappedProperty.GetMaximumOrDefault(System.Byte.MaxValue);

        public override System.Byte Minimum => WrappedProperty.GetMinimumOrDefault(System.Byte.MinValue);

        public override INumberEditorService<System.Byte> EditorService { get; } = NumberEditorService.Instance.Get<System.Byte>();
    }
    public sealed class DecimalEditorViewModel : BaseNumberEditorViewModel<System.Decimal>
    {
        public DecimalEditorViewModel(IWrappedProperty<System.Decimal> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Decimal> Value { get; }

        public override System.Decimal Maximum => WrappedProperty.GetMaximumOrDefault(System.Decimal.MaxValue);

        public override System.Decimal Minimum => WrappedProperty.GetMinimumOrDefault(System.Decimal.MinValue);

        public override INumberEditorService<System.Decimal> EditorService { get; } = NumberEditorService.Instance.Get<System.Decimal>();
    }
    public sealed class DoubleEditorViewModel : BaseNumberEditorViewModel<System.Double>
    {
        public DoubleEditorViewModel(IWrappedProperty<System.Double> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Double> Value { get; }

        public override System.Double Maximum => WrappedProperty.GetMaximumOrDefault(System.Double.MaxValue);

        public override System.Double Minimum => WrappedProperty.GetMinimumOrDefault(System.Double.MinValue);

        public override INumberEditorService<System.Double> EditorService { get; } = NumberEditorService.Instance.Get<System.Double>();
    }
    public sealed class SingleEditorViewModel : BaseNumberEditorViewModel<System.Single>
    {
        public SingleEditorViewModel(IWrappedProperty<System.Single> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Single> Value { get; }

        public override System.Single Maximum => WrappedProperty.GetMaximumOrDefault(System.Single.MaxValue);

        public override System.Single Minimum => WrappedProperty.GetMinimumOrDefault(System.Single.MinValue);

        public override INumberEditorService<System.Single> EditorService { get; } = NumberEditorService.Instance.Get<System.Single>();
    }
    public sealed class Int16EditorViewModel : BaseNumberEditorViewModel<System.Int16>
    {
        public Int16EditorViewModel(IWrappedProperty<System.Int16> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int16> Value { get; }

        public override System.Int16 Maximum => WrappedProperty.GetMaximumOrDefault(System.Int16.MaxValue);

        public override System.Int16 Minimum => WrappedProperty.GetMinimumOrDefault(System.Int16.MinValue);

        public override INumberEditorService<System.Int16> EditorService { get; } = NumberEditorService.Instance.Get<System.Int16>();
    }
    public sealed class Int32EditorViewModel : BaseNumberEditorViewModel<System.Int32>
    {
        public Int32EditorViewModel(IWrappedProperty<System.Int32> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int32> Value { get; }

        public override System.Int32 Maximum => WrappedProperty.GetMaximumOrDefault(System.Int32.MaxValue);

        public override System.Int32 Minimum => WrappedProperty.GetMinimumOrDefault(System.Int32.MinValue);

        public override INumberEditorService<System.Int32> EditorService { get; } = NumberEditorService.Instance.Get<System.Int32>();
    }
    public sealed class Int64EditorViewModel : BaseNumberEditorViewModel<System.Int64>
    {
        public Int64EditorViewModel(IWrappedProperty<System.Int64> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int64> Value { get; }

        public override System.Int64 Maximum => WrappedProperty.GetMaximumOrDefault(System.Int64.MaxValue);

        public override System.Int64 Minimum => WrappedProperty.GetMinimumOrDefault(System.Int64.MinValue);

        public override INumberEditorService<System.Int64> EditorService { get; } = NumberEditorService.Instance.Get<System.Int64>();
    }
    public sealed class SByteEditorViewModel : BaseNumberEditorViewModel<System.SByte>
    {
        public SByteEditorViewModel(IWrappedProperty<System.SByte> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.SByte> Value { get; }

        public override System.SByte Maximum => WrappedProperty.GetMaximumOrDefault(System.SByte.MaxValue);

        public override System.SByte Minimum => WrappedProperty.GetMinimumOrDefault(System.SByte.MinValue);

        public override INumberEditorService<System.SByte> EditorService { get; } = NumberEditorService.Instance.Get<System.SByte>();
    }
    public sealed class UInt16EditorViewModel : BaseNumberEditorViewModel<System.UInt16>
    {
        public UInt16EditorViewModel(IWrappedProperty<System.UInt16> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt16> Value { get; }

        public override System.UInt16 Maximum => WrappedProperty.GetMaximumOrDefault(System.UInt16.MaxValue);

        public override System.UInt16 Minimum => WrappedProperty.GetMinimumOrDefault(System.UInt16.MinValue);

        public override INumberEditorService<System.UInt16> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt16>();
    }
    public sealed class UInt32EditorViewModel : BaseNumberEditorViewModel<System.UInt32>
    {
        public UInt32EditorViewModel(IWrappedProperty<System.UInt32> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt32> Value { get; }

        public override System.UInt32 Maximum => WrappedProperty.GetMaximumOrDefault(System.UInt32.MaxValue);

        public override System.UInt32 Minimum => WrappedProperty.GetMinimumOrDefault(System.UInt32.MinValue);

        public override INumberEditorService<System.UInt32> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt32>();
    }
    public sealed class UInt64EditorViewModel : BaseNumberEditorViewModel<System.UInt64>
    {
        public UInt64EditorViewModel(IWrappedProperty<System.UInt64> property)
            : base(property)
        {
            Value = property.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt64> Value { get; }

        public override System.UInt64 Maximum => WrappedProperty.GetMaximumOrDefault(System.UInt64.MaxValue);

        public override System.UInt64 Minimum => WrappedProperty.GetMinimumOrDefault(System.UInt64.MinValue);

        public override INumberEditorService<System.UInt64> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt64>();
    }

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
