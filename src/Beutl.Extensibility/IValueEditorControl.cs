namespace Beutl.Extensibility;

// The control side of a value editor: it shows the context's value and hands the user's changes to it.
public interface IValueEditorControl<TEdit>
{
    void Connect(IValueEditorContext<TEdit> context);
}
