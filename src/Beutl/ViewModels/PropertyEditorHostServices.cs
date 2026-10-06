using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Services.Adapters;

namespace Beutl.ViewModels;

// The services a standalone property-editor host (not an EditViewModel) offers to the editors it creates.
internal sealed class PropertyEditorHostServices(HistoryManager history, ExtensionProvider extensionProvider)
{
    private PropertyEditorFactoryAdapter? _propertyEditorFactory;
    private PropertiesEditorFactoryImpl? _propertiesEditorFactory;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(HistoryManager))
        {
            return history;
        }

        // Expose the session ExtensionProvider and property-editor factories so nested object /
        // list editors resolve them through the service chain even though this host is not an
        // EditViewModel.
        if (serviceType == typeof(ExtensionProvider))
            return extensionProvider;

        if (serviceType.IsAssignableTo(typeof(IPropertyEditorFactory)))
            return _propertyEditorFactory ??= new PropertyEditorFactoryAdapter(extensionProvider);

        if (serviceType.IsAssignableTo(typeof(IPropertiesEditorFactory)))
            return _propertiesEditorFactory ??= new PropertiesEditorFactoryImpl(extensionProvider);

        return null;
    }
}
