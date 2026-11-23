using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Reflection;
using Beutl.Language;

namespace Beutl.Animation;

public static class ComponentAnimations
{
    private static readonly Dictionary<Type, (Func<IProperty, IComponentAnimation> Separate, Action<IProperty, MediaTime> Unseparate)> s_operations = [];
    private static readonly Dictionary<Type, object> s_definitions = [];
    private static readonly Dictionary<Type, object> s_offsets = [];
    private static readonly Dictionary<Type, object> s_replacements = [];

    static ComponentAnimations()
    {
        // Vector2 (int)
        Register<Media.PixelPoint, int>(
            new("X", v => v.X, (v, n) => new Media.PixelPoint(n, v.Y)),
            new("Y", v => v.Y, (v, n) => new Media.PixelPoint(v.X, n)));
        Register<Media.PixelSize, int>(
            new("Width", v => v.Width, (v, n) => new Media.PixelSize(n, v.Height)),
            new("Height", v => v.Height, (v, n) => new Media.PixelSize(v.Width, n)));

        // Vector2 (float)
        Register<Graphics.Point, float>(
            new("X", v => v.X, (v, n) => new Graphics.Point(n, v.Y)),
            new("Y", v => v.Y, (v, n) => new Graphics.Point(v.X, n)));
        Register<Graphics.Size, float>(
            new("Width", v => v.Width, (v, n) => new Graphics.Size(n, v.Height)),
            new("Height", v => v.Height, (v, n) => new Graphics.Size(v.Width, n)));
        Register<Graphics.Vector, float>(
            new("X", v => v.X, (v, n) => new Graphics.Vector(n, v.Y)),
            new("Y", v => v.Y, (v, n) => new Graphics.Vector(v.X, n)));
        Register<Vector2, float>(
            new("X", v => v.X, (v, n) => new Vector2(n, v.Y)),
            new("Y", v => v.Y, (v, n) => new Vector2(v.X, n)));

        // Vector3 (float)
        Register<Vector3, float>(
            new("X", v => v.X, (v, n) => new Vector3(n, v.Y, v.Z)),
            new("Y", v => v.Y, (v, n) => new Vector3(v.X, n, v.Z)),
            new("Z", v => v.Z, (v, n) => new Vector3(v.X, v.Y, n)));

        // Vector4 (int)
        Register<Media.PixelRect, int>(
            new("X", v => v.X, (v, n) => new Media.PixelRect(n, v.Y, v.Width, v.Height)),
            new("Y", v => v.Y, (v, n) => new Media.PixelRect(v.X, n, v.Width, v.Height)),
            new("Width", v => v.Width, (v, n) => new Media.PixelRect(v.X, v.Y, n, v.Height)),
            new("Height", v => v.Height, (v, n) => new Media.PixelRect(v.X, v.Y, v.Width, n)));

        // Vector4 (float)
        Register<Graphics.Rect, float>(
            new("X", v => v.X, (v, n) => new Graphics.Rect(n, v.Y, v.Width, v.Height)),
            new("Y", v => v.Y, (v, n) => new Graphics.Rect(v.X, n, v.Width, v.Height)),
            new("Width", v => v.Width, (v, n) => new Graphics.Rect(v.X, v.Y, n, v.Height)),
            new("Height", v => v.Height, (v, n) => new Graphics.Rect(v.X, v.Y, v.Width, n)));
        Register<Media.CornerRadius, float>(
            new("TopLeft", v => v.TopLeft, (v, n) => new Media.CornerRadius(n, v.TopRight, v.BottomRight, v.BottomLeft)),
            new("TopRight", v => v.TopRight, (v, n) => new Media.CornerRadius(v.TopLeft, n, v.BottomRight, v.BottomLeft)),
            new("BottomRight", v => v.BottomRight, (v, n) => new Media.CornerRadius(v.TopLeft, v.TopRight, n, v.BottomLeft)),
            new("BottomLeft", v => v.BottomLeft, (v, n) => new Media.CornerRadius(v.TopLeft, v.TopRight, v.BottomRight, n)));
        Register<Graphics.Thickness, float>(
            new("Left", v => v.Left, (v, n) => new Graphics.Thickness(n, v.Top, v.Right, v.Bottom)),
            new("Top", v => v.Top, (v, n) => new Graphics.Thickness(v.Left, n, v.Right, v.Bottom)),
            new("Right", v => v.Right, (v, n) => new Graphics.Thickness(v.Left, v.Top, n, v.Bottom)),
            new("Bottom", v => v.Bottom, (v, n) => new Graphics.Thickness(v.Left, v.Top, v.Right, n)));
        Register<Vector4, float>(
            new("X", v => v.X, (v, n) => new Vector4(n, v.Y, v.Z, v.W)),
            new("Y", v => v.Y, (v, n) => new Vector4(v.X, n, v.Z, v.W)),
            new("Z", v => v.Z, (v, n) => new Vector4(v.X, v.Y, n, v.W)),
            new("W", v => v.W, (v, n) => new Vector4(v.X, v.Y, v.Z, n)));

        Register<Media.Color, byte>(
            new("R", v => v.R, (v, n) => Media.Color.FromArgb(v.A, n, v.G, v.B)),
            new("G", v => v.G, (v, n) => Media.Color.FromArgb(v.A, v.R, n, v.B)),
            new("B", v => v.B, (v, n) => Media.Color.FromArgb(v.A, v.R, v.G, n)),
            new("A", v => v.A, (v, n) => Media.Color.FromArgb(n, v.R, v.G, v.B)));
        Register<Media.GradingColor, float>(
            new("R", v => v.R, (v, n) => new(n, v.G, v.B)),
            new("G", v => v.G, (v, n) => new(v.R, n, v.B)),
            new("B", v => v.B, (v, n) => new(v.R, v.G, n)));
        RegisterWithUnit<Graphics.RelativePoint, float>((a, b) => a.Unit != b.Unit,
            new("X", v => v.Point.X, (v, n) => new(n, v.Point.Y, v.Unit)),
            new("Y", v => v.Point.Y, (v, n) => new(v.Point.X, n, v.Unit)));
        RegisterWithUnit<Graphics.RelativeRect, float>((a, b) => a.Unit != b.Unit,
            new("X", v => v.Rect.X, (v, n) => new(new Graphics.Rect(n, v.Rect.Y, v.Rect.Width, v.Rect.Height), v.Unit)),
            new("Y", v => v.Rect.Y, (v, n) => new(new Graphics.Rect(v.Rect.X, n, v.Rect.Width, v.Rect.Height), v.Unit)),
            new("Width", v => v.Rect.Width, (v, n) => new(new Graphics.Rect(v.Rect.X, v.Rect.Y, n, v.Rect.Height), v.Unit)),
            new("Height", v => v.Rect.Height, (v, n) => new(new Graphics.Rect(v.Rect.X, v.Rect.Y, v.Rect.Width, n), v.Unit)));
    }

