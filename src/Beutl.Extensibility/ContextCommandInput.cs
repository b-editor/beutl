using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Beutl.Extensibility;

/// <summary>
/// Describes which parts of the UI accept typed text so that plain-key context command gestures
/// (for example <c>Space</c>, <c>J</c>, <c>K</c>, <c>L</c>, <c>M</c>) are not treated as shortcuts while the
/// user is typing into them.
/// </summary>
public static class ContextCommandInput
{
    /// <summary>
    /// Marks a control and its descendants as a text-entry surface. Context command handlers use
    /// <see cref="IsFromTextInput(KeyEventArgs?)"/> to skip plain-key gestures whose source lies inside it.
    /// <see cref="TextBox"/> is always treated as text input; use this for custom editors such as a terminal.
    /// </summary>
    public static readonly AttachedProperty<bool> IsTextInputProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("IsTextInput", typeof(ContextCommandInput));

    public static bool GetIsTextInput(Visual element)
    {
        return element.GetValue(IsTextInputProperty);
    }

    public static void SetIsTextInput(Visual element, bool value)
    {
        element.SetValue(IsTextInputProperty, value);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the key event originates from a control that accepts typed text:
    /// a <see cref="TextBox"/>, or any control at or below one marked with <see cref="IsTextInputProperty"/>.
    /// </summary>
    public static bool IsFromTextInput(KeyEventArgs? args)
    {
        if (args?.Source is not Visual source)
        {
            return false;
        }

        foreach (Visual visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is TextBox || GetIsTextInput(visual))
            {
                return true;
            }
        }

        return false;
    }
}
