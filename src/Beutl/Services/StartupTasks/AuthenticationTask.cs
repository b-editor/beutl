using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Refit;

namespace Beutl.Services.StartupTasks;

public sealed class AuthenticationTask : StartupTask
{
    private readonly ILogger<AuthenticationTask> _logger = Log.CreateLogger<AuthenticationTask>();
    private readonly BeutlApiApplication _beutlApiApplication;

    public AuthenticationTask(BeutlApiApplication beutlApiApplication)
    {
        _beutlApiApplication = beutlApiApplication;
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity(nameof(AuthenticationTask)))
            {
                try
                {
                    await _beutlApiApplication.RestoreUserAsync(activity);
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    _logger.LogError(e, "An error occurred during authentication");
                    if (e is ApiException error)
                    {
                        var apiErr = await error.GetContentAsAsync<ApiErrorResponse>();
                        if (apiErr?.ErrorCode == ApiErrorCode.InvalidRefreshToken)
                        {
                            _beutlApiApplication.SignOut();
                            NotificationService.ShowError(
                                Strings.Account,
                                Message.Signin_has_become_invalid,
                                onActionButtonClick: () => _ = _beutlApiApplication.SignInAsync(default),
                                actionButtonText: SettingsPage.SignIn);
                        }
                    }
                    else
                    {
                        await e.Handle();
                    }
                }
            }
        });
    }

    public override Task Task { get; }
}
