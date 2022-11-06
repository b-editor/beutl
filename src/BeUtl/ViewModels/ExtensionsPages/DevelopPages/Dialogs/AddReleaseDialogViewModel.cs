using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddReleaseDialogViewModel
{
    private readonly AuthorizedUser _user;
    private readonly Package _package;

    public AddReleaseDialogViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        _package = package;

        Version.SetValidateNotifyError(str => System.Version.TryParse(str, out _) ? null : S.Message.InvalidString);

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = Version.ObserveHasErrors
            .CombineLatest(Title.ObserveHasErrors, Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third))
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
        try
        {
            Title.ForceValidate();
            Body.ForceValidate();
            Version.ForceValidate();
            if (!IsValid.Value)
            {
                return null;
            }

            await _user.RefreshAsync();

            var request = new CreateReleaseRequest(Body.Value, Title.Value);
            return Result = await _package.AddReleaseAsync(Version.Value, request);
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
