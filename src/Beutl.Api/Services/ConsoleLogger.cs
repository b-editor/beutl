using NuGet.Common;

namespace Beutl.Api.Services;
public class ConsoleLogger : LoggerBase
{
    public override void Log(ILogMessage message)
    {
        Console.WriteLine(message.ToString());
    }

    public override async Task LogAsync(ILogMessage message)
    {
        Console.WriteLine(message.ToString());
    }
}