    private static void Register<T, TComponent>(params ComponentDefinition<T, TComponent>[] components)
        where TComponent : INumber<TComponent>
        => RegisterWithUnit<T, TComponent>(null, components);

    // unitChanged compares a unit that applies to the whole value. Separating such a type keeps the original
    // animation's unit schedule, which an edit that changes the unit replaces.
    private static void RegisterWithUnit<T, TComponent>(Func<T, T, bool>? unitChanged,
        params ComponentDefinition<T, TComponent>[] components)
        where TComponent : INumber<TComponent>
    {
        s_definitions[typeof(T)] = new ComponentSet<T, TComponent>(components, unitChanged);
        s_offsets[typeof(T)] = (Func<T, T, T, double, T>)((value, from, to, scale) =>
        {
            if (unitChanged?.Invoke(from, to) == true || unitChanged?.Invoke(value, from) == true)
                return to;

            foreach (var component in components)
                value = component.Set(value, NumericOffset.AddScaled(component.Get(value), component.Get(from), component.Get(to), scale));
            return value;
        });
        s_replacements[typeof(T)] = (Func<T, T, T, T>)((value, shown, input) =>
        {
            if (unitChanged?.Invoke(shown, input) == true || unitChanged?.Invoke(value, shown) == true)
                return input;

            foreach (var component in components)
            {
                TComponent edited = component.Get(input);
                if (edited != component.Get(shown))
                    value = component.Set(value, edited);
            }
            return value;
        });
        s_operations[typeof(T)] = (property => ComponentAnimation<T, TComponent>.Create((IProperty<T>)property), (property, time) =>
        {
            // Capture the component tracks' value, including component expressions and interpolation, without baking any
            // aggregate expression that stays on the property.
            var typed = (IExpressionProperty<T>)property;
            T value = typed.GetPropertyAnimation() is IComponentAnimation<T> components
                ? components.EvaluateAt(time)
                : typed.Evaluate(new EvaluationContext(time));
            typed.SetPropertyAnimation(null);
            typed.Value = value;
        }
        );
    }

