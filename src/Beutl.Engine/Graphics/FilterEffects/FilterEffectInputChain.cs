using Beutl.Composition;

namespace Beutl.Graphics.Effects;

// Slices borrow resources from a complete evaluated chain. Their empty original group is never edited.
internal sealed class FilterEffectInputChain : IDisposable
{
    internal sealed record Location(FilterEffect Original, FilterEffect[] Path, int Occurrence);
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
    {
        var result = new List<Entry>();
        var occurrences = new Dictionary<FilterEffect, List<FilterEffect[]>>();
        Collect(effect, [], true);
        return result;

        void Collect(FilterEffect.Resource? current, FilterEffect[] path, bool enabled)
        {
            if (current == null) return;
            FilterEffect original = current.RequireOriginal();
            path = [.. path, original];
            switch (current)
            {
                case FilterEffectGroup.Resource group:
                    foreach (var child in group.Children) Collect(child, path, enabled && current.IsEnabled);
                    break;
                case FilterEffectPresenter.Resource presenter:
                    Collect(presenter.Target, path, enabled && current.IsEnabled);
                    break;
                default:
                    int occurrence = NextOccurrence(occurrences, original, path);
                    result.Add(new Entry(current, new(original, path, occurrence), enabled));
                    break;
            }
        }
    }

    // Resolve reference expressions to find the boundary first. Leaf properties/resources after it are
    // never evaluated: they can contain scene queries, media decoders or another time-sampling effect.
    internal static FilterEffectGroup.Resource EvaluatePrefix(Drawable.Resource source,
        FilterEffect? effect, CompositionContext context)
    {
        var locations = new List<(Location Location, bool Enabled)>();
        var occurrences = new Dictionary<FilterEffect, List<FilterEffect[]>>();
        Collect(effect, [], true);
        var paths = locations.Select(static item => item.Location).ToArray();
        int end = paths.Length;
        foreach (var boundary in context.InputBoundaries)
            if (ReferenceEquals(boundary.Drawable, source.GetOriginal()))
                end = Math.Min(end, boundary.Find(paths));

        var prefix = new FilterEffectGroup().ToResource(context);
        var entries = new List<Entry>();
        try
        {
            for (int i = 0; i < end; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var (location, enabled) = locations[i];
                if (!enabled) continue;
                var resource = location.Original.ToResource(context);
                prefix.Children.Add(resource);
                entries.Add(new(resource, location, true));
            }
            source.EvaluatedInputChain = entries;
            return prefix;
        }
        catch
        {
            prefix.Dispose();
            throw;
        }

        void Collect(FilterEffect? current, FilterEffect[] path, bool enabled)
        {
            if (current == null) return;
            path = [.. path, current];
            switch (current)
            {
                case FilterEffectGroup group:
                    foreach (var child in group.Children) Collect(child, path, enabled && current.IsEnabled.Value);
                    break;
                case FilterEffectPresenter presenter:
                    Collect(context.Get(presenter.Target), path, enabled && current.IsEnabled.Value);
                    break;
                default:
                    int occurrence = NextOccurrence(occurrences, current, path);
                    locations.Add((new(current, path, occurrence), enabled));
                    break;
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
