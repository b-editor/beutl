using System.Reactive.Linq;

using Avalonia;
using Avalonia.Data;

using Beutl.Extensibility;

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
        ConnectComponents(
            context,
            connection =>
            {
                _values.ReadIncrementValues = () =>
                {
                    var value = context.GetIncrementValue(new Graphics.RelativePoint(FirstValue, SecondValue, Unit));
                    return [value.Point.X, value.Point.Y];
                };
                _values.ReadFocusedValues = () => context.TryGetFocusedEditValue(out var focused)
                    ? ([focused.Point.X, focused.Point.Y], focused.Unit)
                    : null;
                BindComponent(FirstValueProperty, context.EditValue.Select(v => v.Point.X), connection);
                BindComponent(SecondValueProperty, context.EditValue.Select(v => v.Point.Y), connection);
                BindComponent(UnitProperty, context.EditValue.Select(v => v.Unit), connection);
            },
            value =>
            {
                FirstValue = value.Point.X;
                SecondValue = value.Point.Y;
                Unit = value.Unit;
            },
            reportsMismatch: false);
    }
}
