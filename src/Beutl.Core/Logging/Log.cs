using Microsoft.Extensions.Logging;

namespace Beutl.Logging;

public class Log
{
    private static ILoggerFactory? s_loggerFactory;

    public static ILoggerFactory LoggerFactory
    {
        get => s_loggerFactory!;
        internal set => s_loggerFactory ??= value;
    }

    public static ILogger<T> CreateLogger<T>()
    {
#if DEBUG
        if (s_loggerFactory == null)
        {
            var tmp = new LoggerFactory();
            return tmp.CreateLogger<T>();
        }
        else
#endif
        {
            return LoggerFactory.CreateLogger<T>();
        }
    }

    public static ILogger CreateLogger(Type type)
    {
#if DEBUG
        if (s_loggerFactory == null)
        {
            var tmp = new LoggerFactory();
            return tmp.CreateLogger(type);
        }
        else
#endif
        {
            return LoggerFactory.CreateLogger(type);
        }
    }

    public static ILogger CreateLogger(string categoryName)
    {
#if DEBUG
        if (s_loggerFactory == null)
        {
            var tmp = new LoggerFactory();
            return tmp.CreateLogger(categoryName);
        }
        else
#endif
        {
            return LoggerFactory.CreateLogger(categoryName);
        }
    }
}
