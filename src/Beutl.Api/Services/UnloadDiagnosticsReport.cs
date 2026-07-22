using System.Text;

namespace Beutl.Api.Services;

/// <summary>A group of surviving heap objects of one type, with the assembly it belongs to.</summary>
internal sealed record UnloadDiagnosticsObjectGroup(string TypeName, string AssemblyName, int Count);

/// <summary>A reference chain from a GC root down to a surviving plugin object.</summary>
/// <param name="TargetType">The type name of the surviving plugin object at the end of the chain.</param>
/// <param name="Hops">Human-readable hops ordered from the GC root to the target object.</param>
internal sealed record UnloadDiagnosticsRootPath(string TargetType, IReadOnlyList<string> Hops);

/// <summary>A managed thread and its captured stack frames.</summary>
internal sealed record UnloadDiagnosticsThreadStack(int ManagedThreadId, uint OsThreadId, IReadOnlyList<string> Frames);

/// <summary>
/// Formats the data collected for a failed load-context unload into a full text report and a one-line summary.
/// This type is intentionally free of any process-inspection dependency so it can be unit-tested with synthetic data.
/// </summary>
internal sealed class UnloadDiagnosticsReport
{
    private const int TopTypesInSummary = 5;

    public UnloadDiagnosticsReport(
        string packageName,
        IReadOnlyList<string> assemblyNames,
        int survivingObjectCount,
        IReadOnlyList<UnloadDiagnosticsObjectGroup> survivingTypes,
        IReadOnlyList<UnloadDiagnosticsRootPath> rootPaths,
        IReadOnlyList<UnloadDiagnosticsThreadStack> threadStacks,
        bool captureTruncated)
    {
        PackageName = packageName;
        AssemblyNames = assemblyNames;
        SurvivingObjectCount = survivingObjectCount;
        // Sort here so callers may pass groups in any order and both outputs stay deterministic. AssemblyName is the
        // final tie-breaker because the same type name can appear in two assemblies with an equal count.
        SurvivingTypes = [.. survivingTypes
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.TypeName, StringComparer.Ordinal)
            .ThenBy(x => x.AssemblyName, StringComparer.Ordinal)];
        RootPaths = rootPaths;
        ThreadStacks = threadStacks;
        CaptureTruncated = captureTruncated;
    }

    public string PackageName { get; }

    public IReadOnlyList<string> AssemblyNames { get; }

    public int SurvivingObjectCount { get; }

    public IReadOnlyList<UnloadDiagnosticsObjectGroup> SurvivingTypes { get; }

    public IReadOnlyList<UnloadDiagnosticsRootPath> RootPaths { get; }

    public IReadOnlyList<UnloadDiagnosticsThreadStack> ThreadStacks { get; }

    // Set when any capture phase (heap census, root search, or thread walk) stopped early on an object/time/frame bound.
    public bool CaptureTruncated { get; }

    public string BuildSummary()
    {
        string assemblies = AssemblyNames.Count == 0 ? "(none)" : string.Join(", ", AssemblyNames);
        string topTypes = SurvivingTypes.Count == 0
            ? "(none)"
            : string.Join(", ", SurvivingTypes.Take(TopTypesInSummary).Select(x => $"{x.TypeName} [{x.AssemblyName}] x{x.Count}"));

        return $"Package '{PackageName}' failed to unload: {SurvivingObjectCount} live object(s) across [{assemblies}]. " +
            $"Top types: {topTypes}. {RootPaths.Count} root path(s), {ThreadStacks.Count} thread(s) captured.";
    }

    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Extension unload failure diagnostics ===");
        sb.Append("Package: ").AppendLine(PackageName);
        sb.Append("Assemblies: ").AppendLine(AssemblyNames.Count == 0 ? "(none)" : string.Join(", ", AssemblyNames));
        sb.Append("Surviving objects: ").Append(SurvivingObjectCount)
            .Append(" (across ").Append(SurvivingTypes.Count).AppendLine(" type(s))");
        sb.AppendLine();

        sb.AppendLine("--- Surviving objects by type ---");
        if (SurvivingTypes.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (UnloadDiagnosticsObjectGroup group in SurvivingTypes)
            {
                sb.Append("  ").Append(group.Count).Append("  ").Append(group.TypeName)
                    .Append("  [").Append(group.AssemblyName).AppendLine("]");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- GC root paths (why the objects are still alive) ---");
        if (RootPaths.Count == 0)
        {
            sb.AppendLine("(no root path captured)");
        }
        else
        {
            for (int i = 0; i < RootPaths.Count; i++)
            {
                UnloadDiagnosticsRootPath path = RootPaths[i];
                sb.Append('#').Append(i + 1).Append(" -> ").AppendLine(path.TargetType);
                foreach (string hop in path.Hops)
                {
                    sb.Append("    ").AppendLine(hop);
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- Managed thread stacks ---");
        if (ThreadStacks.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (UnloadDiagnosticsThreadStack thread in ThreadStacks)
            {
                sb.Append("Thread managed=").Append(thread.ManagedThreadId)
                    .Append(" os=").Append(thread.OsThreadId).AppendLine();
                if (thread.Frames.Count == 0)
                {
                    sb.AppendLine("    (no managed frames)");
                }
                else
                {
                    foreach (string frame in thread.Frames)
                    {
                        sb.Append("    at ").AppendLine(frame);
                    }
                }
            }
        }

        if (CaptureTruncated)
        {
            sb.AppendLine();
            sb.AppendLine("(capture stopped early: an object, time, or frame budget exceeded; results may be partial)");
        }

        return sb.ToString();
    }
}
