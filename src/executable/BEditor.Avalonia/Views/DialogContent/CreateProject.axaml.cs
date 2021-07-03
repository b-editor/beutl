using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels.DialogContent;

namespace BEditor.Views.DialogContent
{
    public sealed class CreateProject : FluentWindow
    {
        public CreateProject()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is CreateProjectViewModel vm)
            {
                vm.Create.Subscribe(() => Dispatcher.UIThread.InvokeAsync(Close));
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}