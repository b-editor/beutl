using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Dialogs;
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
                    if (DataContext is SigninViewModel vm && Parent is TabItem item && item.Parent is TabControl tab)
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

        public void Signup(object s, RoutedEventArgs e)
        {
            if (Parent is TabItem item && item.Parent is TabControl tab)
            {
                item.Content = new Signup();

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