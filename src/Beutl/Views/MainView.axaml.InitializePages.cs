using System.Collections;
using System.Collections.Specialized;

using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class MainView
{
    private static readonly Binding s_headerBinding = new("Context.Header");
    private readonly AvaloniaList<NavigationViewItem> _navigationItems = new();
    private Control? _settingsView;
    private readonly Avalonia.Animation.Animation _animation = new()
    {
        Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
        Children =
        {
            new KeyFrame
            {
                Setters =
                {
                    new Setter(OpacityProperty, 0.0),
                    new Setter(TranslateTransform.YProperty, 28.0)
                },
                Cue = new Cue(0d)
            },
            new KeyFrame
            {
                Setters =
                {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(TranslateTransform.YProperty, 0.0)
                },
                Cue = new Cue(1d)
            }
        },
        Duration = TimeSpan.FromSeconds(0.67),
        FillMode = FillMode.Forward
    };

    private void NavigationView_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer.DataContext is MainViewModel.NavItemViewModel itemViewModel
            && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage.Value = itemViewModel;
        }
    }

    private static Control CreateView(MainViewModel.NavItemViewModel item)
    {
        Control? view = null;
        Exception? exception = null;
        try
        {
            view = item.Extension.CreateControl();
        }
        catch (Exception e)
        {
            exception = e;
        }

        view ??= new TextBlock()
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Text = exception != null ? @$"
Error:
    {Message.CouldNotCreateInstanceOfView}
Message:
    {exception.Message}
StackTrace:
    {exception.StackTrace}
" : @$"
Error:
    {Message.CannotDisplayThisContext}
"
        };

        view.IsVisible = false;
        view.DataContext = item.Context;

        return view;
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (MainViewModel.NavItemViewModel item in items)
            {
                if (item != null)
                {
                    int idx = index++;
                    Control view = CreateView(item);

                    NaviContent.Children.Insert(idx, view);
                    _navigationItems.Insert(idx, new NavigationViewItem()
                    {
                        Classes = { "SideNavigationViewItem" },
                        DataContext = item,
                        [!ContentProperty] = s_headerBinding,
                        [Interaction.BehaviorsProperty] = new BehaviorCollection
                        {
                            new NavItemHelper()
                            {
                                FilledIcon = item.Extension.GetFilledIcon(),
                                RegularIcon = item.Extension.GetRegularIcon(),
                            }
                        }
                    });
                }
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = items.Count - 1; i >= 0; --i)
            {
                var item = (MainViewModel.NavItemViewModel)items[i]!;
                if (item != null)
                {
                    int idx = index + i;

                    (item.Context as IDisposable)?.Dispose();
                    NaviContent.Children.RemoveAt(idx);
                    _navigationItems.RemoveAt(idx);
                }
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                throw new Exception("'MainViewModel.Pages' does not support the 'Clear' method.");
        }
    }

    private void InitializePages(MainViewModel viewModel)
    {
        _settingsView = new Pages.SettingsPage
        {
            IsVisible = false,
            DataContext = viewModel.SettingsPage.Context
        };
        NaviContent.Children.Clear();
        NaviContent.Children.Add(_settingsView);
        _navigationItems.Clear();

        Control[] pageViews = viewModel.Pages.Select(CreateView).ToArray();
        NaviContent.Children.InsertRange(0, pageViews);

        NavigationViewItem[] navItems = viewModel.Pages.Select(item =>
        {
            return new NavigationViewItem()
            {
                Classes = { "SideNavigationViewItem" },
                DataContext = item,
                [!ContentProperty] = s_headerBinding,
                [Interaction.BehaviorsProperty] = new BehaviorCollection
                {
                    new NavItemHelper()
                    {
                        FilledIcon = item.Extension.GetFilledIcon(),
                        RegularIcon = item.Extension.GetRegularIcon(),
                    }
                }
            };
        }).ToArray();
        _navigationItems.InsertRange(0, navItems);

        viewModel.Pages.CollectionChanged += OnPagesCollectionChanged;
        _disposables.Add(Disposable.Create(viewModel.Pages, obj => obj.CollectionChanged -= OnPagesCollectionChanged));
        viewModel.SelectedPage.Subscribe(async obj =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                int idx = obj == null ? -1 : viewModel.Pages.IndexOf(obj);

                Control? oldControl = null;
                for (int i = 0; i < NaviContent.Children.Count; i++)
                {
                    if (NaviContent.Children[i] is Control { IsVisible: true } control)
                    {
                        control.IsVisible = false;
                        oldControl = control;
                    }
                }

                Navi.SelectedItem = idx >= 0 ? _navigationItems[idx] : Navi.FooterMenuItems.Cast<object>().First();
                Control newControl = idx >= 0 ? NaviContent.Children[idx] : _settingsView;

                newControl.IsVisible = true;
                newControl.Opacity = 0;
                await _animation.RunAsync(newControl);
                newControl.Opacity = 1;

                newControl.Focus();
            }
        }).AddTo(_disposables);
    }
}
