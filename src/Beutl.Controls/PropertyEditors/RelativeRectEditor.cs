using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Data;
using Avalonia.Interactivity;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class RelativeRectEditor : Vector4Editor, IValueEditorControl<Graphics.RelativeRect>
{
    public static readonly DirectProperty<RelativeRectEditor, float> FirstValueProperty =
        Vector4Editor<float>.FirstValueProperty.AddOwner<RelativeRectEditor>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> SecondValueProperty =
        Vector4Editor<float>.SecondValueProperty.AddOwner<RelativeRectEditor>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> ThirdValueProperty =
        Vector4Editor<float>.ThirdValueProperty.AddOwner<RelativeRectEditor>(
            o => o.ThirdValue,
            (o, v) => o.ThirdValue = v);

    public static readonly DirectProperty<RelativeRectEditor, float> FourthValueProperty =
        Vector4Editor<float>.FourthValueProperty.AddOwner<RelativeRectEditor>(
            o => o.FourthValue,
            (o, v) => o.FourthValue = v);

    public static readonly DirectProperty<RelativeRectEditor, Graphics.RelativeUnit> UnitProperty =
        AvaloniaProperty.RegisterDirect<RelativeRectEditor, Graphics.RelativeUnit>(
            nameof(Unit),
            o => o.Unit,
            (o, v) => o.Unit = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly RelativeVectorValues _values;

    public RelativeRectEditor()
    {
        Values = _values = new RelativeVectorValues(
            this,
            [FirstValueProperty, SecondValueProperty, ThirdValueProperty, FourthValueProperty],
            UnitProperty,
            static (n, newUnit, o, oldUnit, routedEvent) => new PropertyEditorValueChangedEventArgs<Graphics.RelativeRect>(
                new Graphics.RelativeRect(n[0], n[1], n[2], n[3], newUnit),
                new Graphics.RelativeRect(o[0], o[1], o[2], o[3], oldUnit),
                routedEvent));
        ThirdHeader = Strings.Width;
        FourthHeader = Strings.Height;
    }

    public float FirstValue
    {
        get => _values[0];
        set => _values.Set(0, value);
    }

    public float SecondValue
    {
        get => _values[1];
        set => _values.Set(1, value);
    }

    public float ThirdValue
    {
        get => _values[2];
        set => _values.Set(2, value);
    }

    public float FourthValue
    {
        get => _values[3];
        set => _values.Set(3, value);
    }

    public Graphics.RelativeUnit Unit
    {
        get => _values.Unit;
        set => _values.SetUnit(value);
    }

    public void Connect(IValueEditorContext<Graphics.RelativeRect> context)
    {
        CompositeDisposable connection = StartConnection();
        BindComponent(FirstValueProperty, context.EditValue.Select(v => v.Rect.X), connection);
        BindComponent(SecondValueProperty, context.EditValue.Select(v => v.Rect.Y), connection);
        BindComponent(ThirdValueProperty, context.EditValue.Select(v => v.Rect.Width), connection);
        BindComponent(FourthValueProperty, context.EditValue.Select(v => v.Rect.Height), connection);
        BindComponent(UnitProperty, context.EditValue.Select(v => v.Unit), connection);
        this.AddDisposableHandler(ValueChangedEvent, (_, e) =>
            {
                if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativeRect> args)
                {
                    Graphics.RelativeRect coerced = context.PreviewEdit(args.NewValue);
                    FirstValue = coerced.Rect.X;
                    SecondValue = coerced.Rect.Y;
                    ThirdValue = coerced.Rect.Width;
                    FourthValue = coerced.Rect.Height;
                    Unit = coerced.Unit;
                }
            })
            .DisposeWith(connection);
        ConfirmEdits(context, connection);
    }
}
