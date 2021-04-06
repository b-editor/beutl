using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.DialogContent
{
    public class CreateProject : UserControl, IDialogContent
    {
        public CreateProject()
        {
            InitializeComponent();
        }

        public IMessage.ButtonType DialogResult { get; }

        public event EventHandler? ButtonClicked;

        public void CloseClick(object s, RoutedEventArgs e)
        {
            ButtonClicked?.Invoke(this, EventArgs.Empty);
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
