using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Beutl.Controls;

public static partial class TextBoxAttachment
{
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

    [GeneratedAttachedProperty(DefaultValue = EnterBehaviorMode.None)]
    public static partial EnterBehaviorMode GetEnterDownBehavior(TextBox obj);

    private static void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox tb
            && e.Key == Key.Enter)
        {
            EnterBehaviorMode mode = GetEnterDownBehavior(tb);
            if (mode == EnterBehaviorMode.None) return;
            if (mode == EnterBehaviorMode.Auto && tb.AcceptsReturn
                && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
            {
                return;
            }

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
