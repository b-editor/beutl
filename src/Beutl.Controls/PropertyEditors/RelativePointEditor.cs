using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.Extensibility;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class RelativePointEditor : Vector2Editor, IValueEditorControl<Graphics.RelativePoint>
{
    public static readonly DirectProperty<RelativePointEditor, float> FirstValueProperty =
        Vector4Editor<float>.FirstValueProperty.AddOwner<RelativePointEditor>(
            o => o.FirstValue,
            (o, v) => o.FirstValue = v);

    public static readonly DirectProperty<RelativePointEditor, float> SecondValueProperty =
        Vector4Editor<float>.SecondValueProperty.AddOwner<RelativePointEditor>(
            o => o.SecondValue,
            (o, v) => o.SecondValue = v);

    public static readonly DirectProperty<RelativePointEditor, Graphics.RelativeUnit> UnitProperty =
        AvaloniaProperty.RegisterDirect<RelativePointEditor, Graphics.RelativeUnit>(
            nameof(Unit),
            o => o.Unit,
            (o, v) => o.Unit = v,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly RelativeVectorValues _values;

    public RelativePointEditor()
    {
        Values = _values = new RelativeVectorValues(
            this,
            [FirstValueProperty, SecondValueProperty],
            UnitProperty,
            static (n, newUnit, o, oldUnit, routedEvent) => new PropertyEditorValueChangedEventArgs<Graphics.RelativePoint>(
                new Graphics.RelativePoint(n[0], n[1], newUnit),
                new Graphics.RelativePoint(o[0], o[1], oldUnit),
                routedEvent));
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

    public Graphics.RelativeUnit Unit
    {
        get => _values.Unit;
        set => _values.SetUnit(value);
    }

    public void Connect(IValueEditorContext<Graphics.RelativePoint> context)
    {
        CompositeDisposable connection = StartConnection();
        BindComponent(FirstValueProperty, context.EditValue.Select(v => v.Point.X), connection);
        BindComponent(SecondValueProperty, context.EditValue.Select(v => v.Point.Y), connection);
        BindComponent(UnitProperty, context.EditValue.Select(v => v.Unit), connection);
        this.AddDisposableHandler(ValueChangedEvent, (_, e) =>
            {
                if (e is PropertyEditorValueChangedEventArgs<Graphics.RelativePoint> args)
                {
                    Graphics.RelativePoint coerced = context.PreviewEdit(args.NewValue);
                    FirstValue = coerced.Point.X;
                    SecondValue = coerced.Point.Y;
                    Unit = coerced.Unit;
                }
            })
            .DisposeWith(connection);
        ConfirmEdits(context, connection);
    }
}
