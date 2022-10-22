using Avalonia.Platform.Storage;

using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.ViewModels.Dialogs;

using Reactive.Bindings;

using static BeUtl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

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
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        Body = Release.Body
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        Asset = ActualAsset
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsChanging = Title.CombineLatest(Release.Title).Select(t => t.First == t.Second)
            .CombineLatest(
                Body.CombineLatest(Release.Body).Select(t => t.First == t.Second),
                Asset.CombineLatest(ActualAsset).Select(t => t.First?.Id == t.Second?.Id))
            .Select(t => !(t.First && t.Second && t.Third))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanPublish = Release.Title.CombineLatest(Release.Body, Release.AssetId)
            .Select(x => !string.IsNullOrWhiteSpace(x.First)
                && !string.IsNullOrWhiteSpace(x.Second)
                && x.Third.HasValue)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Save = new AsyncReactiveCommand(Title.ObserveHasErrors
            .CombineLatest(Body.ObserveHasErrors)
            .Select(t => !(t.First || t.Second)));

        Save.Subscribe(async () =>
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
        });

        DiscardChanges.Subscribe(async () =>
        {
            Title.Value = Release.Title.Value;
            Body.Value = Release.Body.Value;
            if (Asset.Value?.Id != Release.AssetId.Value)
            {
                await _user.RefreshAsync();
                Asset.Value = await Release.GetAssetAsync();
            }
        });

        Delete.Subscribe(async () =>
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
        });

        MakePublic.Subscribe(() => { /*Todo*/ }).DisposeWith(_disposables);

        MakePrivate.Subscribe(() => { /*Todo*/ }).DisposeWith(_disposables);

        Refresh.Subscribe(async () =>
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
        });
    }

    public Release Release { get; }

    public ReadOnlyReactivePropertySlim<Asset?> ActualAsset { get; }

    public ReactiveProperty<string> Title { get; }

    public ReactiveProperty<string> Body { get; }

    public ReactiveProperty<Asset?> Asset { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public AsyncReactiveCommand Delete { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanPublish { get; }

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public override bool IsAuthorized => true;

    public SelectAssetViewModel SelectReleaseAsset()
    {
        return new SelectAssetViewModel(
            _user,
            x => ToKnownType(x) == KnownType.BeutlPackageFile,
            new FilePickerFileType("Beutl Package File")
            {
                MimeTypes = new string[] { "application/x-beutl-package" },
                Patterns = new string[] { "*.bpkg" }
            });
    }

    public override void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");
        _disposables.Dispose();

        GC.SuppressFinalize(this);
    }
}
