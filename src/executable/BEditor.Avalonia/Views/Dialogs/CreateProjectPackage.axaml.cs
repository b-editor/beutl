using System;

using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels.Dialogs;

namespace BEditor.Views.Dialogs
{
    public partial class CreateProjectPackage : FluentWindow
    {
        public CreateProjectPackage()
        {
            DataContext = new CreateProjectPackageViewModel();
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

            if (DataContext is CreateProjectPackageViewModel vm)
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
