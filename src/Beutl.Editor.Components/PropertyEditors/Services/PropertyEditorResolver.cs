using System.Runtime.CompilerServices;
using Beutl.Api.Services;
using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// Chooses each property's editor among the extensions' registrations: Match editors, then [ChoicesProvider], the exact
// type, the nearest base class, interfaces and open generic types, and CoreObject; later-loaded extensions first.
internal static class PropertyEditorResolver
{
    private static readonly ConditionalWeakTable<IPropertyEditorContext, PropertyEditorRegistration> s_registrations = [];

    // Adds the context of each editor that takes some of adapters, Match editors first, and removes the adapters taken.
    public static void CreateContexts(
        IList<IPropertyAdapter> adapters,
        IReadOnlyList<PropertyEditorExtension> extensions,
        PropertyEditorSurface surface,
        Action<IPropertyEditorContext> add)
    {
        while (adapters.Count > 0
               && (TakeMatched(adapters, extensions, surface, add) || TakeFirst(adapters, extensions, surface, add)))
        {
        }
    }

    public static IPropertyEditorContext? CreateContext(
        IPropertyAdapter property, IReadOnlyList<PropertyEditorExtension> extensions, PropertyEditorSurface surface)
    {
        IPropertyEditorContext? created = null;
        CreateContexts([property], extensions, surface, context => created ??= context);
        return created;
    }

    // The registration that made context, or null for a context made elsewhere.
    public static PropertyEditorRegistration? RegistrationOf(IPropertyEditorContext context)
        => s_registrations.TryGetValue(context, out PropertyEditorRegistration? registration) ? registration : null;

    // The registrations that can take property, most specific first; a list item looks at those for lists first.
    public static IEnumerable<PropertyEditorRegistration> Candidates(
        IPropertyAdapter property, IReadOnlyList<PropertyEditorExtension> extensions, PropertyEditorSurface surface)
    {
        if (surface != PropertyEditorSurface.ListItem)
            return Candidates(property, extensions, r => Applies(r, surface));

        return Candidates(property, extensions, r => r.HasListItemControl && Applies(r, surface))
            .Concat(Candidates(property, extensions, r => !r.HasListItemControl && Applies(r, surface)));
    }

    private static IEnumerable<PropertyEditorRegistration> Candidates(
        IPropertyAdapter property, IReadOnlyList<PropertyEditorExtension> extensions, Func<PropertyEditorRegistration, bool> filter)
    {
        Type type = property.PropertyType;
        if (property.GetAttributes().OfType<ChoicesProviderAttribute>().Any())
        {
            foreach (PropertyEditorRegistration r in Ordered(extensions, filter))
            {
                if (r.Tier == PropertyEditorTier.Choices) yield return r;
            }
        }

        foreach (PropertyEditorRegistration r in Ordered(extensions, filter))
        {
            if (IsType(r) && r.ValueType == type) yield return r;
        }

        for (Type? baseType = type.BaseType;
             baseType != null && baseType != typeof(object) && baseType != typeof(CoreObject);
             baseType = baseType.BaseType)
        {
            foreach (PropertyEditorRegistration r in Ordered(extensions, filter))
            {
                if (IsType(r) && r.ValueType == baseType) yield return r;
            }
        }

        foreach (PropertyEditorRegistration r in Ordered(extensions, filter))
        {
            if (IsType(r) && r.ValueType is { IsInterface: true } i && Implements(type, i)) yield return r;
        }

        if (type != typeof(CoreObject) && type.IsAssignableTo(typeof(CoreObject)))
        {
            foreach (PropertyEditorRegistration r in Ordered(extensions, filter))
            {
                if (IsType(r) && r.ValueType == typeof(CoreObject)) yield return r;
            }
        }
    }

    public static IReadOnlyList<PropertyEditorRegistration> RegistrationsOf(PropertyEditorExtension extension)
        => ExtensionGuard.Call(extension, () => extension.Registry)?.Registrations ?? [];

    private static bool TakeMatched(
        IList<IPropertyAdapter> adapters,
        IReadOnlyList<PropertyEditorExtension> extensions,
        PropertyEditorSurface surface,
        Action<IPropertyEditorContext> add)
    {
        IPropertyAdapter[] remaining = [.. adapters];
        for (int i = extensions.Count - 1; i >= 0; i--)
        {
            PropertyEditorExtension extension = extensions[i];
            // Of one extension's editors, the one that takes the earliest property goes first, as the properties are ordered.
            (PropertyEditorRegistration Registration, IPropertyAdapter[] Taken, int First)? best = null;
            foreach (PropertyEditorRegistration registration in RegistrationsOf(extension))
            {
                if (registration.Tier != PropertyEditorTier.Match || !Applies(registration, surface)) continue;

                IPropertyAdapter[] taken = ExtensionGuard.Call(extension, () => registration.Match!(remaining).ToArray()) ?? [];
                if (taken.Length == 0) continue;

                int first = taken.Min(adapter => Array.IndexOf(remaining, adapter));
                if (best is null || first < best.Value.First)
                    best = (registration, taken, first);
            }

            if (best is { } chosen)
            {
                if (Create(chosen.Registration, chosen.Taken) is { } context) add(context);
                Remove(adapters, chosen.Taken);
                return true;
            }
        }

        return false;
    }

    private static bool TakeFirst(
        IList<IPropertyAdapter> adapters,
        IReadOnlyList<PropertyEditorExtension> extensions,
        PropertyEditorSurface surface,
        Action<IPropertyEditorContext> add)
    {
        foreach (IPropertyAdapter adapter in adapters)
        {
            bool found = false;
            foreach (PropertyEditorRegistration registration in Candidates(adapter, extensions, surface))
            {
                found = true;
                if (Create(registration, [adapter]) is { } context)
                {
                    add(context);
                    break;
                }
            }

            if (found)
            {
                adapters.Remove(adapter);
                return true;
            }
        }

        return false;
    }

    private static IPropertyEditorContext? Create(PropertyEditorRegistration registration, IPropertyAdapter[] properties)
    {
        IPropertyEditorContext? context = ExtensionGuard.Call(registration.Owner, () => registration.CreateContext(properties));
        if (context is null) return null;

        s_registrations.AddOrUpdate(context, registration);
        if (context is PropertyEditorViewModel { Extension: null } viewModel)
            viewModel.Extension = registration.Owner;

        return context;
    }

    private static bool IsType(PropertyEditorRegistration registration) => registration.Tier == PropertyEditorTier.Type;

    private static bool Applies(PropertyEditorRegistration registration, PropertyEditorSurface surface)
        => (surface == PropertyEditorSurface.ListItem || !registration.IsListItemOnly)
           && (surface == PropertyEditorSurface.Panel || !registration.IsPanelOnly);

    // Later-loaded extensions first; each one's registrations in the order it made them.
    private static IEnumerable<PropertyEditorRegistration> Ordered(
        IReadOnlyList<PropertyEditorExtension> extensions, Func<PropertyEditorRegistration, bool> filter)
    {
        for (int i = extensions.Count - 1; i >= 0; i--)
        {
            foreach (PropertyEditorRegistration r in RegistrationsOf(extensions[i]))
            {
                if (filter(r)) yield return r;
            }
        }
    }

    private static bool Implements(Type type, Type interfaceType)
    {
        if (!interfaceType.IsGenericTypeDefinition) return type.IsAssignableTo(interfaceType);

        return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceType);
    }

    private static void Remove(IList<IPropertyAdapter> adapters, IEnumerable<IPropertyAdapter> taken)
    {
        foreach (IPropertyAdapter adapter in taken)
            adapters.Remove(adapter);
    }
}
