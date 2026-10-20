using System.Globalization;
using System.Numerics;

using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.Controls.PropertyEditors;

// The values behind the text boxes of a VectorEditorBase, which calls these by component index.
internal abstract class VectorValues
{
    public virtual bool CanScrub => false;

    public abstract void FormatTexts();

    public abstract void Snapshot();

    public abstract void RaiseConfirmedIfChanged();

    public abstract bool IsValid(string? text);

    // index is -1 for a text box that is not a component's.
    public abstract void Edit(int index, string? newText, string? oldText);

    // Returns false when the text does not parse.
    public abstract bool Step(int index, string? text, PointerWheelEventArgs e);

    // index is -1 for the editor's header, which scrubs every component.
    public virtual void Scrub(int index, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
    }
}

// The values of Vector2Editor<T>, Vector3Editor<T> and Vector4Editor<T>.
internal sealed class NumberVectorValues<TElement> : VectorValues
    where TElement : INumber<TElement>
{
    private readonly VectorEditorBase _editor;
    private readonly DirectPropertyBase<TElement>[] _properties;
    private readonly Func<TElement[], TElement[], RoutedEvent, RoutedEventArgs> _createEventArgs;
    private readonly TElement[] _values;
    private readonly TElement[] _oldValues;

    // createEventArgs builds the event's tuple from the new and the old values.
    public NumberVectorValues(
        VectorEditorBase editor,
        DirectPropertyBase<TElement>[] properties,
        Func<TElement[], TElement[], RoutedEvent, RoutedEventArgs> createEventArgs)
    {
        _editor = editor;
        _properties = properties;
        _createEventArgs = createEventArgs;
        _values = new TElement[properties.Length];
        _oldValues = new TElement[properties.Length];
        Array.Fill(_values, TElement.Zero);
        Array.Fill(_oldValues, TElement.Zero);
    }

    public override bool CanScrub => true;

    public TElement this[int index] => _values[index];

    private string? NumberFormat => _editor.GetValue(Vector4Editor.NumberFormatProperty);

    private TElement LargeChange => _editor.GetValue(Vector4Editor<TElement>.LargeChangeProperty);

    private TElement SmallChange => _editor.GetValue(Vector4Editor<TElement>.SmallChangeProperty);

    public void Set(int index, TElement value)
    {
        if (_editor.SetComponent(_properties[index], ref _values[index], value))
        {
            _editor.SetText(index, NumberEditorHelper.Format(value, NumberFormat));
        }
    }

    public override void FormatTexts()
    {
        for (int i = 0; i < _values.Length; i++)
            _editor.SetText(i, NumberEditorHelper.Format(_values[i], NumberFormat));
    }

    public override void Snapshot()
    {
        _values.CopyTo(_oldValues, 0);
    }

    public override void RaiseConfirmedIfChanged()
    {
        for (int i = 0; i < _values.Length; i++)
        {
            if (_values[i] != _oldValues[i])
            {
                _editor.RaiseEvent(_createEventArgs(_values, _oldValues, PropertyEditor.ValueConfirmedEvent));
                return;
            }
        }
    }

    public override bool IsValid(string? text)
    {
        return TElement.TryParse(text, CultureInfo.CurrentCulture, out _);
    }

    public override void Edit(int index, string? newText, string? oldText)
    {
        if (!NumberEditorHelper.TryParseEdit(newText, oldText, out TElement? newValue, out TElement? oldValue))
            return;

        TElement[] oldValues = [.. _values];
        if (_editor.IsUniformValue)
        {
            Array.Fill(oldValues, oldValue);
        }
        else if (index >= 0)
        {
            oldValues[index] = oldValue;
        }

        ApplyValueChange(index, newValue, oldValues);
    }

    public override bool Step(int index, string? text, PointerWheelEventArgs e)
    {
        if (!TElement.TryParse(text, CultureInfo.CurrentCulture, out TElement? value) || value is null)
            return false;

        TElement oldValue = index >= 0 ? _values[index] : value;
        value = NumberEditorHelper.StepByWheel(oldValue, e, LargeChange, SmallChange);
        if (value != oldValue)
            ApplyValueChange(index, value, [.. _values]);

        return true;
    }

    private void ApplyValueChange(int index, TElement value, TElement[] oldValues)
    {
        TElement[] newValues = [.. _values];
        if (_editor.IsUniformValue)
        {
            SetAll(value);
            Array.Fill(newValues, value);
        }
        else if (index >= 0)
        {
            Set(index, value);
            newValues[index] = value;
        }

        _editor.RaiseEvent(_createEventArgs(newValues, oldValues, PropertyEditor.ValueChangedEvent));
    }

    public override void Scrub(int index, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
        TElement delta = scrub.NextDelta<TElement>(header, e) * SmallChange;

        TElement[] newValues = [.. _values];
        TElement[] oldValues = [.. _values];
        for (int i = 0; i < newValues.Length; i++)
        {
            if (index < 0 || index == i)
                newValues[i] = NumberEditorHelper.AddPreservingScale(newValues[i], delta);
        }

        for (int i = 0; i < newValues.Length; i++)
            Set(i, newValues[i]);

        _editor.RaiseEvent(_createEventArgs(newValues, oldValues, PropertyEditor.ValueChangedEvent));
    }

    // From the last component to the first, the order of FirstValue = SecondValue = value.
    private void SetAll(TElement value)
    {
        for (int i = _values.Length - 1; i >= 0; i--)
            Set(i, value);
    }
}

