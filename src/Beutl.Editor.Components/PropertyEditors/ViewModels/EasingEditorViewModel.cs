using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class EasingEditorViewModel<T>(IPropertyAdapter<T?> property) : PropertyEditorViewModel<T?, Easing?>(property)
    where T : Easing
{
    protected override Easing? ToEdit(T? value) => value;

    protected override T? FromEdit(Easing? edit) => edit as T;

    public override Easing? PreviewEdit(Easing? value)
    {
        // A validator may keep the current value, which raises no change to refresh the graph.
        return !IsDisposed && value is T newValue ? SetCurrentValueAndGetCoerced(newValue) : value;
    }

    public override void ConfirmEdit(Easing? oldValue, Easing? newValue)
    {
        if (IsDisposed || newValue is not T value) return;

        if (ReferenceEquals(oldValue, value))
        {
            // A drag that ended where it began: write the original back without committing. The
            // drag's merged operation then changes nothing. Rolling the pending transaction back
            // instead would also discard other features' uncommitted edits, such as a timeline nudge.
            SetCurrentValueAndGetCoerced(value);
        }
        else
        {
            SetValue(oldValue as T, value);
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EasingEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = EasingEditorItems.Create(typeof(T));
        }
    }
}

internal static class EasingEditorItems
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(EasingEditorItems));

    // Ordered as easing families are usually presented, from gentle to pronounced.
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

    // Built at each Accept so easings registered by extensions loaded later are offered too.
    public static EasingItem[] Create(Type baseType)
    {
        LibraryService library = LibraryService.Current;
        return Create(baseType, library.GetTypesFromFormat(KnownLibraryItemFormats.Easing), library.FindItem);
    }

    internal static EasingItem[] Create(Type baseType, IEnumerable<Type> registeredTypes, Func<Type, LibraryItem?> findItem)
    {
        IEnumerable<Type> registered = registeredTypes.OrderBy(type => type.FullName, StringComparer.Ordinal);

        return s_builtInTypes.Concat(registered)
            .Distinct()
            .Where(type => type.IsAssignableTo(baseType)
                           && !type.IsAbstract
                           && !type.ContainsGenericParameters
                           && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => TryCreateItem(type, findItem(type)))
            .OfType<EasingItem>()
            .ToArray();
    }

    private static EasingItem? TryCreateItem(Type type, LibraryItem? libraryItem)
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
            // An extension's constructor can fail; leave that easing out rather than every easing editor.
            s_logger.LogWarning(ex, "Could not create a preview of the easing {EasingType}.", type);
            return null;
        }
    }
}
