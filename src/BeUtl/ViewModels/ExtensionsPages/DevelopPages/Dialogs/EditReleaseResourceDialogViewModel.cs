using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public sealed class EditReleaseResourceDialogViewModel
{
    private readonly AuthorizedUser _user;
    private readonly ReleaseResource _resource;

    public EditReleaseResourceDialogViewModel(AuthorizedUser user, ReleaseResource resource)
    {
        _user = user;
        _resource = resource;
        Title.Value = resource.Title.Value;
        Body.Value = resource.Body.Value;

        IsValid = Title.ObserveHasErrors
            .CombineLatest(Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second))
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string?> Title { get; } = new();

    public ReactiveProperty<string?> Body { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public async Task<bool> ApplyAsync()
    {
        try
        {
            await _user.RefreshAsync();

            var request = new UpdateReleaseResourceRequest(Body.Value, Title.Value);
            await _resource.UpdateAsync(request);
            return true;
        }
        catch (BeutlApiException<ApiErrorResponse> e)
        {
            Error.Value = e.Result.Message;
            return false;
        }
    }
}
