using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BEditorNext.Controls;
using BEditorNext.Pages;
using FluentAvalonia.UI.Controls;

namespace BEditorNext.Views
{
    public sealed partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            TitleBarArea.PointerPressed += TitleBarArea_PointerPressed;

            EditPageItem.Tag = new EditPage();

            Navi.SelectedItem = EditPageItem;
            Navi.ItemInvoked += NavigationView_ItemInvoked;

            NaviContent.Content = EditPageItem.Tag;
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void TitleBarArea_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }

        private void NavigationView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
        {
            if (e.InvokedItemContainer is NavigationViewItem item)
            {
                NaviContent.Content = item.Tag;
                e.RecommendedNavigationTransitionInfo.RunAnimation(NaviContent);
            }
        }
    }
}
