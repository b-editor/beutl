using BeUtl.Collections;
using BeUtl.Media;

namespace BeUtl.Styling;

public interface IStyle
{
    ICoreList<ISetter> Setters { get; }

    event EventHandler? Invalidated;

    IStyleInstance Instance(IStyleable target, IStyleInstance? baseStyle = null);
}
