using Beutl.Api.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;

namespace Beutl.Editor.Components.PropertyEditors.Services;

internal sealed class PropertiesEditorFactoryImpl(ExtensionProvider extensionProvider) : IPropertiesEditorFactory
{
    public IPropertiesEditorViewModel Create(ICoreObject obj)
    {
        return new PropertiesEditorViewModel(obj, extensionProvider);
    }
}
