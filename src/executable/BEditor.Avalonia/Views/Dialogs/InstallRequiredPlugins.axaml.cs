using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels.Dialogs;

namespace BEditor.Views.Dialogs
{
    public partial class InstallRequiredPlugins : FluentWindow
    {
        public InstallRequiredPlugins()
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

            if (DataContext is InstallRequiredPluginsViewModel vm)
            {
                vm.InstallNow.Subscribe(() => App.Shutdown(0));
                vm.InstallLater.Subscribe(() => Dispatcher.UIThread.InvokeAsync(Close));
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
