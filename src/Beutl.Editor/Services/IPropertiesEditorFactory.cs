namespace Beutl.Editor.Services;

public interface IPropertiesEditorFactory
{
    IPropertiesEditorViewModel Create(ICoreObject obj);
}
