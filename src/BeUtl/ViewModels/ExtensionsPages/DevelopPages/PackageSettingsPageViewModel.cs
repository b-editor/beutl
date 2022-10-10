using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Framework.Service;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : BaseDevelopPageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public PackageSettingsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Name = Package.Name
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        DisplayName = Package.DisplayName
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        Description = Package.Description
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        ShortDescription = Package.ShortDescription
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;

        // 値が変更されるか
        IsChanging = Name.CombineLatest(Package.Name).Select(t => t.First == t.Second)
            .CombineLatest(
                DisplayName.CombineLatest(Package.DisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(Package.Description).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(Package.ShortDescription).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // データ検証を設定
        Name.SetValidateNotifyError(NotNullOrWhitespace);
        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);

        // コマンドを初期化
        Save = new AsyncReactiveCommand(Name.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)))
            .DisposeWith(_disposables);
        Save.Subscribe(async () =>
        {
            try
            {
                await _user.RefreshAsync();
                await Package.UpdateAsync(
                    description: Description.Value,
                    displayName: DisplayName.Value,
                    name: Name.Value,
                    shortDescription: ShortDescription.Value);
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
            }
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(() =>
        {
            Name.Value = Package.Name.Value;
            DisplayName.Value = Package.DisplayName.Value;
            Description.Value = Package.Description.Value;
            ShortDescription.Value = Package.ShortDescription.Value;
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () =>
        {
            try
            {
                await _user.RefreshAsync();
                await Package.DeleteAsync();
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
            }
        }).DisposeWith(_disposables);

        MakePublic.Subscribe(() => { /*Todo*/ }).DisposeWith(_disposables);

        MakePrivate.Subscribe(() => { /*Todo*/ }).DisposeWith(_disposables);

        Refresh.Subscribe(async () =>
        {
            try
            {
                IsBusy.Value = true;
                await _user.RefreshAsync();
                await Package.RefreshAsync();
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

        Refresh.Execute();
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public PackageDetailsPageViewModel CreatePackageDetailsPage()
    {
        return new PackageDetailsPageViewModel(_user, Package);
    }

    public override void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();

        GC.SuppressFinalize(this);
    }
}
