using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class AddResourceDialogViewModel
{
    private readonly AuthorizedUser _user;
    private readonly Package _package;

    public AddResourceDialogViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        _package = package;

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

        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);
        InheritDisplayName.Where(b => b).Subscribe(_ => DisplayName.Value = null);
        InheritDescription.Where(b => b).Subscribe(_ => Description.Value = null);
        InheritShortDescription.Where(b => b).Subscribe(_ => ShortDescription.Value = null);

        IsValid = CultureInput.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth))
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string> CultureInput { get; } = new();

    public ReadOnlyReactivePropertySlim<CultureInfo?> Culture { get; }

    public ReactiveProperty<string?> DisplayName { get; } = new("");

    public ReactiveProperty<string?> Description { get; } = new("");

    public ReactiveProperty<string?> ShortDescription { get; } = new("");

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactiveProperty<bool> InheritDisplayName { get; } = new();

    public ReactiveProperty<bool> InheritDescription { get; } = new();

    public ReactiveProperty<bool> InheritShortDescription { get; } = new();

    public ReactivePropertySlim<string?> Error { get; } = new();

    public PackageResource? Result { get; private set; }

    public async Task<PackageResource?> AddAsync()
    {
        try
        {
            await _user.RefreshAsync();

            var request = new CreatePackageResourceRequest(
                description: Description.Value,
                display_name: DisplayName.Value,
                short_description: ShortDescription.Value,
                tags: null,
                website: null);

            return Result = await _package.AddResourceAsync(CultureInput.Value, request);
        }
        catch (BeutlApiException<ApiErrorResponse> e)
        {
            Error.Value = e.Result.Message;
            return null;
        }
    }

    private static string NotNullOrWhitespace(string? str)
    {
        if (str == null || !string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return S.Message.PleaseEnterString;
        }
    }
}
