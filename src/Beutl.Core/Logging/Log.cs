using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.Logging;

public class Log
{
    private static ILoggerFactory? s_loggerFactory;

    public static ILoggerFactory LoggerFactory
    {
        get => s_loggerFactory!;
        internal set => s_loggerFactory ??= value;
    }

    internal static bool IsLoggerFactoryConfigured => s_loggerFactory is not null;

    public static ILogger<T> CreateLogger<T>()
    {
        return GetLoggerFactoryOrFallback().CreateLogger<T>();
    }

    public static ILogger CreateLogger(Type type)
    {
        return GetLoggerFactoryOrFallback().CreateLogger(type);
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return GetLoggerFactoryOrFallback().CreateLogger(categoryName);
    }

    private static ILoggerFactory GetLoggerFactoryOrFallback()
    {
        return s_loggerFactory ?? NullLoggerFactory.Instance;
    }
}
