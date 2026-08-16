using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class ChangesModel
{
    public ReactiveCollection<PackageChangeModel> InstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UninstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UpdateItems { get; } = [];

    public async Task Load(
        BeutlApiApplication apiApp,
        string[] installItems,
        string[] uninstallItems,
        string[] updateItems,
        CancellationToken cancellationToken)
    {
        var hash = new HashSet<string>();
        foreach (string item in installItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Install,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                InstallItems.Add(itemViewModel);
            }
        }

        hash.Clear();
        foreach (string item in updateItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Update,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                UpdateItems.Add(itemViewModel);
            }
        }

        hash.Clear();
        foreach (string item in uninstallItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Uninstall,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                UninstallItems.Add(itemViewModel);
            }
        }
    }
}
