using Beutl.Logging;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl;

// Ids are unique per project. A duplicate is counted on load, save and close and logged once, but is
// not given a new Id: references resolve within their scene first (#2885).
internal static class DuplicateIds
{
    // The count last logged for each project file.
    private static readonly Dictionary<string, int> s_reported = new(StringComparer.Ordinal);
    private static ILogger? s_logger;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(DuplicateIds));

    // The objects whose Id an object before them in the walk already has, and up to five of their types.
    public static (int Count, Type[] Types) Count(object root)
    {
        var ids = new HashSet<Guid>();
        var types = new List<Type>();
        int count = 0;
        foreach (object obj in ChildObjects.Walk(root))
        {
            if (obj is ICoreObject { Id.Value: var id } && id != Guid.Empty && !ids.Add(id))
            {
                count++;
                if (types.Count < 5 && !types.Contains(obj.GetType()))
                    types.Add(obj.GetType());
            }
        }

        return (count, [.. types]);
    }

    public static void Report(Project project, string occasion)
    {
        try
        {
            (int count, Type[] types) = Count(project);
            string key = project.Uri?.AbsoluteUri ?? string.Empty;
            lock (s_reported)
            {
                if (count == 0)
                {
                    s_reported.Remove(key);
                    return;
                }

                if (s_reported.TryGetValue(key, out int reported) && reported == count)
                    return;

                s_reported[key] = count;
            }

            Logger.LogWarning(
                "Duplicate Ids on {Occasion}: {Count} object(s) share their Id with another object in the project. "
                + "Types: {Types}",
                occasion,
                count,
                string.Join(", ", types.Select(TypeFormat.ToString)));
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            // The count is a diagnostic, so it never fails the load, save or close it runs in.
            Logger.LogWarning(ex, "Could not count the duplicate Ids of the project on {Occasion}.", occasion);
        }
    }
}
