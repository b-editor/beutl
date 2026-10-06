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
        List<PackageChangeModel> installViewModels = await ParseUniqueAsync(
            apiApp, installItems, PackageChangeAction.Install, hash, cancellationToken);
        List<PackageChangeModel> updateViewModels = await ParseUniqueAsync(
            apiApp, updateItems, PackageChangeAction.Update, hash, cancellationToken);
        List<PackageChangeModel> uninstallViewModels = await ParseUniqueAsync(
            apiApp, uninstallItems, PackageChangeAction.Uninstall, hash, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        AddAll(InstallItems, installViewModels);
        AddAll(UpdateItems, updateViewModels);
        AddAll(UninstallItems, uninstallViewModels);
    }

    // Ids are deduplicated within one action only, so the shared set is cleared first.
    private static async Task<List<PackageChangeModel>> ParseUniqueAsync(
        BeutlApiApplication apiApp,
        string[] items,
        PackageChangeAction action,
        HashSet<string> ids,
        CancellationToken cancellationToken)
    {
        var viewModels = new List<PackageChangeModel>();
        ids.Clear();
        foreach (string item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                action,
                cancellationToken);

            if (itemViewModel != null && ids.Add(itemViewModel.Id))
            {
                viewModels.Add(itemViewModel);
            }
        }

        return viewModels;
    }

    private static void AddAll(ReactiveCollection<PackageChangeModel> target, List<PackageChangeModel> items)
    {
        foreach (PackageChangeModel item in items)
        {
            target.Add(item);
        }
    }
}
