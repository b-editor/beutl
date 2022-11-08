using Beutl.Api;
using Beutl.Api.Objects;

using Beutl.ViewModels.Dialogs;

using Reactive.Bindings;

using static Beutl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ReleasePageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public ReleasePageViewModel(AuthorizedUser user, Release release)
    {
        _user = user;
        Release = release;
        ActualAsset = Release.AssetId
            .SelectMany(async id =>
            {
                await _user.RefreshAsync();
                return id.HasValue ? await _user.Profile.GetAssetAsync(id.Value) : null;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Title = Release.Title
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);
        Body = Release.Body
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);
        Asset = ActualAsset
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);

        IsChanging = Title.EqualTo(Release.Title)
            .AreTrue(
                Body.EqualTo(Release.Body),
                Asset.EqualTo(ActualAsset, (x, y) => x?.Id == y?.Id))
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanPublish = Release.AssetId
            .Select(x => x.HasValue)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Save = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    await _user.RefreshAsync();
                    await Release.UpdateAsync(new UpdateReleaseRequest(
                        Asset.Value?.Id,
                        Body.Value,
                        Release.IsPublic.Value,
                        Title.Value));
                }
                catch (Exception ex)
                {
                    ErrorHandle(ex);
                }
            })
            .DisposeWith(_disposables);

        DiscardChanges = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                Title.Value = Release.Title.Value;
                Body.Value = Release.Body.Value;
                if (Asset.Value?.Id != Release.AssetId.Value)
                {
                    await _user.RefreshAsync();
                    Asset.Value = await Release.GetAssetAsync();
                }
            })
            .DisposeWith(_disposables);

        Delete = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    await _user.RefreshAsync();
                    await Release.DeleteAsync();
                }
                catch (Exception ex)
                {
                    ErrorHandle(ex);
                }
            })
            .DisposeWith(_disposables);

        MakePublic = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    await _user.RefreshAsync();
                    await Release.UpdateAsync(isPublic: true);
                }
                catch (Exception ex)
                {
                    ErrorHandle(ex);
                }
            })
            .DisposeWith(_disposables);

        MakePrivate = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    await _user.RefreshAsync();
                    await Release.UpdateAsync(isPublic: true);
                }
                catch (Exception ex)
                {
                    ErrorHandle(ex);
                }
            })
            .DisposeWith(_disposables);

        Refresh = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    await _user.RefreshAsync();
                    await Release.RefreshAsync();
                }
                catch (Exception ex)
                {
                    ErrorHandle(ex);
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);
    }

    public Release Release { get; }

    public ReadOnlyReactivePropertySlim<Asset?> ActualAsset { get; }

    public ReactiveProperty<string?> Title { get; }

    public ReactiveProperty<string?> Body { get; }

    public ReactiveProperty<Asset?> Asset { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; }

    public AsyncReactiveCommand Delete { get; }

    public ReadOnlyReactivePropertySlim<bool> CanPublish { get; }

    public AsyncReactiveCommand MakePublic { get; }

    public AsyncReactiveCommand MakePrivate { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public SelectAssetViewModel SelectReleaseAsset()
    {
        return new SelectAssetViewModel(
            _user,
            x => ToKnownType(x) == KnownType.BeutlPackageFile,
            SharedFilePickerOptions.NuGetPackageFileType);
    }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
