using BeUtl.Models.Extensions.Develop;
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
    private readonly object _lockObject = new();

    public DevelopPageViewModel()
    {
        CreateNewPackage.Subscribe(async frame =>
        {
            DocumentReference? docRef = await _packageController.NewPackage();
            if (docRef != null)
            {
                PackageLink newItem = await PackageLink.OpenAsync(docRef);
                lock (_lockObject)
                {
                    PackageDetailsPageViewModel? viewModel = Packages.FirstOrDefault(p => p.Package.Value.Snapshot.Id == docRef.Id);
                    if (viewModel == null)
                    {
                        viewModel = new PackageDetailsPageViewModel(newItem);

                        for (int i = 0; i < Packages.Count; i++)
                        {
                            int item = Packages[i].Package.Value.Snapshot.Id.CompareTo(docRef.Id);

                            if (item <= 0)
                            {
                                Packages.Insert(i, viewModel);
                                return;
                            }
                        }

                        Packages.Add(viewModel);
                    }
                    frame.Navigate(typeof(PackageDetailsPage), viewModel);
                }
            }
        });

        _packageController.SubscribePackages(
            snapshot =>
            {
                if (!Packages.Any(p => p.Package.Value.Snapshot.Id == snapshot.Id))
                {
                    var newItem = new PackageDetailsPageViewModel(new PackageLink(snapshot));

                    for (int i = 0; i < Packages.Count; i++)
                    {
                        int item = snapshot.Id.CompareTo(Packages[i].Package.Value.Snapshot.Id);

                        if (item <= 0)
                        {
                            Packages.Insert(i, newItem);
                            return;
                        }
                    }

                    Packages.Add(newItem);
                }
            },
            snapshot =>
            {
                PackageDetailsPageViewModel? item = Packages.FirstOrDefault(p => p.Package.Value.Snapshot.Id == snapshot.Id);
                if (item != null)
                {
                    Packages.Remove(item);
                    item.Dispose();
                }
            },
            _ => { },
            _lockObject)
            .DisposeWith(_disposables);
    }

    ~DevelopPageViewModel()
    {
        Dispose();
    }

    public CoreList<PackageDetailsPageViewModel> Packages { get; } = new();

    public ReactiveCommand<Frame> CreateNewPackage { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (PackageDetailsPageViewModel item in Packages.AsSpan())
        {
            item.Dispose();
        }

        Packages.Clear();

        GC.SuppressFinalize(this);
    }
}
