using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Beutl.Services;

public sealed class DefaultResourceProvider : IResourceProvider
{
    public IObservable<T?> GetResourceObservable<T>(ResourceReference<T> reference)
    {
        string key = reference.Key;
        if (key != null)
        {
            if (key.StartsWith("project:"))
            {
                // Todo: 現在開いているプロジェクトからリソースを検索する (ProjectにIResourceProviderを実装してから)
            }
            else
            {
                return GetAppResourceObservable<T>(key);
            }
        }

        return Observable.Empty<T>();
    }

    public bool TryFindResource<T>(ResourceReference<T> reference, [NotNullWhen(true)] out T? value)
    {
        string key = reference.Key;
        if (key != null)
        {
            if (key.StartsWith("project:"))
            {
                // Todo: 現在開いているプロジェクトからリソースを検索する (ProjectにIResourceProviderを実装してから)
            }
            else if (TryFindAppResource(key, out T? typedValue))
            {
                value = typedValue;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IObservable<T?> GetAppResourceObservable<T>(string key)
    {
        IObservable<object?>? obj = Invoke(() => Application.Current?.GetResourceObservable(key));

        return obj?.Select(i => i is T typed ? typed : default) ?? Observable.Empty<T?>();
    }

    private static bool TryFindAppResource<T>(string key, [NotNullWhen(true)] out T? value)
    {
        object? obj = Invoke(() => Application.Current?.FindResource(key));

        if (obj is T typed)
        {
            value = typed;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    private static T Invoke<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }
        else
        {
            return Dispatcher.UIThread.InvokeAsync(func, DispatcherPriority.Send).GetAwaiter().GetResult();
        }
    }
}
