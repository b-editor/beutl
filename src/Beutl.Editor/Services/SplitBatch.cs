using System.Collections.Immutable;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Splits whose new pieces are added to Scene only once the batch ends. Meanwhile the detached pieces read Scene's layers
// and project through it, the groups the cuts create wait in PendingGroups, and the pieces of one file share its duration.
internal sealed class SplitBatch(Scene scene)
{
    public Scene Scene { get; } = scene;

    public List<ImmutableHashSet<Guid>> PendingGroups { get; } = [];

    public Dictionary<(Type, Uri), MediaTime?> SourceDurations { get; } = [];
}
