using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using BeUtl.Collections;

using DynamicData.Binding;

using Google.Apis.Logging;
using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ResourcePageViewModel
{
    public ResourcePageViewModel(DocumentReference reference)
    {
        Reference = reference;
    }

    public DocumentReference Reference { get; }

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<CultureInfo> Culture { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        DisplayName.Value = snapshot.GetValue<string>("displayName");
        Description.Value = snapshot.GetValue<string>("description");
        Culture.Value = new CultureInfo(snapshot.GetValue<string>("culture"));
    }
}

public sealed class MoreResourcesPageViewModel : IDisposable
{
    internal readonly PackagePageViewModel _viewModel;
    private readonly object _lockObject = new();
    private readonly CompositeDisposable _disposables = new();
    private FirestoreChangeListener? _listener;

    public MoreResourcesPageViewModel(PackagePageViewModel viewModel)
    {
        _viewModel = viewModel;
        ActualName = viewModel.ActualName;

        NewCultureInput = new ReactiveProperty<string>();
        NewCultureInput.SetValidateNotifyError(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                try
                {
                    CultureInfo.GetCultureInfo(str);
                    return null!;
                }
                catch { }
            }

            return "CultureNotFoundException";
        });

        NewCulture = NewCultureInput.Select(str =>
            {
                if (!string.IsNullOrWhiteSpace(str))
                {
                    try
                    {
                        return CultureInfo.GetCultureInfo(str);
                    }
                    catch { }
                }

                return null;
            })
            .ToReadOnlyReactivePropertySlim();

        NewDisplayName = new ReactiveProperty<string>();
        NewDisplayName.SetValidateNotifyError(str =>
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return "Please enter a string";
            }
            else
            {
                return null!;
            }
        });
        NewDescription = new ReactiveProperty<string>();
        NewDescription.SetValidateNotifyError(str =>
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return "Please enter a string";
            }
            else
            {
                return null!;
            }
        });
        NewShortDescription = new ReactiveProperty<string>();
        NewShortDescription.SetValidateNotifyError(str =>
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return "Please enter a string";
            }
            else
            {
                return null!;
            }
        });

        CanAddResource = NewCultureInput.ObserveHasErrors
            .CombineLatest(
                NewDisplayName.ObserveHasErrors,
                NewDescription.ObserveHasErrors,
                NewShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth))
            .ToReadOnlyReactivePropertySlim();

        AddResource = new ReactiveCommand(CanAddResource);
        AddResource.Subscribe(async () =>
        {
            if (NewCulture.Value != null)
            {
                CollectionReference? resources = _viewModel.Reference.Collection("resources");
                await resources.AddAsync(new
                {
                    displayName = NewDisplayName.Value,
                    description = NewDescription.Value,
                    shortDescription = NewShortDescription.Value,
                    culture = NewCulture.Value.Name,
                });

                NewDisplayName.Value = "";
                NewDescription.Value = "";
                NewShortDescription.Value = "";
                NewCultureInput.Value = "";
            }
        }).DisposeWith(_disposables);
    }

    public CoreList<ResourcePageViewModel> Items { get; } = new();

    public ReactivePropertySlim<string> ActualName { get; }

    public ReactiveProperty<string> NewCultureInput { get; }

    public ReadOnlyReactivePropertySlim<CultureInfo?> NewCulture { get; }

    public ReactiveProperty<string> NewDisplayName { get; }

    public ReactiveProperty<string> NewDescription { get; }

    public ReactiveProperty<string> NewShortDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddResource { get; }

    public ReactiveCommand AddResource { get; }

    public bool IsInitialized { get; private set; }

    public IEnumerable<CultureInfo> GetCultures()
    {
        return CultureInfo.GetCultures(CultureTypes.AllCultures).Except(Items.Select(i => i.Culture.Value));
    }

    public void Initialize()
    {
        if (!IsInitialized)
        {
            CollectionReference? resources = _viewModel.Reference.Collection("resources");
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
                                var viewModel = new ResourcePageViewModel(item.Reference);
                                viewModel.Update(item);
                                Items.Add(viewModel);
                            }
                        }
                    }
                })
                .DisposeWith(_disposables);

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
                                    var viewModel = new ResourcePageViewModel(item.Document.Reference);
                                    viewModel.Update(item.Document);
                                    Items.Add(viewModel);
                                }
                                break;
                            case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                                foreach (ResourcePageViewModel viewModel in Items)
                                {
                                    if (viewModel.Reference.Id == item.Document.Id)
                                    {
                                        Items.Remove(viewModel);
                                        return;
                                    }
                                }
                                break;
                            case DocumentChange.Type.Modified:
                                foreach (ResourcePageViewModel viewModel in Items)
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

            IsInitialized = true;
        }
    }

    public void Dispose()
    {
        if (IsInitialized)
        {
            _listener?.StopAsync();
            _disposables.Clear();
            Items.Clear();

            IsInitialized = false;
        }
    }
}
