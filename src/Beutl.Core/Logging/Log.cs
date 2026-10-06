using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.Logging;

public class Log
{
    private static ILoggerFactory? s_loggerFactory;

    public static ILoggerFactory LoggerFactory
    {
        get => s_loggerFactory ?? NullLoggerFactory.Instance;
        internal set => s_loggerFactory ??= value;
    }

    internal static bool IsLoggerFactoryConfigured => s_loggerFactory is not null;

    // The logger for type once the factory is configured, kept in cache. Before that an uncached
    // NullLogger, so a later call can still switch to the real application logger.
    internal static ILogger GetLoggerOnceConfigured(ref ILogger? cache, Type type)
    {
        if (cache is not null) return cache;
        if (!IsLoggerFactoryConfigured) return NullLogger.Instance;

        try
        {
            return cache = CreateLogger(type);
        }
        catch (Exception)
        {
            return NullLogger.Instance;
        }
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }

    public static ILogger CreateLogger(Type type)
    {
        return LoggerFactory.CreateLogger(type);
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return LoggerFactory.CreateLogger(categoryName);
    }
}
