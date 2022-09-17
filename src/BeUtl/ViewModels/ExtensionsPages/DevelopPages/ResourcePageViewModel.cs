using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public ResourcePageViewModel(AuthorizedUser user, PackageResource resource)
    {
        _user = user;
        Resource = resource;

        // 入力用のプロパティ
        DisplayName = CreateStringInput(p => p.DisplayName, "");
        Description = CreateStringInput(p => p.Description, "");
        ShortDescription = CreateStringInput(p => p.ShortDescription, "");

        // 値を持っているか (nullじゃない)
        HasDisplayName = CreateHasXXX(s => s.DisplayName);
        HasDescription = CreateHasXXX(s => s.Description);
        HasShortDescription = CreateHasXXX(s => s.ShortDescription);

        // プロパティを購読

        // 入力用プロパティが一つでも変更されたら、trueになる
        IsChanging = DisplayName.CombineLatest(Resource.DisplayName).Select(t => t.First == t.Second)
            .CombineLatest(
                Description.CombineLatest(Resource.Description).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(Resource.ShortDescription).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second && t.Third))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // コマンド設定
        Save = new AsyncReactiveCommand(DisplayName.ObserveHasErrors
            .CombineLatest(Description.ObserveHasErrors, ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third)))
            .DisposeWith(_disposables);

        Save.Subscribe(async () =>
        {
            try
            {
                await _user.RefreshAsync();
                await Resource.UpdateAsync(new UpdatePackageResourceRequest(
                    description: Description.Value,
                    display_name: DisplayName.Value,
                    short_description: ShortDescription.Value,
                    tags: null,
                    website: null));
            }
            catch
            {
                // Todo
            }
        })
            .DisposeWith(_disposables);

        DiscardChanges.Subscribe(() =>
        {
            DisplayName.Value = Resource.DisplayName.Value;
            Description.Value = Resource.Description.Value;
            ShortDescription.Value = Resource.ShortDescription.Value;
        }).DisposeWith(_disposables);

        Refresh.Subscribe(async () =>
        {
            try
            {
                IsBusy.Value = true;
                await _user.RefreshAsync();
                await Resource.RefreshAsync();
            }
            catch
            {
                // Todo
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public PackageResource Resource { get; }

    public ReadOnlyReactivePropertySlim<bool> HasDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> HasShortDescription { get; }

    public ReactiveProperty<string?> DisplayName { get; } = new();

    public ReactiveProperty<string?> Description { get; } = new();

    public ReactiveProperty<string?> ShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    private ReactiveProperty<string?> CreateStringInput(Func<PackageResource, IObservable<string?>> func, string? initial)
    {
        return func(Resource)
            .ToReactiveProperty(initial)
            .DisposeWith(_disposables);
    }

    private ReadOnlyReactivePropertySlim<bool> CreateHasXXX(Func<PackageResource, IObservable<string?>> func)
    {
        return func(Resource)
            .Select(x => !string.IsNullOrEmpty(x))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public async Task DeleteAsync()
    {
        try
        {
            await _user.RefreshAsync();
            await Resource.DeleteAsync();
        }
        catch
        {
            // Todo
        }
    }

    public PackageDetailsPageViewModel CreatePackageDetailsPage()
    {
        return new PackageDetailsPageViewModel(_user, Resource.Package);
    }

    public PackageSettingsPageViewModel CreatePackageSettingsPage()
    {
        return new PackageSettingsPageViewModel(_user, Resource.Package);
    }

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
