using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.ViewModels.Editors;

namespace Beutl.Services.Adapters;

internal sealed class PropertiesEditorFactoryImpl(ExtensionProvider extensionProvider) : IPropertiesEditorFactory
{
    public IPropertiesEditorViewModel Create(ICoreObject obj)
    {
        return new PropertiesEditorViewModel(obj, extensionProvider);
    }
}