// The values of RelativePointEditor and RelativeRectEditor, which share one RelativeUnit.
internal sealed class RelativeVectorValues : VectorValues
{
    private readonly VectorEditorBase _editor;
    private readonly DirectPropertyBase<float>[] _properties;
    private readonly DirectPropertyBase<RelativeUnit> _unitProperty;
    private readonly Func<float[], RelativeUnit, float[], RelativeUnit, RoutedEvent, RoutedEventArgs> _createEventArgs;
    private readonly float[] _values;
    private readonly float[] _oldValues;
    private RelativeUnit _unit;
    private RelativeUnit _oldUnit;

    // createEventArgs builds the event's value from the new values and unit and the old ones.
    public RelativeVectorValues(
        VectorEditorBase editor,
        DirectPropertyBase<float>[] properties,
        DirectPropertyBase<RelativeUnit> unitProperty,
        Func<float[], RelativeUnit, float[], RelativeUnit, RoutedEvent, RoutedEventArgs> createEventArgs)
    {
        _editor = editor;
        _properties = properties;
        _unitProperty = unitProperty;
        _createEventArgs = createEventArgs;
        _values = new float[properties.Length];
        _oldValues = new float[properties.Length];
    }

    public float this[int index] => _values[index];

    public RelativeUnit Unit => _unit;

    public void Set(int index, float value)
    {
        if (_editor.SetComponent(_properties[index], ref _values[index], value))
        {
            FormatTexts();
        }
    }

    public void SetUnit(RelativeUnit unit)
    {
        if (_editor.SetComponent(_unitProperty, ref _unit, unit))
        {
            FormatTexts();
        }
    }

    public override void FormatTexts()
    {
        for (int i = 0; i < _values.Length; i++)
            _editor.SetText(i, _unit == RelativeUnit.Relative ? $"{_values[i] * 100:f}%" : $"{_values[i]}");
    }

    public override void Snapshot()
    {
        _values.CopyTo(_oldValues, 0);
        _oldUnit = _unit;
    }

    public override void RaiseConfirmedIfChanged()
    {
        bool changed = _unit != _oldUnit;
        for (int i = 0; i < _values.Length && !changed; i++)
            changed = _values[i] != _oldValues[i];

        if (changed)
        {
            _editor.RaiseEvent(_createEventArgs(_values, _unit, _oldValues, _oldUnit, PropertyEditor.ValueConfirmedEvent));
        }
    }

    public override bool IsValid(string? text)
    {
        return RelativeUnitParser.TryParse(text, out _, out _);
    }

    public override void Edit(int index, string? newText, string? oldText)
    {
        if (!RelativeUnitParser.TryParseEdit(
            newText, oldText,
            out float newValue, out RelativeUnit newUnit,
            out float oldValue, out RelativeUnit oldUnit))
        {
            return;
        }

        float[] oldValues = [.. _values];
        if (_editor.IsUniformValue)
        {
            Array.Fill(oldValues, oldValue);
        }
        else if (index >= 0)
        {
            oldValues[index] = oldValue;
        }

        ApplyValueChange(index, newValue, newUnit, oldValues, oldUnit);
    }

    public override bool Step(int index, string? text, PointerWheelEventArgs e)
    {
        if (!RelativeUnitParser.TryParse(text, out float value, out _))
            return false;

        float oldValue = index >= 0 ? _values[index] : value;
        value = RelativeUnitParser.StepByWheel(oldValue, _unit, e);
        if (value != oldValue)
            ApplyValueChange(index, value, _unit, [.. _values], _unit);

        return true;
    }

    private void ApplyValueChange(int index, float value, RelativeUnit unit, float[] oldValues, RelativeUnit oldUnit)
    {
        float[] newValues = [.. _values];
        SetUnit(unit);
        if (_editor.IsUniformValue)
        {
            SetAll(value);
            Array.Fill(newValues, value);
        }
        else if (index >= 0)
        {
            Set(index, value);
            newValues[index] = value;
        }

        _editor.RaiseEvent(_createEventArgs(newValues, unit, oldValues, oldUnit, PropertyEditor.ValueChangedEvent));
    }

    // From the last component to the first, the order of FirstValue = SecondValue = value.
    private void SetAll(float value)
    {
        for (int i = _values.Length - 1; i >= 0; i--)
            Set(i, value);
    }
}
