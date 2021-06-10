using Avalonia.Controls;
using Avalonia.Input;

namespace BEditor.Controls
{
    public class SeekTrackButton : Button
    {
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Handled = false;
        }

        protected override void OnPointerEnter(PointerEventArgs e)
        {
            e.Handled = false;
        }

        protected override void OnPointerLeave(PointerEventArgs e)
        {
            e.Handled = false;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            e.Handled = false;
        }
    }
}