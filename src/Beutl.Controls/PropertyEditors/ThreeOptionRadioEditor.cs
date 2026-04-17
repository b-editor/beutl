using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public abstract class ThreeOptionRadioEditor<T> : PropertyEditor
    where T : struct
{
    private readonly CompositeDisposable _disposables = [];
    private RadioButton _buttonA;
    private RadioButton _buttonB;
    private RadioButton _buttonC;

    protected T _value;

    protected abstract string ButtonAName { get; }
    protected abstract string ButtonBName { get; }
    protected abstract string ButtonCName { get; }

    protected abstract T ButtonAValue { get; }
    protected abstract T ButtonBValue { get; }
    protected abstract T ButtonCValue { get; }

    protected abstract string PseudoClassA { get; }
    protected abstract string PseudoClassB { get; }
    protected abstract string PseudoClassC { get; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _buttonA = e.NameScope.Get<RadioButton>(ButtonAName);
        _buttonB = e.NameScope.Get<RadioButton>(ButtonBName);
        _buttonC = e.NameScope.Get<RadioButton>(ButtonCName);

        _buttonA.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        _buttonB.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        _buttonC.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);

        UpdatePseudoClassesAndCheckState();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        if (!double.IsInfinity(availableSize.Width))
        {
            if (availableSize.Width <= 224)
            {
                if (!PseudoClasses.Contains(":compact"))
                {
                    PseudoClasses.Add(":compact");
                }
            }
            else
            {
                if (EditorStyle != PropertyEditorStyle.Compact)
                    PseudoClasses.Remove(":compact");
            }
        }

        return measured;
    }

    protected abstract void SetValue(T value);

    protected void UpdatePseudoClassesAndCheckState()
    {
        PseudoClasses.Remove(PseudoClassA);
        PseudoClasses.Remove(PseudoClassB);
        PseudoClasses.Remove(PseudoClassC);
        string pseudoClass = null;
        RadioButton radioButton = null;

        var comparer = EqualityComparer<T>.Default;
        if (comparer.Equals(_value, ButtonAValue))
        {
            pseudoClass = PseudoClassA;
            radioButton = _buttonA;
        }
        else if (comparer.Equals(_value, ButtonBValue))
        {
            pseudoClass = PseudoClassB;
            radioButton = _buttonB;
        }
        else if (comparer.Equals(_value, ButtonCValue))
        {
            pseudoClass = PseudoClassC;
            radioButton = _buttonC;
        }

        if (radioButton != null)
        {
            radioButton.IsChecked = true;
        }

        if (pseudoClass != null)
        {
            PseudoClasses.Add(pseudoClass);
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        T? value =
            button.Name == ButtonAName ? ButtonAValue :
            button.Name == ButtonBName ? ButtonBValue :
            button.Name == ButtonCName ? ButtonCValue :
            null;

        if (!value.HasValue)
            return;

        T oldValue = _value;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<T>(value.Value, oldValue, ValueChangedEvent));
        SetValue(value.Value);
        RaiseEvent(new PropertyEditorValueChangedEventArgs<T>(value.Value, oldValue, ValueConfirmedEvent));
    }
}
