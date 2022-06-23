using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

#pragma warning disable IDE0001, IDE0049

namespace BeUtl.ViewModels.Editors
{
    // Number
    public sealed class ByteEditorViewModel : BaseNumberEditorViewModel<System.Byte>
    {
        public ByteEditorViewModel(PropertyInstance<System.Byte> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Byte> Value { get; }

        public override System.Byte Maximum => Setter.GetMaximumOrDefault(System.Byte.MaxValue);

        public override System.Byte Minimum => Setter.GetMinimumOrDefault(System.Byte.MinValue);

        public override INumberEditorService<System.Byte> EditorService { get; } = NumberEditorService.Instance.Get<System.Byte>();
    }
    public sealed class DecimalEditorViewModel : BaseNumberEditorViewModel<System.Decimal>
    {
        public DecimalEditorViewModel(PropertyInstance<System.Decimal> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Decimal> Value { get; }

        public override System.Decimal Maximum => Setter.GetMaximumOrDefault(System.Decimal.MaxValue);

        public override System.Decimal Minimum => Setter.GetMinimumOrDefault(System.Decimal.MinValue);

        public override INumberEditorService<System.Decimal> EditorService { get; } = NumberEditorService.Instance.Get<System.Decimal>();
    }
    public sealed class DoubleEditorViewModel : BaseNumberEditorViewModel<System.Double>
    {
        public DoubleEditorViewModel(PropertyInstance<System.Double> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Double> Value { get; }

        public override System.Double Maximum => Setter.GetMaximumOrDefault(System.Double.MaxValue);

        public override System.Double Minimum => Setter.GetMinimumOrDefault(System.Double.MinValue);

        public override INumberEditorService<System.Double> EditorService { get; } = NumberEditorService.Instance.Get<System.Double>();
    }
    public sealed class SingleEditorViewModel : BaseNumberEditorViewModel<System.Single>
    {
        public SingleEditorViewModel(PropertyInstance<System.Single> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Single> Value { get; }

        public override System.Single Maximum => Setter.GetMaximumOrDefault(System.Single.MaxValue);

        public override System.Single Minimum => Setter.GetMinimumOrDefault(System.Single.MinValue);

        public override INumberEditorService<System.Single> EditorService { get; } = NumberEditorService.Instance.Get<System.Single>();
    }
    public sealed class Int16EditorViewModel : BaseNumberEditorViewModel<System.Int16>
    {
        public Int16EditorViewModel(PropertyInstance<System.Int16> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int16> Value { get; }

        public override System.Int16 Maximum => Setter.GetMaximumOrDefault(System.Int16.MaxValue);

        public override System.Int16 Minimum => Setter.GetMinimumOrDefault(System.Int16.MinValue);

        public override INumberEditorService<System.Int16> EditorService { get; } = NumberEditorService.Instance.Get<System.Int16>();
    }
    public sealed class Int32EditorViewModel : BaseNumberEditorViewModel<System.Int32>
    {
        public Int32EditorViewModel(PropertyInstance<System.Int32> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int32> Value { get; }

        public override System.Int32 Maximum => Setter.GetMaximumOrDefault(System.Int32.MaxValue);

        public override System.Int32 Minimum => Setter.GetMinimumOrDefault(System.Int32.MinValue);

        public override INumberEditorService<System.Int32> EditorService { get; } = NumberEditorService.Instance.Get<System.Int32>();
    }
    public sealed class Int64EditorViewModel : BaseNumberEditorViewModel<System.Int64>
    {
        public Int64EditorViewModel(PropertyInstance<System.Int64> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Int64> Value { get; }

        public override System.Int64 Maximum => Setter.GetMaximumOrDefault(System.Int64.MaxValue);

        public override System.Int64 Minimum => Setter.GetMinimumOrDefault(System.Int64.MinValue);

        public override INumberEditorService<System.Int64> EditorService { get; } = NumberEditorService.Instance.Get<System.Int64>();
    }
    public sealed class SByteEditorViewModel : BaseNumberEditorViewModel<System.SByte>
    {
        public SByteEditorViewModel(PropertyInstance<System.SByte> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.SByte> Value { get; }

        public override System.SByte Maximum => Setter.GetMaximumOrDefault(System.SByte.MaxValue);

        public override System.SByte Minimum => Setter.GetMinimumOrDefault(System.SByte.MinValue);

        public override INumberEditorService<System.SByte> EditorService { get; } = NumberEditorService.Instance.Get<System.SByte>();
    }
    public sealed class UInt16EditorViewModel : BaseNumberEditorViewModel<System.UInt16>
    {
        public UInt16EditorViewModel(PropertyInstance<System.UInt16> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt16> Value { get; }

        public override System.UInt16 Maximum => Setter.GetMaximumOrDefault(System.UInt16.MaxValue);

        public override System.UInt16 Minimum => Setter.GetMinimumOrDefault(System.UInt16.MinValue);

        public override INumberEditorService<System.UInt16> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt16>();
    }
    public sealed class UInt32EditorViewModel : BaseNumberEditorViewModel<System.UInt32>
    {
        public UInt32EditorViewModel(PropertyInstance<System.UInt32> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt32> Value { get; }

        public override System.UInt32 Maximum => Setter.GetMaximumOrDefault(System.UInt32.MaxValue);

        public override System.UInt32 Minimum => Setter.GetMinimumOrDefault(System.UInt32.MinValue);

        public override INumberEditorService<System.UInt32> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt32>();
    }
    public sealed class UInt64EditorViewModel : BaseNumberEditorViewModel<System.UInt64>
    {
        public UInt64EditorViewModel(PropertyInstance<System.UInt64> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.UInt64> Value { get; }

        public override System.UInt64 Maximum => Setter.GetMaximumOrDefault(System.UInt64.MaxValue);

        public override System.UInt64 Minimum => Setter.GetMinimumOrDefault(System.UInt64.MinValue);

        public override INumberEditorService<System.UInt64> EditorService { get; } = NumberEditorService.Instance.Get<System.UInt64>();
    }

