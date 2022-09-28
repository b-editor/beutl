using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Controls.Navigation;

#nullable enable

public class NavigationProvider : INavigationProvider
{
    private readonly Frame _frame;
    private readonly IPageResolver _pageResolver;
    private readonly EntranceNavigationTransitionInfo _transitionInfo = new();

    public NavigationProvider(Frame frame, IPageResolver pageResolver)
    {
        _frame = frame;
        _pageResolver = pageResolver;

        frame.Navigated += OnNavigated;
        frame.Navigating += OnNavigating;
    }

    public object? CurrentContext { get; private set; }

    private void OnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.NavigationTransitionInfo is EntranceNavigationTransitionInfo entrance)
        {
            Type type1 = _frame.CurrentSourcePageType;
            Type type2 = e.SourcePageType;
            (int order1, int depth1) = (_pageResolver.GetOrder(type1), _pageResolver.GetDepth(type1));
            (int order2, int depth2) = (_pageResolver.GetOrder(type2), _pageResolver.GetDepth(type2));
            double horizontal = 28;
            double vertical = 28;

            if (order1 == order2)
            {
                horizontal *= Math.Clamp(depth2 - depth1, -1, 1);
                vertical = 0;
            }
            else if (order1 != order2)
            {
                horizontal = 0;
                vertical *= Math.Clamp(order2 - order1, -1, 1);
            }

            entrance.FromHorizontalOffset = horizontal;
            entrance.FromVerticalOffset = vertical;
        }
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is Control control)
        {
            if (e.Parameter is { } parameter)
            {
                CurrentContext = parameter;
                control.DataContext = parameter;
                if (parameter is PageContext pageContext)
                {
                    pageContext.SetNavigation(this);
                }
            }

            control.Focus();
        }
    }

    public async ValueTask<TContext?> FindAsync<TContext>(Predicate<TContext> predicate)
        where TContext : class
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return FindCore(predicate);
        }
        else
        {
            return await Dispatcher.UIThread.InvokeAsync(() => FindCore(predicate));
        }
    }

    private TContext? FindCore<TContext>(Predicate<TContext> predicate)
        where TContext : class
    {
        for (int i = 0; i < _frame.BackStack.Count; i++)
        {
            PageStackEntry item = _frame.BackStack[i];
            if (item.Parameter is TContext typed && predicate(typed))
            {
                return typed;
            }
        }

        for (int i = 0; i < _frame.ForwardStack.Count; i++)
        {
            PageStackEntry item = _frame.ForwardStack[i];
            if (item.Parameter is TContext typed && predicate(typed))
            {
                return typed;
            }
        }

        return default;
    }

    public async ValueTask GoBackAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _frame.GoBack();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => _frame.GoBack());
        }
    }

    public async ValueTask NavigateAsync<TContext>(Predicate<TContext> predicate, Func<TContext> factory)
        where TContext : class
    {
        void NavigateCore(Predicate<TContext> predicate, Func<TContext> factory)
        {
            TContext? context = FindCore(predicate);
            context ??= factory();

            _frame.Navigate(_pageResolver.GetPageType(context?.GetType()), context, _transitionInfo);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            NavigateCore(predicate, factory);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => NavigateCore(predicate, factory));
        }
    }

    public async ValueTask RemoveAllAsync<TContext>(Predicate<TContext> predicate, bool goBack = false)
        where TContext : class
    {
        void RemoveAllCore(Predicate<TContext> predicate, bool goBack)
        {
            for (int i = _frame.BackStack.Count - 1; i >= 0; i--)
            {
                PageStackEntry item = _frame.BackStack[i];
                if (item.Parameter is TContext typed && predicate(typed))
                {
                    _frame.BackStack.RemoveAt(i);
                }
            }

            if (goBack)
            {
                _frame.GoBack();
            }

            for (int i = _frame.ForwardStack.Count - 1; i >= 0; i--)
            {
                PageStackEntry item = _frame.ForwardStack[i];
                if (item.Parameter is TContext typed && predicate(typed))
                {
                    _frame.ForwardStack.RemoveAt(i);
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RemoveAllCore(predicate, goBack);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => RemoveAllCore(predicate, goBack));
        }
    }
}
