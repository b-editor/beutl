using System.Collections.Immutable;

using Beutl.Media;

namespace Beutl;

public class ElementEditedEventArgs : EventArgs
{
    public ImmutableArray<TimeRange> AffectedRange { get; init; }
}
