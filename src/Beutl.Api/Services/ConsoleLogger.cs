using NuGet.Common;

namespace Beutl.Api.Services;

public class ConsoleLogger : LoggerBase
{
    public static readonly ConsoleLogger Instance = new();

    public override void Log(ILogMessage message)
    {
        Console.WriteLine(message.ToString());
    }

    public override Task LogAsync(ILogMessage message)
    {
        Console.WriteLine(message.ToString());
        return Task.CompletedTask;
    }
}
