using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
}
