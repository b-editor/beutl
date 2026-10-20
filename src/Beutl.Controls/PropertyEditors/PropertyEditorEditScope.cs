using Avalonia.Controls;

namespace Beutl.Controls.PropertyEditors;

// A text box preserves its draft; the editor itself marks a direct value adjustment.
internal readonly ref struct PropertyEditorEditScope
{
    private readonly ref Control? _source;
    private readonly Control? _previous;

    public PropertyEditorEditScope(ref Control? source, Control current)
    {
        _source = ref source;
        _previous = source;
        source = current;
    }

    public void Dispose() => _source = _previous;
}
