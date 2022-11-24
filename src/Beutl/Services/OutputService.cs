using Beutl.Api.Services;
using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputQueueItem : IDisposable
{
    private readonly ReactivePropertySlim<bool> _isFinished = new();

    public OutputQueueItem(IOutputContext context)
    {
        Context = context;
        Name = Path.GetFileName(context.TargetFile);

        Context.Started += OnStarted;
        Context.Finished += OnFinished;
    }

    public IOutputContext Context { get; }

    public string Name { get; }

    public IReactiveProperty<bool> IsFinished => _isFinished;

    private void OnStarted(object? sender, EventArgs e)
    {
        IsFinished.Value = false;
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        IsFinished.Value = true;
    }

    public void Dispose()
    {
        Context.Started -= OnStarted;
        Context.Finished -= OnFinished;
        Context.Dispose();
    }
}

public sealed class OutputService
{
    private readonly CoreList<OutputQueueItem> _items;
    private readonly ReactivePropertySlim<OutputQueueItem?> _selectedItem = new();

    public OutputService()
    {
        _items = new CoreList<OutputQueueItem>();
        _items.Attached += OnItemsAttached;
        _items.Detached += OnItemsDetached;
    }

    private void OnItemsDetached(OutputQueueItem obj)
    {
        EditorService editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        if (editorService.TryGetTabItem(obj.Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = true;
        }
    }

    private void OnItemsAttached(OutputQueueItem obj)
    {
        EditorService editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        if (editorService.TryGetTabItem(obj.Context.TargetFile, out EditorTabItem? tabItem))
        {
            tabItem.Context.Value.IsEnabled.Value = false;
        }
    }

    public ICoreList<OutputQueueItem> Items => _items;

    public IReactiveProperty<OutputQueueItem?> SelectedItem => _selectedItem;

    public void AddItem(string file, OutputExtension extension)
    {
        if (Items.Any(x => x.Context.TargetFile == file))
        {
            throw new Exception("Already added");
        }
        if (!extension.TryCreateContext(file, out IOutputContext? context))
        {
            throw new Exception("Failed to create context");
        }

        var item = new OutputQueueItem(context);
        Items.Add(item);
        SelectedItem.Value = item;
    }

    public OutputExtension[] GetExtensions(string file)
    {
        return ServiceLocator.Current.GetRequiredService<ExtensionProvider>()
            .GetExtensions<OutputExtension>()
            .Where(x => x.IsSupported(file)).ToArray();
    }
}
