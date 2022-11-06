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
    private readonly TransitionMode _transitionMode;
    private readonly EntranceNavigationTransitionInfo _transitionInfo = new();
    private const int Orientation_SameOrder = 0b_1_0_0_0;
    private const int Orientation_SameOrder_Reverse = 0b_0_1_0_0;
    private const int Orientation_DifferenceOrder = 0b_0_0_1_0;
    private const int Orientation_DifferenceOrder_Reverse = 0b_0_0_0_1;

    public NavigationProvider(Frame frame, IPageResolver pageResolver, TransitionMode transitionMode = TransitionMode.LeftNavigationAndBreadcrumbs)
    {
        _frame = frame;
        _pageResolver = pageResolver;
        _transitionMode = transitionMode;
        frame.Navigated += OnNavigated;
        frame.Navigating += OnNavigating;
    }

    [Flags]
    public enum TransitionMode
    {
        // 0: Horizontal
        // 1: Vertical
        // 1. Orderが同じときの方向 | 2. 1を反転 | 3. Orderが違うときの方向 | 4. 3を反転
        LeftNavigationAndBreadcrumbs = 0b_0_0_1_0,
        TopNavigation = 0b_0_0_0_0,
    }

    public object? CurrentContext { get; private set; }

    private void OnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        static bool HasFlags(TransitionMode @enum, int flags)
        {
            return ((int)@enum & flags) == flags;
        }

        static void Locate(bool b, int value, ref double horizontal, ref double vertical)
        {
            if (b)
            {
                horizontal = 0;
                vertical *= value;
            }
            else
            {
                horizontal *= value;
                vertical = 0;
            }
        }

        if (e.NavigationTransitionInfo is EntranceNavigationTransitionInfo entrance)
        {
            Type type1 = _frame.CurrentSourcePageType;
            Type type2 = e.SourcePageType;
            (int order1, int depth1) = (_pageResolver.GetOrder(type1), _pageResolver.GetDepth(type1));
            (int order2, int depth2) = (_pageResolver.GetOrder(type2), _pageResolver.GetDepth(type2));
            double horizontal = 28;
            double vertical = 28;
            int signWhenSameOrder = HasFlags(_transitionMode, Orientation_SameOrder_Reverse) ? -1 : 1;
            int signWhenDiffOrder = HasFlags(_transitionMode, Orientation_DifferenceOrder_Reverse) ? -1 : 1;

            if (order1 == order2)
            {
                Locate(HasFlags(_transitionMode, Orientation_SameOrder), Math.Sign(depth2 - depth1) * signWhenSameOrder,
                    ref horizontal, ref vertical);
            }
            else if (order1 != order2)
            {
                Locate(HasFlags(_transitionMode, Orientation_DifferenceOrder), Math.Sign(order2 - order1) * signWhenDiffOrder,
                    ref horizontal, ref vertical);
            }

            entrance.FromHorizontalOffset = horizontal;
            entrance.FromVerticalOffset = Math.Abs(vertical);
            //entrance.FromVerticalOffset = vertical;
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
