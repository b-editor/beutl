using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

internal static class VectorEditorBindingHelper
{
    public static void ApplyNumberAttributes<TElement>(Vector2Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector3Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector4Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }

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
