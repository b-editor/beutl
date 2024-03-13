using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;

using Reactive.Bindings;

namespace Beutl;

internal static class BindingHelper
{
    private static readonly Dictionary<Type, CompiledBindingPath> s_cache = [];

    public static IBinding ToPropertyBinding<T>(this IReactiveProperty<T> property, BindingMode bindingMode = BindingMode.Default)
    {
        if (!s_cache.TryGetValue(typeof(T), out CompiledBindingPath? path))
        {
            var propInfo = new ClrPropertyInfo(
                nameof(IReactiveProperty<T>.Value),
                o =>
                {
                    if (o is IReactiveProperty<T> p)
                    {
                        return p.Value;
                    }
                    else
                    {
                        return default;
                    }
                },
                (o, v) =>
                {
                    if (o is IReactiveProperty<T> p)
                    {
                        p.Value = v is T t ? t : default!;
                    }
                },
                typeof(T));

            var b = new CompiledBindingPathBuilder();
            b.Property(propInfo, PropertyInfoAccessorFactory.CreateInpcPropertyAccessor);

            path = b.Build();
            s_cache.Add(typeof(T), path);
        }

        return new CompiledBindingExtension(path)
        {
            Source = property,
            Mode = bindingMode
        };
    }
}
