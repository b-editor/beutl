using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Models;
using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Signup : UserControl
    {
        public Signup()
        {
            var vm = new SignupViewModel(AppModel.Current.ServiceProvider);
            DataContext = vm;
            vm.SuccessSignup.Subscribe(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is SignupViewModel vm && Parent is FluentAvalonia.UI.Controls.Frame item)
                    {
                        item.Content = new User();
                    }
                });
            });
            InitializeComponent();
        }

        public void Signin(object s, RoutedEventArgs e)
        {
            if (Parent is FluentAvalonia.UI.Controls.Frame item)
            {
                item.Content = new Signin();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}