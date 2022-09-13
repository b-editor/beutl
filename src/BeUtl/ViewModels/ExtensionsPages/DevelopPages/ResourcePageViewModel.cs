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

        // 値を継承するかどうか
        InheritDisplayName = CreateInheritXXX(p => p.DisplayName);
        InheritDescription = CreateInheritXXX(p => p.Description);
        InheritShortDescription = CreateInheritXXX(p => p.ShortDescription);

        // 値を持っているか (nullじゃない)
        HasDisplayName = CreateHasXXX(s => s.DisplayName);
        HasDescription = CreateHasXXX(s => s.Description);
        HasShortDescription = CreateHasXXX(s => s.ShortDescription);

        // データ検証
        DisplayName.SetValidateNotifyError(NotWhitespace);
        Description.SetValidateNotifyError(NotWhitespace);
        ShortDescription.SetValidateNotifyError(NotWhitespace);

        // プロパティを購読
        // 値を継承する場合、入力用プロパティにnullを、それ以外はもともとの値を設定する
        InheritDisplayName.Subscribe(b => DisplayName.Value = b ? null : Resource.DisplayName.Value)
            .DisposeWith(_disposables);
        InheritDescription.Subscribe(b => Description.Value = b ? null : Resource.Description.Value)
            .DisposeWith(_disposables);
        InheritShortDescription.Subscribe(b => ShortDescription.Value = b ? null : Resource.ShortDescription.Value)
            .DisposeWith(_disposables);

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
                    description: InheritDescription.Value ? null : Description.Value,
                    display_name: InheritDisplayName.Value ? null : DisplayName.Value,
                    short_description: InheritShortDescription.Value ? null : ShortDescription.Value,
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
            InheritDisplayName.Value = string.IsNullOrEmpty(Resource.DisplayName.Value);
            InheritDescription.Value = string.IsNullOrEmpty(Resource.Description.Value);
            InheritShortDescription.Value = string.IsNullOrEmpty(Resource.ShortDescription.Value);
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

    public ReactiveProperty<bool> InheritDisplayName { get; } = new();

    public ReactiveProperty<bool> InheritDescription { get; } = new();

    public ReactiveProperty<bool> InheritShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    private static string NotWhitespace(string? str)
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

    private ReactiveProperty<string?> CreateStringInput(Func<PackageResource, IObservable<string?>> func, string? initial)
    {
        return func(Resource)
            .ToReactiveProperty(initial)
            .DisposeWith(_disposables);
    }

    private ReadOnlyReactivePropertySlim<bool> CreateHasXXX(Func<PackageResource, IObservable<string?>> func)
    {
        return func(Resource)
            .Select(x => string.IsNullOrEmpty(x))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    private ReactiveProperty<bool> CreateInheritXXX(Func<PackageResource, IObservable<string?>> func)
    {
        return func(Resource)
            .Select(x => string.IsNullOrEmpty(x))
            .ToReactiveProperty()
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
