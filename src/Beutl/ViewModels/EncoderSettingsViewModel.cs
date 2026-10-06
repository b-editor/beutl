using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Media.Encoding;
using Beutl.Services;
using DynamicData;

namespace Beutl.ViewModels;

public sealed class EncoderSettingsViewModel : IPropertyEditorContextVisitor, IServiceProvider, IDisposable
{
    private readonly PropertyEditorHostServices _services;
    private readonly ExtensionProvider _extensionProvider;

    public EncoderSettingsViewModel(MediaEncoderSettings settings, ExtensionProvider extensionProvider)
    {
        Settings = settings;
        _extensionProvider = extensionProvider;
        var sequenceGenerator = new OperationSequenceGenerator();
        _services = new PropertyEditorHostServices(new HistoryManager(settings, sequenceGenerator), extensionProvider);
        InitializeCoreObject(settings, (_, m) => m.Browsable);
    }

    public MediaEncoderSettings Settings { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public object? GetService(Type serviceType)
    {
        return _services.GetService(serviceType);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    private void InitializeCoreObject(MediaEncoderSettings obj,
        Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        List<IPropertyAdapter> props = CoreObjectPropertyEditorBuilder.CreateAdapters(obj, predicate);

        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props, _extensionProvider);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        tempItems.Sort((x, y) =>
        {
            int xx = CoreObjectPropertyEditorBuilder.GetDisplayAttribute(x)?.GetOrder() ?? 0;
            int yy = CoreObjectPropertyEditorBuilder.GetDisplayAttribute(y)?.GetOrder() ?? 0;
            return xx - yy;
        });

        CoreObjectPropertyEditorBuilder.GroupByDisplayGroupName(tempItems);

        Properties.AddRange(tempItems);
    }

    public void Dispose()
    {
        for (int i = Properties.Count - 1; i >= 0; i--)
        {
            var item = Properties[i];
            Properties.RemoveAt(i);
            item?.Dispose();
        }
    }
}
