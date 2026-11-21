using System.Numerics;
using System.Runtime.CompilerServices;

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

    public abstract void FormatText(int index, bool focused);

    public abstract void Snapshot();

    public abstract void RaiseConfirmedIfChanged();

    public abstract bool IsValid(string? text);

    // index is -1 for a text box that is not a component's.
    public abstract void Edit(int index, string? newText, string? oldText);

    // Returns false when the text does not parse.
    public abstract bool Step(int index, string? text, PointerWheelEventArgs e);

    // index is -1 for what scrubs every component: the editor's header, or the one box of a uniform value.
    public virtual void Scrub(int index, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
    }

    // The values after a scrub moves the component at index by delta, or every component for -1.
    protected static T[] Scrubbed<T>(T[] values, int index, T delta)
        where T : INumber<T>
    {
        T[] result = [.. values];
        for (int i = 0; i < result.Length; i++)
        {
            if (index < 0 || index == i)
                result[i] = NumberEditorHelper.AddPreservingScale(result[i], delta);
        }

        return result;
    }

    // One pulse when a value the handlers kept crossed its reference: the component of references, or zero.
    protected static void PulseIfCrossed<T>(
        VectorEditorBase editor, T[] oldValues, T[] values, ITuple? references, ulong timestamp)
        where T : INumber<T>
    {
        for (int i = 0; i < values.Length; i++)
        {
            T reference = references?[i] is T value ? value : T.Zero;
            if (NumberEditorHelper.CrossedHapticReference(oldValues[i], values[i], reference))
            {
                NativeHapticFeedback.PerformAlignment(editor, timestamp);
                break; // A uniform or whole-vector scrub produces one pulse per pointer event.
            }
        }
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

    public Func<TElement[]>? ReadIncrementValues { get; set; }

    public Func<TElement[]?>? ReadFocusedValues { get; set; }

    private string? NumberFormat => _editor.GetValue(Vector4Editor.NumberFormatProperty);

    private TElement LargeChange => _editor.GetValue(Vector4Editor<TElement>.LargeChangeProperty);

    private TElement SmallChange => _editor.GetValue(Vector4Editor<TElement>.SmallChangeProperty);

    public void Set(int index, TElement value)
    {
        if (_editor.SetComponent(_properties[index], ref _values[index], value))
        {
            FormatText(index, _editor.IsTextBoxFocused(index));
        }
    }

    public override void FormatTexts()
    {
        TElement[]? focused = ReadFocusedValues?.Invoke();
        for (int i = 0; i < _values.Length; i++)
        {
            TElement display = focused != null && _editor.IsTextBoxFocused(i) ? focused[i] : _values[i];
            _editor.SetText(i, NumberEditorHelper.Format(display, NumberFormat));
        }
    }

    public override void FormatText(int index, bool focused)
    {
        TElement display = focused && ReadFocusedValues?.Invoke() is { } values ? values[index] : _values[index];
        _editor.SetText(index, NumberEditorHelper.Format(display, NumberFormat));
    }

    public override void Snapshot()
    {
        _values.CopyTo(_oldValues, 0);
    }

    public override void RaiseConfirmedIfChanged()
    {
        for (int i = 0; i < _values.Length; i++)
        {
            if (_editor.HasUnconfirmedEdit || _values[i] != _oldValues[i])
            {
                _editor.RaiseEvent(_createEventArgs(_values, _oldValues, PropertyEditor.ValueConfirmedEvent));
                return;
            }
        }
    }

    public override bool IsValid(string? text)
    {
        return NumberEditorHelper.TryParse<TElement>(text, out _);
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

        if (ReadFocusedValues?.Invoke() is { } focused)
        {
            ApplyFocusedValueChange(index, newValue, focused, oldValues);
            return;
        }

        ApplyValueChange(index, newValue, oldValues);
    }

    public override bool Step(int index, string? text, PointerWheelEventArgs e)
    {
        if (!NumberEditorHelper.TryParse(text, out TElement? value) || value is null)
            return false;

        if (ReadFocusedValues?.Invoke() is { } focused)
        {
            TElement oldFocusedValue = focused[Math.Max(index, 0)];
            value = NumberEditorHelper.StepByWheel(oldFocusedValue, e, LargeChange, SmallChange);
            if (value != oldFocusedValue)
                ApplyFocusedValueChange(index, value, focused, [.. _values]);

            return true;
        }

        TElement[] input = ReadIncrementValues?.Invoke() ?? [.. _values];
        TElement oldValue = input[Math.Max(index, 0)];
        value = NumberEditorHelper.StepByWheel(oldValue, e, LargeChange, SmallChange);
        if (value != oldValue)
            ApplyValueChange(index, value, [.. _values], input);

        return true;
    }

    private void ApplyFocusedValueChange(int index, TElement value, TElement[] focusedValues, TElement[] oldValues)
    {
        TElement[] newValues = [.. focusedValues];
        if (_editor.IsUniformValue)
        {
            Array.Fill(newValues, value);
        }
        else if (index >= 0)
        {
            newValues[index] = value;
        }

        _editor.RaiseUnderlyingValueChanged(_createEventArgs(newValues, oldValues, PropertyEditor.ValueChangedEvent));
    }

    private void ApplyValueChange(int index, TElement value, TElement[] oldValues, TElement[]? input = null)
    {
        TElement[] newValues = input ?? [.. _values];
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

        TElement[] oldValues = [.. _values];
        TElement[] newValues = Scrubbed(ReadIncrementValues?.Invoke() ?? oldValues, index, delta);
        for (int i = 0; i < newValues.Length; i++)
            Set(i, newValues[i]);

        _editor.RaiseEvent(_createEventArgs(newValues, oldValues, PropertyEditor.ValueChangedEvent));

        // The handlers have written the accepted values back, including shared-axis constraints.
        PulseIfCrossed(_editor, oldValues, _values, _editor.HapticReferenceValues, e.Timestamp);
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

    public override bool CanScrub => true;

    public float this[int index] => _values[index];

    public RelativeUnit Unit => _unit;

    public Func<float[]>? ReadIncrementValues { get; set; }

    public Func<(float[] Values, RelativeUnit Unit)?>? ReadFocusedValues { get; set; }

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

    private static string FormatRelative(float value, RelativeUnit unit)
        => unit == RelativeUnit.Relative ? $"{value * 100:f}%" : $"{value}";

    public override void FormatTexts()
    {
        (float[] Values, RelativeUnit Unit)? focused = ReadFocusedValues?.Invoke();
        for (int i = 0; i < _values.Length; i++)
        {
            bool useFocused = focused != null && _editor.IsTextBoxFocused(i);
            float value = useFocused ? focused!.Value.Values[i] : _values[i];
            RelativeUnit unit = useFocused ? focused!.Value.Unit : _unit;
            _editor.SetText(i, FormatRelative(value, unit));
        }
    }

    public override void FormatText(int index, bool focused)
    {
        if (focused && ReadFocusedValues?.Invoke() is { } underlying)
        {
            _editor.SetText(index, FormatRelative(underlying.Values[index], underlying.Unit));
        }
        else
        {
            _editor.SetText(index, FormatRelative(_values[index], _unit));
        }
    }

    public override void Snapshot()
    {
        _values.CopyTo(_oldValues, 0);
        _oldUnit = _unit;
    }

    public override void RaiseConfirmedIfChanged()
    {
        bool changed = _editor.HasUnconfirmedEdit || _unit != _oldUnit;
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

        if (ReadFocusedValues?.Invoke() is { } focused)
        {
            ApplyFocusedValueChange(index, newValue, newUnit, focused.Values, oldValues, oldUnit);
            return;
        }

        ApplyValueChange(index, newValue, newUnit, oldValues, oldUnit);
    }

    public override bool Step(int index, string? text, PointerWheelEventArgs e)
    {
        if (!RelativeUnitParser.TryParse(text, out float value, out _))
            return false;

        if (ReadFocusedValues?.Invoke() is { } focused)
        {
            float oldFocusedValue = focused.Values[Math.Max(index, 0)];
            value = RelativeUnitParser.StepByWheel(oldFocusedValue, focused.Unit, e);
            if (value != oldFocusedValue)
                ApplyFocusedValueChange(index, value, focused.Unit, focused.Values, [.. _values], _unit);

            return true;
        }

        float[] input = ReadIncrementValues?.Invoke() ?? [.. _values];
        float oldValue = input[Math.Max(index, 0)];
        value = RelativeUnitParser.StepByWheel(oldValue, _unit, e);
        if (value != oldValue)
            ApplyValueChange(index, value, _unit, [.. _values], _unit, input);

        return true;
    }

    private void ApplyFocusedValueChange(int index, float value, RelativeUnit unit, float[] focusedValues, float[] oldValues, RelativeUnit oldUnit)
    {
        float[] newValues = [.. focusedValues];
        if (_editor.IsUniformValue)
        {
            Array.Fill(newValues, value);
        }
        else if (index >= 0)
        {
            newValues[index] = value;
        }

        _editor.RaiseUnderlyingValueChanged(_createEventArgs(newValues, unit, oldValues, oldUnit, PropertyEditor.ValueChangedEvent));
    }

    // A pixel is a percent of a relative value and a whole unit of an absolute one, the small step of the wheel.
    public override void Scrub(int index, ref HeaderScrubGesture scrub, Visual header, PointerEventArgs e)
    {
        float delta = scrub.NextDelta<float>(header, e) * (_unit == RelativeUnit.Relative ? 0.01f : 1f);

        float[] oldValues = [.. _values];
        float[] newValues = Scrubbed(ReadIncrementValues?.Invoke() ?? oldValues, index, delta);
        for (int i = 0; i < newValues.Length; i++)
            Set(i, newValues[i]);

        _editor.RaiseEvent(_createEventArgs(newValues, _unit, oldValues, _unit, PropertyEditor.ValueChangedEvent));

        // The handlers have written the accepted values back.
        PulseIfCrossed(_editor, oldValues, _values, null, e.Timestamp);
    }

    private void ApplyValueChange(int index, float value, RelativeUnit unit, float[] oldValues, RelativeUnit oldUnit, float[]? input = null)
    {
        float[] newValues = input ?? [.. _values];
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
