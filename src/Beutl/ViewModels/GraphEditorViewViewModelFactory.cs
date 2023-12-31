using System.Numerics;
using System.Runtime.CompilerServices;

namespace Beutl.ViewModels;

public abstract partial class GraphEditorViewViewModelFactory
{
    private static readonly Dictionary<Type, GraphEditorViewViewModelFactory> s_registry = [];

    static GraphEditorViewViewModelFactory()
    {
        InitializeGenerated();
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

        Type type = parent.Animation.Property.PropertyType;
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
