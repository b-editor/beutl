using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.Models;

namespace BEditor.Views.ManagePlugins
{
    public partial class User : UserControl
    {
        public User()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            if (AppModel.Current.User is null && Parent is TabItem item && item.Parent is TabControl tab)
            {
                item.Content = new Signin();
                tab.SelectedItem = null;
                tab.SelectedItem = item;
            }

            base.OnAttachedToLogicalTree(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}