using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Collections;
using BeUtl.Models.Extensions.Develop;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : IDisposable
{
    private readonly IDisposable _disposable;

    public PackageReleasesPageViewModel(PackageDetailsPageViewModel parent)
    {
        Parent = parent;

        _disposable = parent.Package.Value.SubscribeReleases(
            item =>
            {
                if (!Items.Any(p => p.Release.Snapshot.Id == item.Id))
                {
                    var viewModel = new ReleasePageViewModel(new PackageReleaseLink(item), this);
                    viewModel.Update(item);
                    Items.Add(viewModel);
                }
            },
            item =>
            {
                var viewModel = Items.FirstOrDefault(p => p.Release.Snapshot.Id == item.Id);
                if (viewModel != null)
                {
                    Items.Remove(viewModel);
                    viewModel.Dispose();
                }
            },
            item =>
            {
                var viewModel = Items.FirstOrDefault(p => p.Release.Snapshot.Id == item.Id);
                viewModel?.Update(item);
            });

        Reference = parent.Reference.Collection("releases");

        Add.Subscribe(async p =>
        {
            await Parent.Package.Value.AddRelease(new PackageRelease(
                Version: Version.Parse(p.Version.Value),
                Title: p.Title.Value,
                Body: p.Body.Value,
                IsVisible: false,
                DownloadLink: null,
                SHA256: null));
        });
    }

    public PackageDetailsPageViewModel Parent { get; }

    public CollectionReference Reference { get; }

    public CoreList<ReleasePageViewModel> Items { get; } = new();

    public AsyncReactiveCommand<AddReleaseDialogViewModel> Add { get; } = new();

    public void Dispose()
    {
        _disposable.Dispose();

        foreach (var item in Items)
        {
            item.Dispose();
        }
        Items.Clear();
    }
}
