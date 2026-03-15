using Microsoft.Extensions.Logging;
using NuGet.Common;
using LogLevel = NuGet.Common.LogLevel;

namespace Beutl.Api.Services;

public class LoggerAdapter(Microsoft.Extensions.Logging.ILogger logger) : LoggerBase
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger = logger;

    public override void Log(ILogMessage message)
    {
        switch (message.Level)
        {
            case LogLevel.Debug:
                _logger.LogDebug(message.ToString());
                break;
            case LogLevel.Information:
                _logger.LogInformation(message.ToString());
                break;
            case LogLevel.Warning:
                _logger.LogWarning(message.ToString());
                break;
            case LogLevel.Error:
                _logger.LogError(message.ToString());
                break;
            case LogLevel.Verbose:
                _logger.LogTrace(message.ToString());
                break;
            case LogLevel.Minimal:
                _logger.LogDebug(message.ToString());
                break;
            default:
                _logger.LogInformation(message.ToString());
                break;
        }
    }

    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}
