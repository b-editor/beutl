using System.ComponentModel.DataAnnotations;
using System.Numerics;

using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T>(IPropertyAdapter<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, INumber<T>
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is NumberEditor<T> editor && !Disposables.IsDisposed)
        {
            var attrs = PropertyAdapter.GetAttributes();
            var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
            if (stepAttr != null)
            {
                editor.LargeChange = T.CreateTruncating(stepAttr.LargeChange);
                editor.SmallChange = T.CreateTruncating(stepAttr.SmallChange);
            }
            var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
            if (formatAttr != null)
            {
                editor.NumberFormat = formatAttr.DataFormatString;
            }

            editor.Bind(NumberEditor<T>.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<T> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is NumberEditor<T> editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }
}
