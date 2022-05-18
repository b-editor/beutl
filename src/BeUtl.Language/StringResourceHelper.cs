using System.Reactive.Linq;

namespace BeUtl.Language;

internal static class StringResourceHelper
{
    public static string GetStringResource(this string key)
    {
        if (new ResourceReference<string>(key).TryFindResource(out string? value))
        {
            return value;
        }
        else
        {
            return key;
        }
    }

    public static string GetStringResource(this string key, string defaultValue)
    {
        if (new ResourceReference<string>(key).TryFindResource(out string? value))
        {
            return value;
        }
        else
        {
            return defaultValue;
        }
    }

    public static IObservable<string> GetStringObservable(this string key)
    {
        return new ResourceReference<string>(key)
            .GetResourceObservable()
            .Select(i => i ?? key);
    }

    public static IObservable<string> GetStringObservable(this string key, string defaultValue)
    {
        return new ResourceReference<string>(key)
            .GetResourceObservable()
            .Select(i => i ?? defaultValue);
    }
}
