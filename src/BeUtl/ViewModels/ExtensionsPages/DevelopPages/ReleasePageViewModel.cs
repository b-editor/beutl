using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ReleasePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public ReleasePageViewModel(AuthorizedUser user, Release release)
    {
        _user = user;
        Release = release;

        Title = Release.Title
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;
        Body = Release.Body
            .ToReactiveProperty()
            .DisposeWith(_disposables)!;

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsChanging = Title.CombineLatest(Release.Title).Select(t => t.First == t.Second)
            .CombineLatest(
                Body.CombineLatest(Release.Body).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second))
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
                    null,
                    Body.Value,
                    Release.IsPublic.Value,
                    Title.Value));
            }
            catch
            {
                // Todo
            }
        });

        DiscardChanges.Subscribe(() =>
        {
            Title.Value = Release.Title.Value;
            Body.Value = Release.Body.Value;
        });

        Delete.Subscribe(async () =>
        {
            try
            {
                await _user.RefreshAsync();
                await Release.DeleteAsync();
            }
            catch
            {
                // Todo
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

    public Release Release { get; }

    public ReactiveProperty<string> Title { get; }

    public ReactiveProperty<string> Body { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public AsyncReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public PackageDetailsPageViewModel CreatePackageDetailsPage()
    {
        return new PackageDetailsPageViewModel(_user, Release.Package);
    }

    public PackageReleasesPageViewModel CreatePackageReleasesPage()
    {
        return new PackageReleasesPageViewModel(_user, Release.Package);
    }

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");
        _disposables.Dispose();

        GC.SuppressFinalize(this);
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
