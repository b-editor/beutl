using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Rendering;

using BEditor.Models;
using BEditor.Properties;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BEditor.Views.ManagePlugins
{
    public partial class ManagePluginsWindow : FluentWindow
    {
        private readonly NavigationView _navView;
        private readonly Frame _frame;

        public ManagePluginsWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            _navView = this.Find<NavigationView>("NavView");
            _frame = this.Find<Frame>("FrameView");

            _navView.ItemInvoked += NavView_ItemInvoked;

            AddNavigationViewMenuItems();
            _frame.Navigated += OnFrameNavigated;

            _frame.Navigate(typeof(LoadedPlugins));
        }

        private NavigationViewItem? GetNVIFromPageSourceType(IEnumerable items, Type t)
        {
            foreach (var item in items)
            {
                if (item is NavigationViewItem nvi)
                {
                    if (nvi.MenuItems?.Count() > 0)
                    {
                        var inner = GetNVIFromPageSourceType(nvi.MenuItems, t);
                        if (inner == null)
                            continue;

                        return inner;
                    }
                    else if (nvi.Tag is Type tag && tag == t)
                    {
                        return nvi;
                    }
                }
            }

            return null;
        }

        private void OnFrameNavigated(object? sender, NavigationEventArgs e)
        {
            var nvi = GetNVIFromPageSourceType(_navView.MenuItems, e.SourcePageType);
            if (nvi != null)
            {
                _navView.SelectedItem = nvi;
                _navView.AlwaysShowHeader = false;
                _navView.Header = nvi.Content;
            }
        }

        private void AddNavigationViewMenuItems()
        {
            _navView.MenuItems = new List<NavigationViewItemBase>
            {
                new NavigationViewItem
                {
                    Content = Strings.Installed,
                    Icon = new SymbolIcon { Symbol = Symbol.Download },
                    Tag = typeof(LoadedPlugins)
                },
                new NavigationViewItem
                {
                    Content = Strings.Library,
                    Icon = new SymbolIcon { Symbol = Symbol.Library },
                    Tag = typeof(Library)
                },
                new NavigationViewItem
                {
                    Content = Strings.Changes,
                    Icon = new SymbolIcon { Symbol = Symbol.Calendar },
                    Tag = typeof(ScheduleChanges)
                },
                new NavigationViewItem
                {
                    Content = Strings.Update,
                    Icon = new SymbolIcon { Symbol = Symbol.Refresh },
                    Tag = typeof(Update)
                },
                new NavigationViewItem
                {
                    Content = Strings.CreatePluginPackage,
                    Icon = new SymbolIcon { Symbol = Symbol.ZipFolder },
                    Tag = typeof(CreatePluginPackage)
                },
                new NavigationViewItem
                {
                    Content = Strings.User,
                    Icon = new SymbolIcon { Symbol = Symbol.People },
                    Tag = typeof(User)
                },
            };
        }

        private void NavView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
        {
            if (e.InvokedItemContainer is NavigationViewItem nvi && nvi.Tag is Type typ)
            {
                _frame.Navigate(typ, null, e.RecommendedNavigationTransitionInfo);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}