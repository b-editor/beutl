using Beutl.Collections;

namespace Beutl.Styling;

public interface IStyle
{
    ICoreReadOnlyList<ISetter> Setters { get; }

    Type TargetType { get; }

    event EventHandler? Invalidated;

    IStyleInstance Instance(IStyleable target, IStyleInstance? baseStyle = null);
}
