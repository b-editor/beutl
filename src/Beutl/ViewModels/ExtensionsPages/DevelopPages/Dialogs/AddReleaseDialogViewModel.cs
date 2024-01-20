using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using NuGet.Versioning;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddReleaseDialogViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<AddReleaseDialogViewModel>();
    private readonly AuthorizedUser _user;
    private readonly Package _package;

    public AddReleaseDialogViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        _package = package;

        Version.SetValidateNotifyError(str => NuGetVersion.TryParse(str, out _) ? null : Message.InvalidString);

        IsValid = Version.ObserveHasErrors
            .Not()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string> Title { get; } = new(mode: ReactivePropertyMode.Default | ReactivePropertyMode.IgnoreInitialValidationError);

    public ReactiveProperty<string> Body { get; } = new(mode: ReactivePropertyMode.Default | ReactivePropertyMode.IgnoreInitialValidationError);

    public ReactiveProperty<string> Version { get; } = new(mode: ReactivePropertyMode.Default | ReactivePropertyMode.IgnoreInitialValidationError);

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public Release? Result { get; private set; }

    public async Task<Release?> AddAsync()
    {
        using Activity? activity = Telemetry.StartActivity("AddReleaseDialog");
        try
        {
            Title.ForceValidate();
            Body.ForceValidate();
            Version.ForceValidate();
            if (!IsValid.Value)
            {
                return null;
            }

            using (await _user.Lock.LockAsync())
            {
                await _user.RefreshAsync();

                var request = new CreateReleaseRequest(Body.Value, "*", Title.Value);
                return Result = await _package.AddReleaseAsync(Version.Value, request);
            }
        }
        catch (BeutlApiException<ApiErrorResponse> e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Error.Value = e.Result.Message;
            _logger.LogError(e, "API error occurred.");
            return null;
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Error.Value = Message.AnUnexpectedErrorHasOccurred;
            _logger.LogError(e, "An unexpected error has occurred.");
            return null;
        }
    }
}