    // Vector2
    public sealed class PixelPointEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelPoint>
    {
        public PixelPointEditorViewModel(PropertyInstance<BeUtl.Media.PixelPoint> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelPoint> Value { get; }

        public BeUtl.Media.PixelPoint Maximum => Setter.GetMaximumOrDefault(new BeUtl.Media.PixelPoint(System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelPoint Minimum => Setter.GetMinimumOrDefault(new BeUtl.Media.PixelPoint(System.Int32.MinValue, System.Int32.MinValue));
    }
    public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelSize>
    {
        public PixelSizeEditorViewModel(PropertyInstance<BeUtl.Media.PixelSize> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelSize> Value { get; }

        public BeUtl.Media.PixelSize Maximum => Setter.GetMaximumOrDefault(new BeUtl.Media.PixelSize(System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelSize Minimum => Setter.GetMinimumOrDefault(new BeUtl.Media.PixelSize(System.Int32.MinValue, System.Int32.MinValue));
    }
    public sealed class PointEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Point>
    {
        public PointEditorViewModel(PropertyInstance<BeUtl.Graphics.Point> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Point> Value { get; }

        public BeUtl.Graphics.Point Maximum => Setter.GetMaximumOrDefault(new BeUtl.Graphics.Point(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Point Minimum => Setter.GetMinimumOrDefault(new BeUtl.Graphics.Point(System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class SizeEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Size>
    {
        public SizeEditorViewModel(PropertyInstance<BeUtl.Graphics.Size> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Size> Value { get; }

        public BeUtl.Graphics.Size Maximum => Setter.GetMaximumOrDefault(new BeUtl.Graphics.Size(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Size Minimum => Setter.GetMinimumOrDefault(new BeUtl.Graphics.Size(System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class VectorEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Vector>
    {
        public VectorEditorViewModel(PropertyInstance<BeUtl.Graphics.Vector> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Vector> Value { get; }

        public BeUtl.Graphics.Vector Maximum => Setter.GetMaximumOrDefault(new BeUtl.Graphics.Vector(System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Vector Minimum => Setter.GetMinimumOrDefault(new BeUtl.Graphics.Vector(System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class Vector2EditorViewModel : BaseEditorViewModel<System.Numerics.Vector2>
    {
        public Vector2EditorViewModel(PropertyInstance<System.Numerics.Vector2> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector2> Value { get; }

        public System.Numerics.Vector2 Maximum => Setter.GetMaximumOrDefault(new System.Numerics.Vector2(System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector2 Minimum => Setter.GetMinimumOrDefault(new System.Numerics.Vector2(System.Single.MinValue, System.Single.MinValue));
    }

    // Vector3
    public sealed class Vector3EditorViewModel : BaseEditorViewModel<System.Numerics.Vector3>
    {
        public Vector3EditorViewModel(PropertyInstance<System.Numerics.Vector3> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector3> Value { get; }

        public System.Numerics.Vector3 Maximum => Setter.GetMaximumOrDefault(new System.Numerics.Vector3(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector3 Minimum => Setter.GetMinimumOrDefault(new System.Numerics.Vector3(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }

    // Vector4
    public sealed class PixelRectEditorViewModel : BaseEditorViewModel<BeUtl.Media.PixelRect>
    {
        public PixelRectEditorViewModel(PropertyInstance<BeUtl.Media.PixelRect> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.PixelRect> Value { get; }

        public BeUtl.Media.PixelRect Maximum => Setter.GetMaximumOrDefault(new BeUtl.Media.PixelRect(System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue, System.Int32.MaxValue));

        public BeUtl.Media.PixelRect Minimum => Setter.GetMinimumOrDefault(new BeUtl.Media.PixelRect(System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue, System.Int32.MinValue));
    }
    public sealed class CornerRadiusEditorViewModel : BaseEditorViewModel<BeUtl.Media.CornerRadius>
    {
        public CornerRadiusEditorViewModel(PropertyInstance<BeUtl.Media.CornerRadius> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Media.CornerRadius> Value { get; }

        public BeUtl.Media.CornerRadius Maximum => Setter.GetMaximumOrDefault(new BeUtl.Media.CornerRadius(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Media.CornerRadius Minimum => Setter.GetMinimumOrDefault(new BeUtl.Media.CornerRadius(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class ThicknessEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Thickness>
    {
        public ThicknessEditorViewModel(PropertyInstance<BeUtl.Graphics.Thickness> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Thickness> Value { get; }

        public BeUtl.Graphics.Thickness Maximum => Setter.GetMaximumOrDefault(new BeUtl.Graphics.Thickness(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Thickness Minimum => Setter.GetMinimumOrDefault(new BeUtl.Graphics.Thickness(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class RectEditorViewModel : BaseEditorViewModel<BeUtl.Graphics.Rect>
    {
        public RectEditorViewModel(PropertyInstance<BeUtl.Graphics.Rect> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<BeUtl.Graphics.Rect> Value { get; }

        public BeUtl.Graphics.Rect Maximum => Setter.GetMaximumOrDefault(new BeUtl.Graphics.Rect(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public BeUtl.Graphics.Rect Minimum => Setter.GetMinimumOrDefault(new BeUtl.Graphics.Rect(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }
    public sealed class Vector4EditorViewModel : BaseEditorViewModel<System.Numerics.Vector4>
    {
        public Vector4EditorViewModel(PropertyInstance<System.Numerics.Vector4> pi)
            : base(pi)
        {
            Value = pi.GetObservable()
                .ToReadOnlyReactivePropertySlim()
                .AddTo(Disposables);
        }

        public ReadOnlyReactivePropertySlim<System.Numerics.Vector4> Value { get; }

        public System.Numerics.Vector4 Maximum => Setter.GetMaximumOrDefault(new System.Numerics.Vector4(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue));

        public System.Numerics.Vector4 Minimum => Setter.GetMinimumOrDefault(new System.Numerics.Vector4(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue, System.Single.MinValue));
    }
}
