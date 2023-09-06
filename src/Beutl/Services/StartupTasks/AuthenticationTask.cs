using Beutl.Api;
using Beutl.Services;

using Serilog;

namespace Beutl.Services.StartupTasks;

public sealed class AuthenticationTask : StartupTask
{
    private readonly ILogger _logger = Log.ForContext<AuthenticationTask>();
    private readonly BeutlApiApplication _beutlApiApplication;

    public AuthenticationTask(BeutlApiApplication beutlApiApplication)
    {
        _beutlApiApplication = beutlApiApplication;
        Task = Task.Run(async () =>
        {
            try
            {
                await _beutlApiApplication.RestoreUserAsync();
            }
            catch (Exception e)
            {
                _logger.Error(e, "An error occurred during authentication");
                e.Handle();
            }
        });
    }

    public override Task Task { get; }
}
