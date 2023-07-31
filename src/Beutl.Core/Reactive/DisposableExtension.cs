namespace Beutl.Reactive;

public static class DisposableExtension
{
    public static T DisposeWith<T>(this T disposable, ICollection<IDisposable> list)
        where T : IDisposable
    {
        list.Add(disposable);
        return disposable;
    }
}
