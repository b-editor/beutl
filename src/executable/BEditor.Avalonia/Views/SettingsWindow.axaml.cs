using System;
using System.Collections;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Properties;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BEditor.Views
{
    public sealed class SettingsWindow : FluentWindow
    {
        private readonly NavigationView _navView;
        private readonly Frame _frame;

        public SettingsWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            _navView = this.Find<NavigationView>("NavView");
            _frame = this.Find<Frame>("FrameView");

            _navView.BackRequested += NavView_BackRequested;
            _navView.ItemInvoked += NavView_ItemInvoked;

            AddNavigationViewMenuItems();
            _frame.Navigated += OnFrameNavigated;

            _frame.Navigate(typeof(Settings.Appearance));
        }

        public SettingsWindow Navigate(Type type)
        {
            _frame.Navigate(type, null);
            return this;
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

        private void NavView_BackRequested(object? sender, NavigationViewBackRequestedEventArgs e)
        {
            _frame?.GoBack();
        }

        private void AddNavigationViewMenuItems()
        {
            _navView.MenuItems = new List<NavigationViewItemBase>
            {
                new NavigationViewItem
                {
                    Content = Strings.Appearance,
                    Icon = new SymbolIcon { Symbol = Symbol.DarkTheme },
                    Tag = typeof(Settings.Appearance)
                },
                new NavigationViewItem
                {
                    Content = Strings.Font,
                    Icon = new SymbolIcon { Symbol = Symbol.Font },
                    Tag = typeof(Settings.Fonts)
                },
                new NavigationViewItem
                {
                    Content = Strings.Project,
                    Icon = new SymbolIcon { Symbol = Symbol.Document },
                    Tag = typeof(Settings.Project)
                },
                new NavigationViewItem
                {
                    Content = Strings.PackageSource,
                    Icon = new SymbolIcon { Symbol = Symbol.Cloud },
                    Tag = typeof(Settings.PackageSource)
                },
                new NavigationViewItem
                {
                    Content = Strings.KeyBindings,
                    Icon = new SymbolIcon { Symbol = Symbol.Keyboard },
                    Tag = typeof(Settings.KeyBindings)
                },
                new NavigationViewItem
                {
                    Content = Strings.License,
                    Icon = new SymbolIcon { Symbol = Symbol.Tag },
                    Tag = typeof(Settings.License)
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