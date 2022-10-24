using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Framework.Service;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Microsoft.Extensions.DependencyInjection;

using Nito.Disposables;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public PackageSettingsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Name = Package.Name
            .CopyToReactiveProperty()
            .SetValidateNotifyError(NotNullOrWhitespace)
            .DisposeWith(_disposables);
        DisplayName = Package.DisplayName
            .CopyToReactiveProperty()
            .SetValidateNotifyError(NotNullOrWhitespace)
            .DisposeWith(_disposables);
        Description = Package.Description
            .CopyToReactiveProperty()
            .SetValidateNotifyError(NotNullOrWhitespace)
            .DisposeWith(_disposables);
        ShortDescription = Package.ShortDescription
            .CopyToReactiveProperty()
            .SetValidateNotifyError(NotNullOrWhitespace)
            .DisposeWith(_disposables);

        // 値が変更されるか
        IsChanging = Name.EqualTo(Package.Name)
            .AreTrue(
                DisplayName.EqualTo(Package.DisplayName),
                Description.EqualTo(Package.Description),
                ShortDescription.EqualTo(Package.ShortDescription))
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // コマンドを初期化
        Save = Name.ObserveHasErrors
            .AnyTrue(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Not()
            .ToAsyncReactiveCommand()
            .WithSubscribe(async () =>
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
            })
            .DisposeWith(_disposables);

        DiscardChanges = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                Name.Value = Package.Name.Value;
                DisplayName.Value = Package.DisplayName.Value;
                Description.Value = Package.Description.Value;
                ShortDescription.Value = Package.ShortDescription.Value;
            })
            .DisposeWith(_disposables);

        Delete = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
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
            })
            .DisposeWith(_disposables);

        MakePublic = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    await _user.RefreshAsync();
                    await Package.UpdateAsync(isPublic: true);
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
                    await Package.UpdateAsync(isPublic: false);
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
            })
            .DisposeWith(_disposables);

        Refresh.Execute();
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; }

    public AsyncReactiveCommand Delete { get; }

    public AsyncReactiveCommand MakePublic { get; }

    public AsyncReactiveCommand MakePrivate { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();

        GC.SuppressFinalize(this);
    }
}
