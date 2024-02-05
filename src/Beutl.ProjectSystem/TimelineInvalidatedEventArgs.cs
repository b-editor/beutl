using System.Collections;
using System.Collections.Immutable;

using Beutl.Media;

namespace Beutl;

public class TimelineInvalidatedEventArgs : RenderInvalidatedEventArgs
{
    public TimelineInvalidatedEventArgs(object? obj) : base(obj)
    {
    }

    public TimelineInvalidatedEventArgs(ICollection collection) : base(collection)
    {
    }

    public TimelineInvalidatedEventArgs(object? sender, string propertyName) : base(sender, propertyName)
    {
    }

    public ImmutableArray<TimeRange> AffectedRange { get; init; }
}
