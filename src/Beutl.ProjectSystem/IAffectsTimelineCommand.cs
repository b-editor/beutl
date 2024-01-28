using System.Collections.Immutable;

using Beutl.Media;

namespace Beutl;

public interface IAffectsTimelineCommand : IRecordableCommand
{
    ImmutableArray<TimeRange> GetAffectedRange();
}
