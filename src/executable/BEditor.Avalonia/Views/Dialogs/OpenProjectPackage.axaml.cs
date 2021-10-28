using System;

using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Controls;
using BEditor.ViewModels.Dialogs;

namespace BEditor.Views.Dialogs
{
    public sealed class OpenProjectPackage : FluentWindow
    {
        public OpenProjectPackage()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            Close(OpenProjectPackageViewModel.State.Close);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is OpenProjectPackageViewModel vm)
            {
                vm.Close.Subscribe(s => Dispatcher.UIThread.InvokeAsync(() => Close(s)));
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}