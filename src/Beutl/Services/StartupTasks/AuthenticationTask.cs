using Beutl.Api;

using OpenTelemetry.Trace;

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
            using (Activity? activity = Telemetry.StartActivity("AuthenticationTask"))
            {
                try
                {
                    await _beutlApiApplication.RestoreUserAsync(activity);
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(e);
                    _logger.Error(e, "An error occurred during authentication");
                    e.Handle();
                }
            }
        });
    }

    public override Task Task { get; }
}
