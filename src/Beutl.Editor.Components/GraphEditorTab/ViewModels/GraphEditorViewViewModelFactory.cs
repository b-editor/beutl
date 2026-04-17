using System.Numerics;
using System.Runtime.CompilerServices;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public abstract class GraphEditorViewViewModelFactory
{
    private static readonly Dictionary<Type, GraphEditorViewViewModelFactory> s_registry = [];

    static GraphEditorViewViewModelFactory()
    {
        // Vector2 (int)
        Register<Media.PixelPoint>(int.MinValue, int.MaxValue,
            new("X", v => v.X, (v, n) => new Media.PixelPoint(int.CreateTruncating(n), v.Y)),
            new("Y", v => v.Y, (v, n) => new Media.PixelPoint(v.X, int.CreateTruncating(n))));
        Register<Media.PixelSize>(int.MinValue, int.MaxValue,
            new("Width", v => v.Width, (v, n) => new Media.PixelSize(int.CreateTruncating(n), v.Height)),
            new("Height", v => v.Height, (v, n) => new Media.PixelSize(v.Width, int.CreateTruncating(n))));

        // Vector2 (float)
        Register<Graphics.Point>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Graphics.Point(float.CreateTruncating(n), v.Y)),
            new("Y", v => v.Y, (v, n) => new Graphics.Point(v.X, float.CreateTruncating(n))));
        Register<Graphics.Size>(float.MinValue, float.MaxValue,
            new("Width", v => v.Width, (v, n) => new Graphics.Size(float.CreateTruncating(n), v.Height)),
            new("Height", v => v.Height, (v, n) => new Graphics.Size(v.Width, float.CreateTruncating(n))));
        Register<Graphics.Vector>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Graphics.Vector(float.CreateTruncating(n), v.Y)),
            new("Y", v => v.Y, (v, n) => new Graphics.Vector(v.X, float.CreateTruncating(n))));
        Register<Vector2>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Vector2(float.CreateTruncating(n), v.Y)),
            new("Y", v => v.Y, (v, n) => new Vector2(v.X, float.CreateTruncating(n))));

        // Vector3 (float)
        Register<Vector3>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Vector3(float.CreateTruncating(n), v.Y, v.Z)),
            new("Y", v => v.Y, (v, n) => new Vector3(v.X, float.CreateTruncating(n), v.Z)),
            new("Z", v => v.Z, (v, n) => new Vector3(v.X, v.Y, float.CreateTruncating(n))));

        // Vector4 (int)
        Register<Media.PixelRect>(int.MinValue, int.MaxValue,
            new("X", v => v.X, (v, n) => new Media.PixelRect(int.CreateTruncating(n), v.Y, v.Width, v.Height)),
            new("Y", v => v.Y, (v, n) => new Media.PixelRect(v.X, int.CreateTruncating(n), v.Width, v.Height)),
            new("Width", v => v.Width, (v, n) => new Media.PixelRect(v.X, v.Y, int.CreateTruncating(n), v.Height)),
            new("Height", v => v.Height, (v, n) => new Media.PixelRect(v.X, v.Y, v.Width, int.CreateTruncating(n))));

        // Vector4 (float)
        Register<Graphics.Rect>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Graphics.Rect(float.CreateTruncating(n), v.Y, v.Width, v.Height)),
            new("Y", v => v.Y, (v, n) => new Graphics.Rect(v.X, float.CreateTruncating(n), v.Width, v.Height)),
            new("Width", v => v.Width, (v, n) => new Graphics.Rect(v.X, v.Y, float.CreateTruncating(n), v.Height)),
            new("Height", v => v.Height, (v, n) => new Graphics.Rect(v.X, v.Y, v.Width, float.CreateTruncating(n))));
        Register<Media.CornerRadius>(float.MinValue, float.MaxValue,
            new("TopLeft", v => v.TopLeft, (v, n) => new Media.CornerRadius(float.CreateTruncating(n), v.TopRight, v.BottomRight, v.BottomLeft)),
            new("TopRight", v => v.TopRight, (v, n) => new Media.CornerRadius(v.TopLeft, float.CreateTruncating(n), v.BottomRight, v.BottomLeft)),
            new("BottomRight", v => v.BottomRight, (v, n) => new Media.CornerRadius(v.TopLeft, v.TopRight, float.CreateTruncating(n), v.BottomLeft)),
            new("BottomLeft", v => v.BottomLeft, (v, n) => new Media.CornerRadius(v.TopLeft, v.TopRight, v.BottomRight, float.CreateTruncating(n))));
        Register<Graphics.Thickness>(float.MinValue, float.MaxValue,
            new("Left", v => v.Left, (v, n) => new Graphics.Thickness(float.CreateTruncating(n), v.Top, v.Right, v.Bottom)),
            new("Top", v => v.Top, (v, n) => new Graphics.Thickness(v.Left, float.CreateTruncating(n), v.Right, v.Bottom)),
            new("Right", v => v.Right, (v, n) => new Graphics.Thickness(v.Left, v.Top, float.CreateTruncating(n), v.Bottom)),
            new("Bottom", v => v.Bottom, (v, n) => new Graphics.Thickness(v.Left, v.Top, v.Right, float.CreateTruncating(n))));
        Register<Vector4>(float.MinValue, float.MaxValue,
            new("X", v => v.X, (v, n) => new Vector4(float.CreateTruncating(n), v.Y, v.Z, v.W)),
            new("Y", v => v.Y, (v, n) => new Vector4(v.X, float.CreateTruncating(n), v.Z, v.W)),
            new("Z", v => v.Z, (v, n) => new Vector4(v.X, v.Y, float.CreateTruncating(n), v.W)),
            new("W", v => v.W, (v, n) => new Vector4(v.X, v.Y, v.Z, float.CreateTruncating(n))));

        s_registry[typeof(Media.Color)] = new ColorFactory();
    }

    public abstract double MaxValue { get; }

    public abstract double MinValue { get; }

    public static IEnumerable<GraphEditorViewViewModelFactory> GetFactory(GraphEditorViewModel parent)
    {
        static bool IsAssignableToGenericType(Type givenType, Type genericType)
        {
            foreach (Type it in givenType.GetInterfaces())
            {
                if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                    return true;
            }

            if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
                return true;

            Type? baseType = givenType.BaseType;
            if (baseType == null)
                return false;

            return IsAssignableToGenericType(baseType, genericType);
        }

        Type type = parent.Animation.ValueType;
        if (s_registry.TryGetValue(type, out GraphEditorViewViewModelFactory? factory))
        {
            yield return factory;
        }
        else
        {
            if (IsAssignableToGenericType(type, typeof(INumber<>))
                && IsAssignableToGenericType(type, typeof(IMinMaxValue<>)))
            {
                factory = (GraphEditorViewViewModelFactory)Activator.CreateInstance(typeof(NumberFactory<>).MakeGenericType(type))!;
                s_registry[type] = factory;
                yield return factory;
            }
        }
    }

    public static GraphEditorViewViewModel[] CreateViews(GraphEditorViewModel parent, GraphEditorViewViewModelFactory? factory)
    {
        return factory?.CreateViewsCore(parent) ?? [];
    }

    protected abstract GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent);

    private static void Register<T>(double minValue, double maxValue, params VectorLikeFactory<T>.Field[] fields)
        where T : struct
    {
        s_registry[typeof(T)] = new VectorLikeFactory<T>(minValue, maxValue, fields);
    }

    private sealed class VectorLikeFactory<T> : GraphEditorViewViewModelFactory where T : struct
    {
        public readonly record struct Field(string Name, Func<T, double> Get, Func<T, double, T> Set);

        private readonly Field[] _fields;

        public VectorLikeFactory(double minValue, double maxValue, Field[] fields)
        {
            MinValue = minValue;
            MaxValue = maxValue;
            _fields = fields;
        }

        public override double MaxValue { get; }

        public override double MinValue { get; }

        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            var result = new GraphEditorViewViewModel[_fields.Length];
            for (int i = 0; i < _fields.Length; i++)
            {
                int index = i;
                result[i] = new GraphEditorViewViewModel(
                    parent,
                    _fields[i].Name,
                    obj => ConvertTo(index, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(index, old, value, out obj));
            }
            return result;
        }

        private double ConvertTo(int fieldIndex, object? obj)
        {
            if (obj is T typed && (uint)fieldIndex < (uint)_fields.Length)
            {
                return _fields[fieldIndex].Get(typed);
            }
            return 1d;
        }

        private bool TryConvertFrom(int fieldIndex, object? oldValue, double value, out object? obj)
        {
            if (oldValue is T old && (uint)fieldIndex < (uint)_fields.Length)
            {
                obj = _fields[fieldIndex].Set(old, value);
                return true;
            }
            obj = null;
            return false;
        }
    }

    private sealed class NumberFactory<T> : GraphEditorViewViewModelFactory
        where T : INumber<T>, IMinMaxValue<T>
    {
        public override double MaxValue => double.CreateTruncating(T.MaxValue);

        public override double MinValue => double.CreateTruncating(T.MinValue);

        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "Self",
                    obj =>
                    {
                        if (obj is T t)
                        {
                            return double.CreateTruncating(t);
                        }
                        else
                        {
                            return 1d;
                        }
                    },
                    (object? _, double value, Type _, out object? obj) =>
                    {
                        obj = T.CreateTruncating(value);
                        return true;
                    })
            ];
        }
    }

    private sealed class ColorFactory : GraphEditorViewViewModelFactory
    {
        private static double OECF_sRGB(double linear)
        {
            return linear <= 0.0031308 ? linear * 12.92 : ((Math.Pow(linear, 1.0 / 2.4) * 1.055) - 0.055);
        }

        private static double EOCF_sRGB(double srgb)
        {
            return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        protected override GraphEditorViewViewModel[] CreateViewsCore(GraphEditorViewModel parent)
        {
            return
            [
                new GraphEditorViewViewModel(
                    parent,
                    "Alpha",
                    obj => ConvertTo(0, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(0, old, value, out obj),
                    Avalonia.Media.Colors.White),

                new GraphEditorViewViewModel(
                    parent,
                    "Red",
                    obj => ConvertTo(1, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(1, old, value, out obj),
                    Avalonia.Media.Colors.Red),

                new GraphEditorViewViewModel(
                    parent,
                    "Green",
                    obj => ConvertTo(2, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(2, old, value, out obj),
                    Avalonia.Media.Colors.Green),

                new GraphEditorViewViewModel(
                    parent,
                    "Blue",
                    obj => ConvertTo(3, obj),
                    (object? old, double value, Type _, out object? obj) => TryConvertFrom(3, old, value, out obj),
                    Avalonia.Media.Colors.Blue)
            ];
        }

        public override double MaxValue => double.CreateTruncating(byte.MaxValue);

        public override double MinValue => double.CreateTruncating(byte.MinValue);

        private static double ConvertTo(int fieldIndex, object? obj)
        {
            static double Cast(byte value)
            {
                return EOCF_sRGB(value / 255d) * 255d;
            }

            if (obj is Media.Color typed)
            {
                return fieldIndex switch
                {
                    0 => typed.A,
                    1 => Cast(typed.R),
                    2 => Cast(typed.G),
                    3 => Cast(typed.B),
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
            byte Cast()
            {
                return byte.CreateTruncating(Math.Round(OECF_sRGB(value / 255d) * 255d));
            }

            if (oldValue is Media.Color old)
            {
                switch (fieldIndex)
                {
                    case 0:
                        obj = new Media.Color(byte.CreateTruncating(value), old.R, old.G, old.B);
                        return true;

                    case 1:
                        obj = new Media.Color(old.A, Cast(), old.G, old.B);
                        return true;

                    case 2:
                        obj = new Media.Color(old.A, old.R, Cast(), old.B);
                        return true;

                    case 3:
                        obj = new Media.Color(old.A, old.R, old.G, Cast());
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
