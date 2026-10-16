using Avalonia.Controls;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Controls.Navigation;

public class NavigationProvider : INavigationProvider
{
    private readonly FAFrame _frame;
    private readonly IPageResolver _pageResolver;
    private readonly FAEntranceNavigationTransitionInfo _transitionInfo = new();

    public NavigationProvider(FAFrame frame, IPageResolver pageResolver)
    {
        _frame = frame;
        _pageResolver = pageResolver;
        frame.Navigated += OnNavigated;
        frame.Navigating += OnNavigating;
    }

    private void OnNavigating(object sender, FANavigatingCancelEventArgs e)
    {
        if (e.NavigationTransitionInfo is FAEntranceNavigationTransitionInfo entrance)
        {
            Type type1 = _frame.CurrentSourcePageType;
            Type type2 = e.SourcePageType;
            (int order1, int depth1) = (_pageResolver.GetOrder(type1), _pageResolver.GetDepth(type1));
            (int order2, int depth2) = (_pageResolver.GetOrder(type2), _pageResolver.GetDepth(type2));

            if (order1 == order2)
            {
                entrance.FromHorizontalOffset = 28 * Math.Sign(depth2 - depth1);
                entrance.FromVerticalOffset = 0;
            }
            else
            {
                entrance.FromHorizontalOffset = 0;
                entrance.FromVerticalOffset = 28;
            }
        }
    }

    private void OnNavigated(object sender, FANavigationEventArgs e)
    {
        if (e.Content is Control control)
        {
            if (e.Parameter is { } parameter)
            {
                control.DataContext = parameter;
                if (parameter is PageContext pageContext)
                {
                    pageContext.SetNavigation(this);
                }
            }

            control.Focus();
        }
    }

    private TContext? FindCore<TContext>(Predicate<TContext> predicate)
        where TContext : class
    {
        for (int i = 0; i < _frame.BackStack.Count; i++)
        {
            FAPageStackEntry item = _frame.BackStack[i];
            if (item.Parameter is TContext typed && predicate(typed))
            {
                return typed;
            }
        }

        for (int i = 0; i < _frame.ForwardStack.Count; i++)
        {
            FAPageStackEntry item = _frame.ForwardStack[i];
            if (item.Parameter is TContext typed && predicate(typed))
            {
                return typed;
            }
        }

        return default;
    }

    public async ValueTask NavigateAsync<TContext>(Predicate<TContext> predicate, Func<TContext> factory)
        where TContext : class
    {
        void NavigateCore(Predicate<TContext> predicate, Func<TContext> factory)
        {
            TContext? context = FindCore(predicate);
            context ??= factory() ?? throw new InvalidOperationException("The page context factory returned null.");

            _frame.Navigate(_pageResolver.GetPageType(context.GetType()), context, _transitionInfo);
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
}
