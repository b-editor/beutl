namespace Beutl.Controls.Navigation;
#nullable enable

public interface INavigationProvider
{
    ValueTask NavigateAsync<TContext>() where TContext : class
    {
        return NavigateAsync<TContext>(_ => true, () => throw new Exception());
    }

    ValueTask NavigateAsync<TContext>(Predicate<TContext> predicate, Func<TContext> factory) where TContext : class;
}
