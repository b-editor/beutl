using NuGet.Common;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class ResolveTaskModel : NuGet.Common.LoggerBase
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger = Logging.Log.CreateLogger<ResolveTaskModel>();
    private readonly BeutlApiApplication _app;
    private readonly PackageInstallContext _context;

    public ResolveTaskModel(BeutlApiApplication app, PackageInstallContext context)
    {
        _app = app;
        _context = context;
    }

    public ReactiveProperty<bool> ShowDetails { get; } = new(false);

    public ReactiveProperty<string> Message { get; } = new();

    public ReactiveProperty<bool?> FailedToResolve { get; } = new();

    public ReactiveProperty<bool> IsProgressBarVisible { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<bool> IsRunning { get; } = new();

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public async Task<bool> Run(CancellationToken token)
    {
        PackageInstaller installer = _app.GetResource<PackageInstaller>();
        IsRunning.Value = true;

        try
        {
            IsProgressBarVisible.Value = true;

            await installer.ResolveDependencies(_context, this, token);
            Succeeded.Value = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage.Value = Strings.Operation_canceled;
            Failed.Value = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while resolving dependencies.");
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
            return false;
        }
        finally
        {
            IsProgressBarVisible.Value = false;
            IsRunning.Value = false;
        }
    }

    public override void Log(ILogMessage message)
    {
        if (Message.Value == null)
        {
            Message.Value = message.Message;
        }
        else
        {
            Message.Value = $"{Message.Value}\n{message.Message}";
        }
    }

    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}
