using Avalonia.Collections;

using Beutl.Api.Services;
using Beutl.Framework;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class OutputPageViewModel : BasePageViewModel, IPageContext
{
    private readonly OutputService _outputService;

    public OutputPageViewModel()
    {
        _outputService = ServiceLocator.Current.GetRequiredService<OutputService>();
    }

    public PageExtension Extension => OutputPageExtension.Instance;

    public string Header => Strings.Output;

    public ICoreList<OutputQueueItem> Items => _outputService.Items;

    public IReactiveProperty<OutputQueueItem?> SelectedItem => _outputService.SelectedItem;

    public void AddItem(string file, OutputExtension extension)
    {
        try
        {
            _outputService.AddItem(file, extension);
        }
        catch (Exception e)
        {
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
