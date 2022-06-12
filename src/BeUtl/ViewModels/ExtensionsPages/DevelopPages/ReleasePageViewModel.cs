using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using BeUtl.Collections;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ReleaseResourceViewModel
{
    public ReleaseResourceViewModel(DocumentReference reference)
    {
        Reference = reference;
        Delete.Subscribe(() => Reference.DeleteAsync());
    }

    public DocumentReference Reference { get; }

    public ReactivePropertySlim<string> Title { get; } = new();

    public ReactivePropertySlim<string> Body { get; } = new();

    public ReactivePropertySlim<Version> Version { get; } = new();

    public ReactivePropertySlim<CultureInfo> Culture { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        Title.Value = snapshot.GetValue<string>("title");
        Body.Value = snapshot.GetValue<string>("body");
        Version.Value = System.Version.Parse(snapshot.GetValue<string>("version"));
        Culture.Value = CultureInfo.GetCultureInfo(snapshot.GetValue<string>("culture"));
    }
}

public sealed class ReleasePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly object _lockObject = new();
    private readonly FirestoreChangeListener? _listener;

    public ReleasePageViewModel(DocumentReference reference, PackageReleasesPageViewModel parent)
    {
        Parent = parent;
        Reference = reference;

        VersionInput.SetValidateNotifyError(str => System.Version.TryParse(str, out _) ? null : "CultureNotFoundException");

        Version = VersionInput.Select(str => System.Version.TryParse(str, out Version? v) ? v : null)
            .ToReadOnlyReactivePropertySlim();

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        Save = new AsyncReactiveCommand(Title.ObserveHasErrors
            .CombineLatest(Body.ObserveHasErrors, VersionInput.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third)));

        Save.Subscribe(async () =>
        {
            await Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["version"] = (ActualVersion.Value = Version.Value!).ToString(),
                ["title"] = ActualTitle.Value = Title.Value,
                ["body"] = ActualBody.Value = Body.Value,
            });
        });

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            Update(snapshot);
        });

        Delete.Subscribe(async () => await Reference.DeleteAsync());

        CollectionReference? resources = reference.Collection("resources");
        resources?.GetSnapshotAsync()
            .ToObservable()
            .Subscribe(snapshot =>
            {
                foreach (DocumentSnapshot item in snapshot.Documents)
                {
                    lock (_lockObject)
                    {
                        if (!Items.Any(p => p.Reference.Id == item.Reference.Id))
                        {
                            var viewModel = new ReleaseResourceViewModel(item.Reference);
                            viewModel.Update(item);
                            Items.Add(viewModel);
                        }
                    }
                }
            });

        _listener = resources?.Listen(snapshot =>
        {
            foreach (DocumentChange item in snapshot.Changes)
            {
                lock (_lockObject)
                {
                    switch (item.ChangeType)
                    {
                        case DocumentChange.Type.Added when item.NewIndex.HasValue:
                            if (!Items.Any(p => p.Reference.Id == item.Document.Reference.Id))
                            {
                                var viewModel = new ReleaseResourceViewModel(item.Document.Reference);
                                viewModel.Update(item.Document);
                                Items.Add(viewModel);
                            }
                            break;
                        case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                            foreach (ReleaseResourceViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    Items.Remove(viewModel);
                                    return;
                                }
                            }
                            break;
                        case DocumentChange.Type.Modified:
                            foreach (ReleaseResourceViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    viewModel.Update(item.Document);
                                    return;
                                }
                            }
                            break;
                    }
                }
            }
        });
    }

    public PackageReleasesPageViewModel Parent { get; }

    public DocumentReference Reference { get; }

    public ReactivePropertySlim<string> ActualTitle { get; } = new();

    public ReactivePropertySlim<string> ActualBody { get; } = new();

    public ReactivePropertySlim<Version> ActualVersion { get; } = new();

    public ReactivePropertySlim<bool> IsPublic { get; } = new();
    
    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> VersionInput { get; } = new();

    public ReadOnlyReactivePropertySlim<Version?> Version { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public CoreList<ReleaseResourceViewModel> Items { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        _listener?.StopAsync();

        Items.Clear();
    }

    public void Update(DocumentSnapshot snapshot)
    {
        ActualTitle.Value = Title.Value = snapshot.GetValue<string>("title");
        ActualBody.Value = Body.Value = snapshot.GetValue<string>("body");
        ActualVersion.Value = System.Version.Parse(VersionInput.Value = snapshot.GetValue<string>("version"));
        IsPublic.Value = snapshot.GetValue<bool>("visible");
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
