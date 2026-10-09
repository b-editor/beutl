using Avalonia;
using Avalonia.Interactivity;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Editors;

public sealed class EasingEditorViewModel<T>(IPropertyAdapter<T?> property) : ValueEditorViewModel<T?>(property)
    where T : Easing
{
    // Whether other features had uncommitted operations when the current drag wrote its first step.
    private bool? _pendingBeforeDrag;

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EasingEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = EasingEditorItems.Create(typeof(T));
            editor.Bind(EasingEditor.ValueProperty, Value.Select(value => (Easing?)value).ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Easing?> { NewValue: T newValue } && sender is EasingEditor editor)
        {
            _pendingBeforeDrag ??= this.GetService<HistoryManager>()?.HasPendingOperations;
            // A validator may keep the current value, which raises no change to refresh the graph.
            editor.Value = SetCurrentValueAndGetCoerced(newValue);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is not PropertyEditorValueChangedEventArgs<Easing?> { NewValue: T newValue } args) return;

        bool? pendingBeforeDrag = _pendingBeforeDrag;
        _pendingBeforeDrag = null;
        if (ReferenceEquals(args.OldValue, newValue))
        {
            // A drag that ended where it began: drop its pending writes instead of committing a no-op.
            // Rolling back would also discard another feature's pending edits (a timeline nudge waits
            // 300 ms to commit), so then the original is only written back and joins that transaction.
            if (pendingBeforeDrag == false && IsElementEditable)
            {
                this.GetRequiredService<HistoryManager>().Rollback();
            }
            else
            {
                SetCurrentValueAndGetCoerced(newValue);
            }
        }
        else
        {
            SetValue(args.OldValue as T, newValue);
        }

        if (sender is EasingEditor editor)
        {
            // A validator may keep the current value, which raises no change to refresh the editor.
            editor.Value = EditingKeyFrame.Value is { } keyFrame ? keyFrame.Value : PropertyAdapter.GetValue();
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
