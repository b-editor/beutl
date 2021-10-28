
using Avalonia.Controls;

using BEditor.Controls;

namespace BEditor.Views.DialogContent
{
    public sealed class EmptyDialog : FluentWindow
    {
        private readonly Border _border;

        public EmptyDialog(IDialogContent content) : this()
        {
            _border.Child = content;

            content.ButtonClicked += (_, _) => Close();
        }

        public EmptyDialog(IControl content) : this()
        {
            _border.Child = content;
        }

        private EmptyDialog()
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _border = new Border();

            Content = _border;

            PointerPressed += EmptyDialog_PointerPressed;
        }

        private void EmptyDialog_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }
    }
}