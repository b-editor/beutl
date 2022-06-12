using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Collections;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : IDisposable
{
    private readonly object _lockObject = new();
    private readonly FirestoreChangeListener? _listener;

    public PackageReleasesPageViewModel(PackageDetailsPageViewModel parent)
    {
        Parent = parent;

        Reference = parent.Reference.Collection("releases");
        Reference.GetSnapshotAsync()
            .ToObservable()
            .Subscribe(snapshot =>
            {
                foreach (DocumentSnapshot item in snapshot.Documents)
                {
                    lock (_lockObject)
                    {
                        if (!Items.Any(p => p.Reference.Id == item.Reference.Id))
                        {
                            var viewModel = new ReleasePageViewModel(item.Reference, this);
                            viewModel.Update(item);
                            Items.Add(viewModel);
                        }
                    }
                }
            });

        _listener = Reference.Listen(snapshot =>
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
                                var viewModel = new ReleasePageViewModel(item.Document.Reference, this);
                                viewModel.Update(item.Document);
                                Items.Add(viewModel);
                            }
                            break;
                        case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                            foreach (ReleasePageViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    Items.Remove(viewModel);
                                    return;
                                }
                            }
                            break;
                        case DocumentChange.Type.Modified:
                            foreach (ReleasePageViewModel viewModel in Items)
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

        Add.Subscribe(async p =>
        {
            await Reference.AddAsync(new
            {
                version = p.Version.Value,
                title = p.Title.Value,
                body = p.Body.Value,
                visible = false
            });
        });
    }

    public PackageDetailsPageViewModel Parent { get; }

    public CollectionReference Reference { get; }

    public CoreList<ReleasePageViewModel> Items { get; } = new();

    public AsyncReactiveCommand<AddReleaseDialogViewModel> Add { get; } = new();

    public void Dispose()
    {
        _listener?.StopAsync();

        Items.Clear();
    }
}
