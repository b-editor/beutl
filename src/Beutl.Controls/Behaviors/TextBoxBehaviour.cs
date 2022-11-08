
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace Beutl.Controls.Behaviors;

public sealed class TextBoxBehaviour : Behavior<TextBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject != null)
        {
        }
    }

}
