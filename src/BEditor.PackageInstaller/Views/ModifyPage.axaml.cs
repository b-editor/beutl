using System;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.PackageInstaller.ViewModels;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.PackageInstaller.Views
{
    public partial class ModifyPage : UserControl
    {
        public ModifyPage()
        {
            InitializeComponent();
        }

        public void CancelClick(object s, RoutedEventArgs e)
        {
            if (Parent is Window window)
            {
                window.Close();
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is ModifyPageViewModel vm)
            {
                vm.CompleteModify.Subscribe(i =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (Parent is Window window)
                        {
                            window.Content = new CompletePage
                            {
                                DataContext = new CompletePageViewModel(i.Item1, i.Item2)
                            };
                        }
                    });
                });
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}