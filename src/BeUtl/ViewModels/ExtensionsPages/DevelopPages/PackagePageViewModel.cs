using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackagePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public PackagePageViewModel(DocumentReference docRef)
    {
        Reference = docRef;
        ResourcesViewModel = new MoreResourcesPageViewModel(this);

        IObservable<DocumentSnapshot> observable = Reference.ToObservable();

        observable.Select(d => d.GetValue<string>("name"))
            .Subscribe(s => Name.Value = ActualName.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("displayName"))
            .Subscribe(s => DisplayName.Value = ActualDisplayName.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("description"))
            .Subscribe(s => Description.Value = ActualDescription.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("shortDescription"))
            .Subscribe(s => ShortDescription.Value = ActualShortDescription.Value = s)
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
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)));
        Save.Subscribe(async () =>
        {
            await Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["name"] = Name.Value,
                ["displayName"] = DisplayName.Value,
                ["description"] = Description.Value,
                ["shortDescription"] = ShortDescription.Value
            });
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            Name.Value = snapshot.GetValue<string>("name");
            DisplayName.Value = snapshot.GetValue<string>("displayName");
            Description.Value = snapshot.GetValue<string>("description");
            ShortDescription.Value = snapshot.GetValue<string>("shortDescription");
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () => await Reference.DeleteAsync()).DisposeWith(_disposables);

        LocalizedDisplayName = ResourcesViewModel.Items.ToCollectionChanged<ResourcePageViewModel>()
            .SelectMany(_ => ResourcesViewModel.Items.Count > 0
                ? ResourcesViewModel.Items.ToObservable()
                    .SelectMany(i => i.ActualCulture
                        .CombineLatest(i.ActualDisplayName)
                        .Select(ii => ii.First.Equals(CultureInfo.CurrentUICulture) ? ii.Second : null))
                : Observable.Return<string?>(null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        HasLocalizedDisplayName = LocalizedDisplayName
            .Select(i => !string.IsNullOrWhiteSpace(i))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public DocumentReference Reference { get; }

    public MoreResourcesPageViewModel ResourcesViewModel { get; }

    public ReactivePropertySlim<string> ActualName { get; } = new();

    public ReactivePropertySlim<string> ActualDisplayName { get; } = new();

    public ReactivePropertySlim<string> ActualDescription { get; } = new();

    public ReactivePropertySlim<string> ActualShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLocalizedDisplayName { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Dispose()
    {
        _disposables?.Dispose();
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
