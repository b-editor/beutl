using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Models;
using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public partial class Signup : UserControl
    {
        public Signup()
        {
            var vm = new SignupViewModel(AppModel.Current.ServiceProvider);
            DataContext = vm;
            vm.SuccessSignup.Subscribe(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is SignupViewModel vm && Parent is TabItem item && item.Parent is TabControl tab)
                    {
                        item.Content = new User();

                        // VisualTreeを更新
                        tab.SelectedItem = null;
                        tab.SelectedItem = item;
                    }
                });
            });
            InitializeComponent();
        }

        public void Signin(object s, RoutedEventArgs e)
        {
            if (Parent is TabItem item && item.Parent is TabControl tab)
            {
                item.Content = new Signin();

                // VisualTreeを更新
                tab.SelectedItem = null;
                tab.SelectedItem = item;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}