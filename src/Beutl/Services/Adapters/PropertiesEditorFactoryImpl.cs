using Beutl.Editor.Services;
using Beutl.ViewModels.Editors;

namespace Beutl.Services.Adapters;

internal sealed class PropertiesEditorFactoryImpl : IPropertiesEditorFactory
{
    public static readonly PropertiesEditorFactoryImpl Instance = new();

    public IPropertiesEditorViewModel Create(ICoreObject obj)
    {
        return new PropertiesEditorViewModel(obj);
    }
}
