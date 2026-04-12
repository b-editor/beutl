namespace Beutl.Editor.Services;

public interface IElementAdder
{
    void AddElement(ElementDescription desc);

    void AddElementFromTemplate(ObjectTemplateItem template, TimeSpan start, int layer);
}
