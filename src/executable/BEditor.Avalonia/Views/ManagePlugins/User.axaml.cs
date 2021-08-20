using Avalonia;
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
                DataContext = new UserViewModel(AppModel.Current.User);
            }
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (AppModel.Current.User is null
                && Parent is FluentAvalonia.UI.Controls.Frame item)
            {
                item.Content = new Signin();
            }

            base.OnAttachedToVisualTree(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}