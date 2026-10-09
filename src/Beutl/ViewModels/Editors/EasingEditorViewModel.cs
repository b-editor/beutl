using Avalonia;
using Avalonia.Interactivity;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;

namespace Beutl.ViewModels.Editors;

public sealed class EasingEditorViewModel<T>(IPropertyAdapter<T?> property) : ValueEditorViewModel<T?>(property)
    where T : Easing
{
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
        if (e is PropertyEditorValueChangedEventArgs<Easing?> { NewValue: T newValue })
        {
            SetCurrentValueAndGetCoerced(newValue);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Easing?> { NewValue: T newValue } args)
        {
            SetValue(args.OldValue as T, newValue);
        }
    }
}

internal static class EasingEditorItems
{
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
        IEnumerable<Type> registered = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Easing)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        return s_builtInTypes.Concat(registered)
            .Distinct()
            .Where(type => type.IsAssignableTo(baseType)
                           && !type.IsAbstract
                           && !type.ContainsGenericParameters
                           && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => new EasingItem(
                TypeDisplayHelpers.GetLocalizedName(type),
                TypeDisplayHelpers.GetLocalizedDescription(type),
                type,
                // Matches the spline icon of the library tab; a default spline would look linear.
                type == typeof(SplineEasing) ? new SplineEasing(0.75f, 0.1f, 0.25f, 0.9f) : null))
            .ToArray();
    }
}
