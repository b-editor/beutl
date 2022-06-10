using System.Reactive.Disposables;
using System.Reactive.Threading.Tasks;

using BeUtl.Collections;
using BeUtl.Pages.ExtensionsPages.DevelopPages;
using BeUtl.Services;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public sealed class DevelopPageViewModel : IDisposable
{
    private readonly PackageController _packageController = ServiceLocator.Current.GetRequiredService<PackageController>();
    private readonly CompositeDisposable _disposables = new();
    private readonly FirestoreChangeListener? _packagesListener;
    private readonly object _lockObject = new();

    public DevelopPageViewModel()
    {
        CreateNewPackage.Subscribe(async frame =>
        {
            DocumentReference? docRef = await _packageController.NewPackage();
            if (docRef != null)
            {
                lock (_lockObject)
                {
                    PackagePageViewModel? viewModel = Packages.FirstOrDefault(p => p.Reference.Id == docRef.Id);
                    if (viewModel == null)
                    {
                        viewModel = new PackagePageViewModel(docRef);
                        Packages.Add(viewModel);
                    }

                    frame.Navigate(typeof(PackagePage), viewModel);
                }
            }
        });

        CollectionReference? packages = _packageController.GetPackages();
        packages?.GetSnapshotAsync()
            .ToObservable()
            .Subscribe(snapshot =>
            {
                foreach (DocumentSnapshot item in snapshot.Documents)
                {
                    lock (_lockObject)
                    {
                        var viewModel = new PackagePageViewModel(item.Reference);
                        Packages.Add(viewModel);
                    }
                }
            });

        _packagesListener = packages?.Listen(snapshot =>
        {
            foreach (DocumentChange item in snapshot.Changes)
            {
                lock (_lockObject)
                {
                    switch (item.ChangeType)
                    {
                        case DocumentChange.Type.Added when item.NewIndex.HasValue:
                            if (!Packages.Any(p => p.Reference.Id == item.Document.Reference.Id))
                            {
                                var viewModel = new PackagePageViewModel(item.Document.Reference);
                                Packages.Add(viewModel);
                            }
                            break;
                        case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                            foreach (PackagePageViewModel pkg in Packages)
                            {
                                if (pkg.Reference.Id == item.Document.Id)
                                {
                                    Packages.Remove(pkg);
                                    pkg.Dispose();
                                    return;
                                }
                            }
                            break;
                        case DocumentChange.Type.Modified:
                            break;
                    }
                }
            }
        });
    }

    public CoreList<PackagePageViewModel> Packages { get; } = new();

    public ReactiveCommand<Frame> CreateNewPackage { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        _packagesListener?.StopAsync();
    }
}