    internal static ComponentSet<T, TComponent> GetComponentSet<T, TComponent>()
        => (ComponentSet<T, TComponent>)s_definitions[typeof(T)];

    internal static DisplayAttribute GetDisplayAttribute(string name)
        => name switch
        {
            "R" => new() { Name = nameof(Strings.Red), ResourceType = typeof(Strings) },
            "G" => new() { Name = nameof(Strings.Green), ResourceType = typeof(Strings) },
            "B" => new() { Name = nameof(Strings.Blue), ResourceType = typeof(Strings) },
            "A" => new() { Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings) },
            "X" or "Y" or "Z" or "W" => new() { Name = name },
            _ => new() { Name = name, ResourceType = typeof(Strings) }
        };

    public static bool IsSupported(Type type) => s_operations.ContainsKey(type);

    public static T OffsetValue<T>(T value, T from, T to)
        => s_offsets.TryGetValue(typeof(T), out object? offset) ? ((Func<T, T, T, double, T>)offset)(value, from, to, 1) : to;

    internal static T AddScaledDelta<T>(T value, T from, T to, double scale)
    {
        EnsureScaledDeltaSupported<T>();
        return s_offsets.TryGetValue(typeof(T), out object? offset)
            ? ((Func<T, T, T, double, T>)offset)(value, from, to, scale)
            : ScalarOffset<T>.Add!(value, from, to, scale);
    }

    internal static void EnsureScaledDeltaSupported<T>()
    {
        if (!s_offsets.ContainsKey(typeof(T)) && ScalarOffset<T>.Add is null)
            throw new ExpressionException($"Loop modes Offset and Continue are not supported for {typeof(T).Name}.");
    }

    private static class ScalarOffset<T>
    {
        public static readonly Func<T, T, T, double, T>? Add = Bind();

        private static Func<T, T, T, double, T>? Bind()
        {
            Type type = typeof(T);
            if (!type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INumber<>)
                && i.GenericTypeArguments[0] == type))
            {
                return null;
            }

            return typeof(NumericOffset).GetMethod(nameof(NumericOffset.AddScaled), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(type)
                .CreateDelegate<Func<T, T, T, double, T>>();
        }
    }

    // Takes the components of input that differ from shown, and keeps value's other components.
    public static T ReplaceEditedComponents<T>(T value, T shown, T input)
        => s_replacements.TryGetValue(typeof(T), out object? replace) ? ((Func<T, T, T, T>)replace)(value, shown, input) : input;

    public static IComponentAnimation Separate(IProperty property)
    {
        if (property.GetPropertyAnimation() is IComponentAnimation existing) return existing;
        var animation = s_operations[property.ValueType].Separate(property);
        property.SetPropertyAnimation(animation);
        return animation;
    }

    public static void Unseparate(IProperty property, MediaTime time)
    {
        if (property.GetPropertyAnimation() is IComponentAnimation)
            s_operations[property.ValueType].Unseparate(property, time);
    }
}

internal sealed record ComponentDefinition<T, TComponent>(
    string Name, Func<T, TComponent> Get, Func<T, TComponent, T> Set);

internal sealed record ComponentSet<T, TComponent>(
    ComponentDefinition<T, TComponent>[] Components, Func<T, T, bool>? UnitChanged);
