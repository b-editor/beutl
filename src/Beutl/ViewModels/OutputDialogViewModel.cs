using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class OutputDialogViewModel : BasePageViewModel, IToolWindowContext
{
    private readonly OutputService _outputService;
    private readonly ILogger _logger = Log.CreateLogger<OutputDialogViewModel>();

    public OutputDialogViewModel()
    {
        _outputService = OutputService.Current;
        CanRemove = SelectedItem
            .SelectMany(x => x?.Context?.IsEncoding?.Not() ?? Observable.Return(false))
            .ToReadOnlyReactivePropertySlim();
    }

    public ToolWindowExtension Extension => OutputDialogExtension.Instance;

    public ICoreList<OutputQueueItem> Items => _outputService.Items;

    public IReactiveProperty<OutputQueueItem?> SelectedItem => _outputService.SelectedItem;

    public ReadOnlyReactivePropertySlim<bool> CanRemove { get; }

    public void AddItem(string file, OutputExtension extension)
    {
        try
        {
            _outputService.AddItem(file, extension);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception has occurred.");

            ErrorHandle(e);
        }
    }

    public void RemoveSelected()
    {
        if (SelectedItem.Value != null)
        {
            Items.Remove(SelectedItem.Value);
        }
    }

    public OutputExtension[] GetExtensions(string file)
    {
        return _outputService.GetExtensions(file);
    }

    public void Save()
    {
        _outputService.SaveItems();
    }

    public void Restore()
    {
        _outputService.RestoreItems();
    }

    public override void Dispose()
    {
    }
}
