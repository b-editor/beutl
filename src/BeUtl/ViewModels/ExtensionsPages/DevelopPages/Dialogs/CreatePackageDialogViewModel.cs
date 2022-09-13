using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class CreatePackageDialogViewModel
{
    private readonly AuthorizedUser _user;

    public CreatePackageDialogViewModel(AuthorizedUser user)
    {
        _user = user;

        Name.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = Name.ObserveHasErrors
            .Select(x => !x)
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string> Name { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public Package? Result { get; private set; }

    public async Task<Package?> CreateAsync()
    {
        try
        {
            await _user.RefreshAsync();

            var request = new CreatePackageRequest("", "", "", null, "");
            return Result = await _user.Profile.AddPackageAsync(Name.Value, request);
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
