using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Models;
using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Signin : UserControl
    {
        public Signin()
        {
            var vm = new SigninViewModel(AppModel.Current.ServiceProvider);
            DataContext = vm;
            vm.SuccessSignin.Subscribe(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is SigninViewModel vm && Parent is FluentAvalonia.UI.Controls.Frame item)
                    {
                        item.Content = new User();
                    }
                });
            });
            InitializeComponent();
        }

        public void Signup(object s, RoutedEventArgs e)
        {
            if (Parent is FluentAvalonia.UI.Controls.Frame item)
            {
                item.Content = new Signup();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}