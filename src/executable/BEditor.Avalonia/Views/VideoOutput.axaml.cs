using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BEditor.Controls;
using BEditor.LangResources;
using BEditor.ViewModels;

using FluentAvalonia.UI.Controls;

namespace BEditor.Views
{
    public sealed class VideoOutput : FluentWindow
    {
        private readonly VideoOutputViewModel _viewModel;
        private readonly NavigationView _navView;

        public VideoOutput()
        {
            _viewModel = new VideoOutputViewModel();
            DataContext = _viewModel;
            InitializeComponent();

            _viewModel.Output.Subscribe(Close);
#if DEBUG
            this.AttachDevTools();
#endif
            _navView = this.Find<NavigationView>("NavView");

            _navView.ItemInvoked += NavView_ItemInvoked;

            AddNavigationViewMenuItems();

            var first = _navView.MenuItems.OfType<NavigationViewItemBase>().FirstOrDefault();
            _navView.Content = first?.Tag;
            _navView.SelectedItem = first;
        }

        private void AddNavigationViewMenuItems()
        {
            _navView.MenuItems = new List<NavigationViewItemBase>
            {
                new NavigationViewItem
                {
                    Content = Strings.Infomation,
                    Icon = new FluentAvalonia.UI.Controls.PathIcon { Data = (Geometry)App.Current.FindResource("Info20Regular")! },
                    Tag = new VideoOutputPages.Infomation(),
                },
                new NavigationViewItem
                {
                    Content = Strings.Video,
                    Icon = new SymbolIcon { Symbol = Symbol.Video },
                    Tag = new VideoOutputPages.Video(),
                },
                new NavigationViewItem
                {
                    Content = Strings.Audio,
                    Icon = new SymbolIcon { Symbol = Symbol.Speaker2 },
                    Tag = new VideoOutputPages.Audio()
                },
                new NavigationViewItem
                {
                    Content = Strings.Metadata,
                    Icon = new SymbolIcon { Symbol = Symbol.Tag },
                    Tag = new VideoOutputPages.Metadata()
                },
                new NavigationViewItem
                {
                    Content = Strings.Output,
                    Icon = new SymbolIcon { Symbol = Symbol.Import },
                    Tag = new VideoOutputPages.Output()
                },
            };
        }

        private void NavView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
        {
            if (e.InvokedItemContainer is NavigationViewItem nvi)
            {
                _navView.Content = nvi.Tag;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}