using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddReleaseResourceDialogViewModel
{
    private readonly AuthorizedUser _user;
    private readonly Release _release;

    public AddReleaseResourceDialogViewModel(AuthorizedUser user, Release release)
    {
        _user = user;
        _release = release;
        CultureInput.SetValidateNotifyError(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    CultureInfo.GetCultureInfo(str);
                    return null!;
                }
                catch { }
            }

            return S.Message.InvalidString;
        });

        Culture = CultureInput.Select(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(str);
                }
                catch { }
            }

            return null;
        })
            .ToReadOnlyReactivePropertySlim();

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsValid = CultureInput.ObserveHasErrors
            .CombineLatest(Title.ObserveHasErrors, Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third))
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public ReleaseResource? Result { get; private set; }

    public async Task<ReleaseResource?> AddAsync()
    {
        try
        {
            await _user.RefreshAsync();

            var request = new CreateReleaseResourceRequest(Body.Value, Title.Value);
            return Result = await _release.AddResourceAsync(CultureInput.Value, request);
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
