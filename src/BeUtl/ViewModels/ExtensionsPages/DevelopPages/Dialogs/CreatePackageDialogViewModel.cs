using Avalonia.Platform.Storage;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class CreatePackageDialogViewModel
{
    private readonly AuthorizedUser _user;
    private LocalPackage? _localPackage;

    public CreatePackageDialogViewModel(AuthorizedUser user)
    {
        _user = user;

        Name.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = Name.ObserveHasErrors
            .Select(x => !x)
            .ToReadOnlyReactivePropertySlim();

        SelectedFile.Subscribe(async file =>
        {
            try
            {
                IsFileLoading.Value = true;
                if (file?.CanOpenRead == true && file.TryGetUri(out Uri? uri))
                {
                    string ext = Path.GetExtension(uri.OriginalString);
                    bool nuspec = ext is ".nuspec";

                    using (Stream stream = await file.OpenReadAsync().ConfigureAwait(false))
                    {
                        _localPackage = nuspec
                            ? Beutl.Api.Services.Helper.ReadLocalPackageFromNuspecFile(stream)
                            : Beutl.Api.Services.Helper.ReadLocalPackageFromNupkgFile(stream);

                        if (_localPackage != null)
                        {
                            Name.Value = _localPackage.Name;
                        }
                    }
                }
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
        try
        {
            Name.ForceValidate();
            if (!IsValid.Value)
            {
                return null;
            }

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

            Result = await _user.Profile.AddPackageAsync(Name.Value, request);

            if (_localPackage != null)
            {
                try
                {
                    await Result.AddReleaseAsync(
                        _localPackage.Version, new CreateReleaseRequest("", _localPackage.Version));
                }
                catch
                {
                }
            }

            return Result;
        }
        catch (BeutlApiException<ApiErrorResponse> e)
        {
            Error.Value = e.Result.Message;
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
            return S.Message.PleaseEnterString;
        }
    }
}
