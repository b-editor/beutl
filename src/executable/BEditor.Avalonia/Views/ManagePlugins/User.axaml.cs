using System.IO;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.Models;
using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class User : UserControl
    {
        public User()
        {
            if (AppModel.Current.User is not null)
            {
                var vm = new UserViewModel(AppModel.Current.User);
                vm.SignOut.Subscribe(() =>
                {
                    var file = Path.Combine(ServicesLocator.GetUserFolder(), "token");
                    if (File.Exists(file))
                        File.Delete(file);

                    if (Parent is FluentAvalonia.UI.Controls.Frame item)
                    {
                        item.Content = new Signin();
                    }
                });
                DataContext = vm;
            }
            InitializeComponent();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);
            if (AppModel.Current.User is null
                && Parent is FluentAvalonia.UI.Controls.Frame item)
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