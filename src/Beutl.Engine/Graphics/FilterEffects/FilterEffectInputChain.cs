using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.Graphics.Effects;

// Slices borrow resources from a complete evaluated chain. Their empty original group is never edited.
internal sealed class FilterEffectInputChain : IDisposable
{
    internal sealed record Location(FilterEffect Original, FilterEffect[] Path, int Occurrence)
    {
        public bool SameAs(Location other) => ReferenceEquals(Original, other.Original)
            && Occurrence == other.Occurrence && Path.SequenceEqual(other.Path);
    }
    internal sealed record Entry(FilterEffect.Resource Resource, Location Location, bool ParentEnabled)
    {
        public bool Enabled => ParentEnabled && Resource.IsEnabled;
    }

    public FilterEffectInputChain(IEnumerable<Entry> entries)
    {
        Resource = new FilterEffectGroup().ToResource(CompositionContext.Default);
        Resource.Children.AddRange(entries.Where(static item => item.ParentEnabled).Select(static item => item.Resource));
        Resource.Version++;
    }

    public FilterEffectGroup.Resource Resource { get; }

    public static bool HasInputEvaluation(FilterEffect.Resource? resource) => resource is { IsEnabled: true } && resource switch
    {
        FilterEffectGroup.Resource group => group.Children.Any(HasInputEvaluation),
        FilterEffectPresenter.Resource presenter => HasInputEvaluation(presenter.Target),
        _ => resource.UsesInputEvaluation,
    };

    public static List<Entry> Capture(FilterEffect.Resource? effect)
        => Locate(effect, static resource => (resource.RequireOriginal(), resource.IsEnabled), ChildrenOf)
            .ConvertAll(static leaf => new Entry(leaf.Effect, leaf.Location, leaf.Enabled));

    // The effects a group or presenter applies, or null for an effect that applies itself.
    private static IEnumerable<FilterEffect.Resource?>? ChildrenOf(FilterEffect.Resource resource) => resource switch
    {
        FilterEffectGroup.Resource group => group.Children,
        FilterEffectPresenter.Resource presenter => new[] { presenter.Target },
        _ => null,
    };

    // Resolve reference expressions to find the boundary first. Leaf properties/resources after it are
    // never evaluated: they can contain scene queries, media decoders or another time-sampling effect.
    // A reused sample passes its previous prefix, whose effects reconcile in place to keep loaded resources.
    internal static FilterEffectGroup.Resource EvaluatePrefix(Drawable.Resource source,
        FilterEffect? effect, CompositionContext context, EngineObject.Resource? previous)
    {
        var locations = Locate(effect, static current => (current, current.IsEnabled.Value), current => current switch
        {
            FilterEffectGroup group => group.Children,
            FilterEffectPresenter presenter => new[] { context.Get(presenter.Target) },
            _ => null,
        });
        var paths = locations.Select(static item => item.Location).ToArray();
        int end = paths.Length;
        foreach (var boundary in context.InputBoundaries)
            if (ReferenceEquals(boundary.Drawable, source.GetOriginal()))
                end = Math.Min(end, boundary.Find(paths));

        var reused = previous as FilterEffectGroup.Resource;
        List<Entry>? reusable = reused != null && source.EvaluatedInputChain is { } evaluated
            && evaluated.Select(static entry => entry.Resource).SequenceEqual(reused.Children)
            ? [.. evaluated]
            : null;
        if (reusable == null) reused = null;

        var prefix = reused ?? new FilterEffectGroup().ToResource(context);
        var entries = new List<Entry>();
        var created = new List<FilterEffect.Resource>();
        try
        {
            for (int i = 0; i < end; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var (_, location, enabled) = locations[i];
                if (!enabled) continue;
                int match = reusable?.FindIndex(entry => entry.Location.SameAs(location)) ?? -1;
                FilterEffect.Resource resource;
                if (match >= 0)
                {
                    resource = reusable![match].Resource;
                    reusable.RemoveAt(match);
                    ResourceReconciler.ReconcileInPlace(resource, location.Original, context);
                }
                else
                {
                    resource = location.Original.ToResource(context);
                    created.Add(resource);
                }
                entries.Add(new(resource, location, true));
            }
        }
        catch
        {
            // Reconciled effects stay in the previous prefix, which its source still owns.
            foreach (var resource in created) resource.Dispose();
            if (reused == null) prefix.Dispose();
            throw;
        }

        if (reusable != null)
        {
            foreach (var entry in reusable) entry.Resource.Dispose();
        }
        prefix.Children.Clear();
        prefix.Children.AddRange(entries.Select(static entry => entry.Resource));
        prefix.Version++;
        source.EvaluatedInputChain = entries;
        return prefix;
    }

    // Walks the evaluated and the authored effect trees alike, so a location found in one matches the other.
    private static List<(T Effect, Location Location, bool Enabled)> Locate<T>(T? root,
        Func<T, (FilterEffect Original, bool Enabled)> describe, Func<T, IEnumerable<T?>?> childrenOf)
        where T : class
    {
        var leaves = new List<(T Effect, Location Location, bool Enabled)>();
        var occurrences = new Dictionary<FilterEffect, List<FilterEffect[]>>();
        Collect(root, [], true);
        return leaves;

        void Collect(T? current, FilterEffect[] path, bool enabled)
        {
            if (current == null) return;
            var (original, currentEnabled) = describe(current);
            path = [.. path, original];
            if (childrenOf(current) is { } children)
            {
                foreach (var child in children) Collect(child, path, enabled && currentEnabled);
            }
            else
            {
                int occurrence = NextOccurrence(occurrences, original, path);
                leaves.Add((current, new(original, path, occurrence), enabled));
            }
        }
    }

    private static int NextOccurrence(Dictionary<FilterEffect, List<FilterEffect[]>> occurrences,
        FilterEffect original, FilterEffect[] path)
    {
        if (!occurrences.TryGetValue(original, out var paths)) occurrences.Add(original, paths = []);
        int result = paths.Count(previous => previous.SequenceEqual(path));
        paths.Add(path);
        return result;
    }

    public void Dispose()
    {
        Resource.Children.Clear();
        Resource.Dispose();
    }
}

internal sealed record FilterEffectInputBoundary(Drawable Drawable, FilterEffectInputChain.Location Location)
{
    public int Find(IReadOnlyList<FilterEffectInputChain.Location> chain)
    {
        // Prefer the longest surviving authored path to the effect. Changing a group's reference or
        // inserting another presenter must not renumber this occurrence. An entirely new route to an
        // active effect still stops before it, preventing recursion without rejecting the changed chain.
        int result = chain.Count;
        int bestMatch = 0;
        for (int i = 0; i < chain.Count; i++)
        {
            var candidate = chain[i];
            if (candidate.Occurrence != Location.Occurrence) continue;
            int matched = 0;
            while (matched < candidate.Path.Length && matched < Location.Path.Length
                && ReferenceEquals(candidate.Path[^(matched + 1)], Location.Path[^(matched + 1)])) matched++;
            if (matched > bestMatch)
            {
                bestMatch = matched;
                result = i;
            }
        }
        return result;
    }
}
