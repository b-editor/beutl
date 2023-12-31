
using System.Runtime.CompilerServices;

namespace Beutl.ViewModels;

public abstract partial class GraphEditorViewViewModelFactory
{
    private static void InitializeGenerated()
    {
        s_registry[typeof(Media.PixelPoint)] = new PixelPointFactory();
        s_registry[typeof(Media.PixelSize)] = new PixelSizeFactory();
        s_registry[typeof(Graphics.Point)] = new PointFactory();
        s_registry[typeof(Graphics.Size)] = new SizeFactory();
        s_registry[typeof(Graphics.Vector)] = new VectorFactory();
        s_registry[typeof(System.Numerics.Vector2)] = new Vector2Factory();

        s_registry[typeof(System.Numerics.Vector3)] = new Vector3Factory();

        s_registry[typeof(Media.PixelRect)] = new PixelRectFactory();
        s_registry[typeof(Graphics.Rect)] = new RectFactory();
        s_registry[typeof(Media.CornerRadius)] = new CornerRadiusFactory();
        s_registry[typeof(Graphics.Thickness)] = new ThicknessFactory();
        s_registry[typeof(System.Numerics.Vector4)] = new Vector4Factory();
    }

    // Vector2
    private sealed class PixelPointFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(int.MaxValue);

