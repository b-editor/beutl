using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(FocusAnyTextBox, BorderPointerOver)]
[TemplatePart("PART_InnerFirstTextBox", typeof(TextBox))]
[TemplatePart("PART_InnerSecondTextBox", typeof(TextBox))]
[TemplatePart("PART_BackgroundBorder", typeof(Border))]
public class Vector2Editor : PropertyEditor, IStyleable
{
    private const string FocusAnyTextBox = ":focus-any-textbox";
    private const string BorderPointerOver = ":border-pointerover";
    private Border _backgroundBorder;

    protected TextBox InnerFirstTextBox { get; private set; }

    protected TextBox InnerSecondTextBox { get; private set; }

    Type IStyleable.StyleKey => typeof(Vector2Editor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InnerFirstTextBox = e.NameScope.Get<TextBox>("PART_InnerFirstTextBox");
        InnerSecondTextBox = e.NameScope.Get<TextBox>("PART_InnerSecondTextBox");
        _backgroundBorder = e.NameScope.Get<Border>("PART_BackgroundBorder");

        InnerFirstTextBox.GotFocus += OnInnerTextBoxGotFocus;
        InnerSecondTextBox.GotFocus += OnInnerTextBoxGotFocus;
        InnerFirstTextBox.LostFocus += OnInnerTextBoxLostFocus;
        InnerSecondTextBox.LostFocus += OnInnerTextBoxLostFocus;

        InnerFirstTextBox.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
        InnerSecondTextBox.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
        _backgroundBorder.GetObservable(IsPointerOverProperty).Subscribe(IsPointerOverChanged);
    }

    private void IsPointerOverChanged(bool obj)
    {
        if (InnerFirstTextBox.IsPointerOver
            || InnerSecondTextBox.IsPointerOver
            || _backgroundBorder.IsPointerOver)
        {
            PseudoClasses.Add(BorderPointerOver);
        }
        else
        {
            PseudoClasses.Remove(BorderPointerOver);
        }
    }

    private void OnInnerTextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        UpdateFocusState();
    }

    private void OnInnerTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        UpdateFocusState();
    }

    private void UpdateFocusState()
    {
        if (InnerFirstTextBox.IsFocused || InnerSecondTextBox.IsFocused)
        {
            PseudoClasses.Add(FocusAnyTextBox);
        }
        else
        {
            PseudoClasses.Remove(FocusAnyTextBox);
        }
    }
}
