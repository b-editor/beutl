using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

internal static class EasingItems
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(EasingItems));

    // The editor orders the easing families from gentle to pronounced.
    private static readonly Type[] s_builtInTypes =
    [
        typeof(LinearEasing),
        typeof(SplineEasing),
        typeof(HoldEasing),
        typeof(SineEaseIn),
        typeof(SineEaseOut),
        typeof(SineEaseInOut),
        typeof(QuadraticEaseIn),
        typeof(QuadraticEaseOut),
        typeof(QuadraticEaseInOut),
        typeof(CubicEaseIn),
        typeof(CubicEaseOut),
        typeof(CubicEaseInOut),
        typeof(QuarticEaseIn),
        typeof(QuarticEaseOut),
        typeof(QuarticEaseInOut),
        typeof(QuinticEaseIn),
        typeof(QuinticEaseOut),
        typeof(QuinticEaseInOut),
        typeof(ExponentialEaseIn),
        typeof(ExponentialEaseOut),
        typeof(ExponentialEaseInOut),
        typeof(CircularEaseIn),
        typeof(CircularEaseOut),
        typeof(CircularEaseInOut),
        typeof(BackEaseIn),
        typeof(BackEaseOut),
        typeof(BackEaseInOut),
        typeof(ElasticEaseIn),
        typeof(ElasticEaseOut),
        typeof(ElasticEaseInOut),
        typeof(BounceEaseIn),
        typeof(BounceEaseOut),
        typeof(BounceEaseInOut),
    ];

    // The Library tab shows the families alphabetically, followed by Linear and Hold.
    private static readonly Type[] s_libraryBuiltInTypes = s_builtInTypes
        .OrderBy(type => type == typeof(HoldEasing))
        .ThenBy(type => type == typeof(LinearEasing))
        .ThenBy(type => type.Name, StringComparer.Ordinal)
        .ToArray();

    // Built at each Accept so easings registered by extensions loaded later are offered too.
    public static EasingItem[] CreateEditorItems(Type baseType, LibraryService library)
    {
        return CreateEditorItems(baseType, library.GetTypesFromFormat(KnownLibraryItemFormats.Easing), library.FindItem);
    }

    internal static EasingItem[] CreateEditorItems(Type baseType, IEnumerable<Type> registeredTypes, Func<Type, LibraryItem?> findItem)
    {
        return GetTypes(baseType, s_builtInTypes, registeredTypes)
            .Select(type => TryCreateItem(type, findItem(type)))
            .OfType<EasingItem>()
            .ToArray();
    }

    public static IEnumerable<Type> GetLibraryTypes(LibraryService library)
    {
        // The Library tab has a separate spline tile.
        return GetTypes(typeof(Easing), s_libraryBuiltInTypes, library.GetTypesFromFormat(KnownLibraryItemFormats.Easing))
            .Where(type => type != typeof(SplineEasing));
    }

    private static IEnumerable<Type> GetTypes(Type baseType, Type[] builtInTypes, IEnumerable<Type> registeredTypes)
    {
        return builtInTypes.Concat(registeredTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
            .Distinct()
            .Where(type => type.IsAssignableTo(baseType)
                           && !type.IsAbstract
                           && !type.ContainsGenericParameters
                           && type.GetConstructor(Type.EmptyTypes) != null);
    }

    public static EasingItem? TryCreateItem(Type type, LibraryItem? libraryItem)
    {
        try
        {
            // A name registered through the library API wins, as in the library picker.
            return new EasingItem(
                libraryItem?.DisplayName ?? TypeDisplayHelpers.GetLocalizedName(type),
                libraryItem?.Description ?? TypeDisplayHelpers.GetLocalizedDescription(type),
                type,
                // Matches the spline icon of the library tab; a default spline would look linear.
                type == typeof(SplineEasing) ? new SplineEasing(0.75f, 0.1f, 0.25f, 0.9f) : null);
        }
        catch (Exception ex)
        {
            // A failed extension constructor must not prevent the remaining previews from being shown.
            s_logger.LogWarning(ex, "Could not create a preview of the easing {EasingType}.", type);
            return null;
        }
    }
}
