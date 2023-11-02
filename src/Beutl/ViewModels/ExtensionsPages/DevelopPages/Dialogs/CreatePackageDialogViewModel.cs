using Avalonia.Platform.Storage;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Services;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class CreatePackageDialogViewModel
{
    private readonly ILogger _logger = Log.ForContext<CreatePackageDialogViewModel>();
    private readonly AuthorizedUser _user;
    private readonly DiscoverService _discoverService;
    private LocalPackage? _localPackage;

    public CreatePackageDialogViewModel(AuthorizedUser user, DiscoverService discoverService)
    {
        _user = user;
        _discoverService = discoverService;
        Name.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = Name.ObserveHasErrors
            .Select(x => !x)
            .ToReadOnlyReactivePropertySlim();

        SelectedFile.Subscribe(async file =>
        {
            using Activity? activity = Telemetry.StartActivity("CreatePackageDialog.SelectFile");
            try
            {
                IsFileLoading.Value = true;
                if (file?.TryGetLocalPath() is string localPath)
                {
                    string ext = Path.GetExtension(localPath);
                    bool nuspec = ext is ".nuspec";

                    using (Stream stream = await file.OpenReadAsync().ConfigureAwait(false))
                    {
                        _localPackage = nuspec
                            ? Helper.ReadLocalPackageFromNuspecFile(stream)
                            : Helper.ReadLocalPackageFromNupkgFile(stream);

                        if (_localPackage != null)
                        {
                            Name.Value = _localPackage.Name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(ex);
                Error.Value = Message.AnUnexpectedErrorHasOccurred;
                _logger.Error(ex, "An unexpected error has occurred.");
            }
            finally
            {
                IsFileLoading.Value = false;
            }
        });
    }

    public ReactiveProperty<string> Name { get; } = new(mode: ReactivePropertyMode.Default | ReactivePropertyMode.IgnoreInitialValidationError);

    public ReactiveProperty<IStorageFile?> SelectedFile { get; } = new();

    public ReactiveProperty<bool> IsFileLoading { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public Package? Result { get; private set; }

    public async Task<Package?> CreateAsync()
    {
        using Activity? activity = Telemetry.StartActivity("CreatePackageDialog.Create");
        try
        {
            Name.ForceValidate();
            if (!IsValid.Value)
            {
                return null;
            }

            using (await _user.Lock.LockAsync())
            {
                await _user.RefreshAsync();

                CreatePackageRequest? request;
                if (_localPackage != null)
                {
                    request = new CreatePackageRequest(
                        description: _localPackage.Description,
                        display_name: _localPackage.DisplayName,
                        short_description: _localPackage.ShortDescription,
                        tags: _localPackage.Tags,
                        website: _localPackage.WebSite);
                }
                else
                {
                    request = new CreatePackageRequest("", "", "", Array.Empty<string>(), "");
                }

                // nupkgから作成された場合のために、既にパッケージがある場合そのリリースに追加する。
                try
                {
                    Package existing = await _discoverService.GetPackage(Name.Value);
                    if (existing.Owner.Id != _user.Profile.Id)
                    {
                        throw new Exception("Invalid Owner.");
                    }

                    Result = existing;
                }
                catch (BeutlApiException<ApiErrorResponse> ex)
                when (ex.Result.Error_code is ApiErrorCode.PackageNotFound or ApiErrorCode.PackageNotFoundById)
                {
                    Result = await _user.Profile.AddPackageAsync(Name.Value, request);
                }
                catch
                {
                    throw;
                }

                if (_localPackage != null)
                {
                    try
                    {
                        await Result.AddReleaseAsync(
                            _localPackage.Version, new CreateReleaseRequest("", _localPackage.TargetVersion, _localPackage.Version));

                        //_user.Profile.AddAssetAsync()
                    }
                    catch (Exception ex)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                        activity?.RecordException(ex);
                        _logger.Error(ex, "An unexpected error has occurred.");
                        ex.Handle();
                    }
                }

                return Result;
            }
        }
        catch (BeutlApiException<ApiErrorResponse> e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.RecordException(e);
            Error.Value = e.Result.Message;
            _logger.Error(e, "API error occurred.");
            return null;
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.RecordException(e);
            Error.Value = Message.AnUnexpectedErrorHasOccurred;
            _logger.Error(e, "An unexpected error has occurred.");
            return null;
        }
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return Message.PleaseEnterString;
        }
    }
}
