using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Beutl.Controls;

public static class TextBoxAttachment
{
    public static readonly AttachedProperty<EnterBehaviorMode> EnterDownBehaviorProperty =
        AvaloniaProperty.RegisterAttached<TextBox, EnterBehaviorMode>("EnterDownBehavior", typeof(TextBoxAttachment), EnterBehaviorMode.None);

    static TextBoxAttachment()
    {
        EnterDownBehaviorProperty.Changed.Subscribe(ev =>
        {
            if (ev.Sender is TextBox tb && ev.NewValue.HasValue)
            {
                if (ev.NewValue.Value != EnterBehaviorMode.None)
                {
                    tb.AddHandler(InputElement.KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);
                }
                else
                {
                    tb.RemoveHandler(InputElement.KeyDownEvent, OnTextBoxKeyDown);
                }
            }
        });
    }

    public enum EnterBehaviorMode
    {
        None,
        LostFocus,
        Auto
    }

    public static EnterBehaviorMode GetEnterDownBehavior(TextBox obj)
    {
        return obj.GetValue(EnterDownBehaviorProperty);
    }

    public static void SetEnterDownBehavior(TextBox obj, EnterBehaviorMode value)
    {
        obj.SetValue(EnterDownBehaviorProperty, value);
    }

    private static void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb
            && e.Key == Key.Enter)
        {
            EnterBehaviorMode mode = GetEnterDownBehavior(tb);
            if (mode == EnterBehaviorMode.None) return;
            if (mode == EnterBehaviorMode.Auto && tb.AcceptsReturn && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

            foreach (InputElement item in tb.GetLogicalSiblings().SkipWhile(v => v != tb).Skip(1).OfType<InputElement>())
            {
                if (item.Focus())
                {
                    e.Handled = true;
                    return;
                }
            }


            foreach (InputElement item in tb.GetLogicalAncestors().OfType<InputElement>())
            {
                if (item.Focus())
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}
