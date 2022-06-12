using System.Reactive.Disposables;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public PackageSettingsPageViewModel(DocumentReference docRef, PackageDetailsPageViewModel parent)
    {
        Reference = docRef;
        Parent = parent;
        Resources = new MoreResourcesPageViewModel(this);

        IsChanging = Name.CombineLatest(parent.Name).Select(t => t.First == t.Second)
            .CombineLatest(
                DisplayName.CombineLatest(parent.DisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(parent.Description).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(parent.ShortDescription).Select(t => t.First == t.Second))
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

        MakePublic.Subscribe(async () => await Reference.UpdateAsync("visible", true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Reference.UpdateAsync("visible", false)).DisposeWith(_disposables);
    }

    public DocumentReference Reference { get; }

    public PackageDetailsPageViewModel Parent { get; }

    public MoreResourcesPageViewModel Resources { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

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
