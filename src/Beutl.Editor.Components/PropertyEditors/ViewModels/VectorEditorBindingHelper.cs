using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

internal static class VectorEditorBindingHelper
{
    // Passes the steps of the property's NumberStepAttribute and the format of its DisplayFormatAttribute to the
    // setters, skipping an attribute the property does not have.
    public static void ApplyNumberAttributes<TElement>(
        IPropertyAdapter adapter,
        Action<TElement, TElement> setSteps,
        Action<string?> setFormat)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            setSteps(TElement.CreateTruncating(stepAttr.LargeChange), TElement.CreateTruncating(stepAttr.SmallChange));
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            setFormat(formatAttr.DataFormatString);
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector2Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);

    public static void ApplyNumberAttributes<TElement>(Vector3Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);

    public static void ApplyNumberAttributes<TElement>(Vector4Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);

    public static void BindComponents(
        Vector4Editor<float> editor,
        CompositeDisposable disposables,
        ReadOnlyReactivePropertySlim<float> first,
        ReadOnlyReactivePropertySlim<float> second,
        ReadOnlyReactivePropertySlim<float> third,
        ReadOnlyReactivePropertySlim<float> fourth,
        ReactivePropertySlim<bool> isUniform)
    {
        editor.Bind(Vector4Editor<float>.FirstValueProperty, first.ToBinding())
            .DisposeWith(disposables);
        editor.Bind(Vector4Editor<float>.SecondValueProperty, second.ToBinding())
            .DisposeWith(disposables);
        editor.Bind(Vector4Editor<float>.ThirdValueProperty, third.ToBinding())
            .DisposeWith(disposables);
        editor.Bind(Vector4Editor<float>.FourthValueProperty, fourth.ToBinding())
            .DisposeWith(disposables);
        editor.Bind(Vector4Editor.IsUniformProperty, isUniform.ToBinding())
            .DisposeWith(disposables);
    }

    public static void AttachValueHandlers(
        Vector4Editor<float> editor,
        CompositeDisposable disposables,
        EventHandler<PropertyEditorValueChangedEventArgs> onConfirmed,
        EventHandler<PropertyEditorValueChangedEventArgs> onChanged)
    {
        editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, onConfirmed)
            .DisposeWith(disposables);
        editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, onChanged)
            .DisposeWith(disposables);
    }
}
