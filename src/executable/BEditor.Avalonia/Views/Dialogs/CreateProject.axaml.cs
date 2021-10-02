using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Controls;
using BEditor.ViewModels.Dialogs;

namespace BEditor.Views.Dialogs
{
    public sealed class CreateProject : FluentWindow
    {
        public CreateProject()
        {
            InitializeComponent();
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