        public override double MinValue => double.CreateTruncating(int.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(int value)
            {
                return value;
            }

            if (obj is Media.PixelPoint typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int Cast()
            {
                return int.CreateTruncating(value);
            }

            if (oldValue is Media.PixelPoint old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Media.PixelPoint(Cast(), old.Y);
                        return true;

                    case 1:
                        obj = new Media.PixelPoint(old.X, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class PixelSizeFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "Width",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Height",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(int.MaxValue);

        public override double MinValue => double.CreateTruncating(int.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(int value)
            {
                return value;
            }

            if (obj is Media.PixelSize typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.Width),
                    1 => Cast(typed.Height),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int Cast()
            {
                return int.CreateTruncating(value);
            }

            if (oldValue is Media.PixelSize old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Media.PixelSize(Cast(), old.Height);
                        return true;

                    case 1:
                        obj = new Media.PixelSize(old.Width, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class PointFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Graphics.Point typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Graphics.Point old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Graphics.Point(Cast(), old.Y);
                        return true;

                    case 1:
                        obj = new Graphics.Point(old.X, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class SizeFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "Width",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Height",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Graphics.Size typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.Width),
                    1 => Cast(typed.Height),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Graphics.Size old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Graphics.Size(Cast(), old.Height);
                        return true;

                    case 1:
                        obj = new Graphics.Size(old.Width, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class VectorFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Graphics.Vector typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Graphics.Vector old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Graphics.Vector(Cast(), old.Y);
                        return true;

                    case 1:
                        obj = new Graphics.Vector(old.X, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class Vector2Factory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is System.Numerics.Vector2 typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is System.Numerics.Vector2 old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new System.Numerics.Vector2(Cast(), old.Y);
                        return true;

                    case 1:
                        obj = new System.Numerics.Vector2(old.X, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }

    // Vector3
    private sealed class Vector3Factory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Z",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is System.Numerics.Vector3 typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    2 => Cast(typed.Z),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is System.Numerics.Vector3 old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new System.Numerics.Vector3(Cast(), old.Y, old.Z);
                        return true;

                    case 1:
                        obj = new System.Numerics.Vector3(old.X, Cast(), old.Z);
                        return true;

                    case 2:
                        obj = new System.Numerics.Vector3(old.X, old.Y, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }

    // Vector4
    private sealed class PixelRectFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Width",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Height",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(int.MaxValue);

        public override double MinValue => double.CreateTruncating(int.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(int value)
            {
                return value;
            }

            if (obj is Media.PixelRect typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    2 => Cast(typed.Width),
                    3 => Cast(typed.Height),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int Cast()
            {
                return int.CreateTruncating(value);
            }

            if (oldValue is Media.PixelRect old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Media.PixelRect(Cast(), old.Y, old.Width, old.Height);
                        return true;

                    case 1:
                        obj = new Media.PixelRect(old.X, Cast(), old.Width, old.Height);
                        return true;

                    case 2:
                        obj = new Media.PixelRect(old.X, old.Y, Cast(), old.Height);
                        return true;

                    case 3:
                        obj = new Media.PixelRect(old.X, old.Y, old.Width, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class RectFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Width",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Height",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Graphics.Rect typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    2 => Cast(typed.Width),
                    3 => Cast(typed.Height),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Graphics.Rect old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Graphics.Rect(Cast(), old.Y, old.Width, old.Height);
                        return true;

                    case 1:
                        obj = new Graphics.Rect(old.X, Cast(), old.Width, old.Height);
                        return true;

                    case 2:
                        obj = new Graphics.Rect(old.X, old.Y, Cast(), old.Height);
                        return true;

                    case 3:
                        obj = new Graphics.Rect(old.X, old.Y, old.Width, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class CornerRadiusFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "TopLeft",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "TopRight",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "BottomRight",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "BottomLeft",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Media.CornerRadius typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.TopLeft),
                    1 => Cast(typed.TopRight),
                    2 => Cast(typed.BottomRight),
                    3 => Cast(typed.BottomLeft),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Media.CornerRadius old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Media.CornerRadius(Cast(), old.TopRight, old.BottomRight, old.BottomLeft);
                        return true;

                    case 1:
                        obj = new Media.CornerRadius(old.TopLeft, Cast(), old.BottomRight, old.BottomLeft);
                        return true;

                    case 2:
                        obj = new Media.CornerRadius(old.TopLeft, old.TopRight, Cast(), old.BottomLeft);
                        return true;

                    case 3:
                        obj = new Media.CornerRadius(old.TopLeft, old.TopRight, old.BottomRight, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class ThicknessFactory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "Left",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Top",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Right",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Bottom",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is Graphics.Thickness typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.Left),
                    1 => Cast(typed.Top),
                    2 => Cast(typed.Right),
                    3 => Cast(typed.Bottom),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is Graphics.Thickness old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Graphics.Thickness(Cast(), old.Top, old.Right, old.Bottom);
                        return true;

                    case 1:
                        obj = new Graphics.Thickness(old.Left, Cast(), old.Right, old.Bottom);
                        return true;

                    case 2:
                        obj = new Graphics.Thickness(old.Left, old.Top, Cast(), old.Bottom);
                        return true;

                    case 3:
                        obj = new Graphics.Thickness(old.Left, old.Top, old.Right, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }
    private sealed class Vector4Factory : GraphEditorViewViewModelFactory
    {
        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "X",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Y",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "Z",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj)),

                new GraphEditorViewViewModel(
                    parent,
                    "W",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj))
            ];
        }

        public override double MaxValue => double.CreateTruncating(float.MaxValue);

        public override double MinValue => double.CreateTruncating(float.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double Cast(float value)
            {
                return value;
            }

            if (obj is System.Numerics.Vector4 typed)
            {
                return fieldIndex switch
                {
                    0 => Cast(typed.X),
                    1 => Cast(typed.Y),
                    2 => Cast(typed.Z),
                    3 => Cast(typed.W),
                    _ => 1d
                };
            }
            else
            {
                return 1d;
            }
        }

        private static bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float Cast()
            {
                return float.CreateTruncating(value);
            }

            if (oldValue is System.Numerics.Vector4 old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new System.Numerics.Vector4(Cast(), old.Y, old.Z, old.W);
                        return true;

                    case 1:
                        obj = new System.Numerics.Vector4(old.X, Cast(), old.Z, old.W);
                        return true;

                    case 2:
                        obj = new System.Numerics.Vector4(old.X, old.Y, Cast(), old.W);
                        return true;

                    case 3:
                        obj = new System.Numerics.Vector4(old.X, old.Y, old.Z, Cast());
                        return true;

                    default:
                        obj = null;
                        return false;
                }
            }
            else
            {
                obj = null;
                return false;
            }
        }
    }

}
