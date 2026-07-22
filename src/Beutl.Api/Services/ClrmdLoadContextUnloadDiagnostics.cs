using System.Diagnostics;

using Beutl.Logging;

using Microsoft.Diagnostics.Runtime;

using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

/// <summary>
/// ClrMD-backed <see cref="ILoadContextUnloadDiagnostics"/>. It snapshots the current process (an out-of-band copy,
/// so walking it adds no GC roots to the live heap) and records the surviving plugin objects, the reference chains
/// from GC roots that keep them alive, and the managed thread stacks.
/// </summary>
internal sealed class ClrmdLoadContextUnloadDiagnostics : ILoadContextUnloadDiagnostics
{
    // The whole capture runs only on the rare unload-failure path; these caps keep a large heap from hanging it.
    private static readonly TimeSpan s_budget = TimeSpan.FromSeconds(30);
    private const int MaxRootPaths = 15;
    private const int MaxVisitedObjects = 300_000;
    private const int MaxFramesPerThread = 200;

    // Dumps get their own directory that the app-side log housekeeping never scans, so PruneOldDumps is their only
    // retention bound. Unload failures are rare, so keeping a few is plenty of history.
    private const int MaxRetainedDumps = 5;

    private readonly ILogger _logger = Log.CreateLogger<ClrmdLoadContextUnloadDiagnostics>();

    public void CaptureUnloadFailure(string packageName, IReadOnlyList<string> assemblySimpleNames)
    {
        try
        {
            var pluginAssemblies = new HashSet<string>(assemblySimpleNames, StringComparer.OrdinalIgnoreCase);
            if (pluginAssemblies.Count == 0)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            using DataTarget dataTarget = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
            if (dataTarget.ClrVersions.IsDefaultOrEmpty)
            {
                _logger.LogWarning("No CLR found in snapshot; cannot capture unload diagnostics for {PackageName}.", packageName);
                return;
            }

            using ClrRuntime runtime = dataTarget.ClrVersions[0].CreateRuntime();
            ClrHeap heap = runtime.Heap;
            if (!heap.CanWalkHeap)
            {
                _logger.LogWarning("Snapshot heap is not walkable; cannot capture unload diagnostics for {PackageName}.", packageName);
                return;
            }

            (List<UnloadDiagnosticsObjectGroup> groups, int total, HashSet<ulong> targets, bool censusTruncated) =
                CensusSurvivingObjects(heap, pluginAssemblies, stopwatch);

            (List<UnloadDiagnosticsRootPath> rootPaths, bool rootTruncated) =
                FindRootPaths(heap, targets, stopwatch);

            (List<UnloadDiagnosticsThreadStack> threads, bool threadTruncated) =
                CaptureThreadStacks(runtime, pluginAssemblies, stopwatch);

            var report = new UnloadDiagnosticsReport(
                packageName, assemblySimpleNames, total, groups, rootPaths, threads,
                censusTruncated || rootTruncated || threadTruncated);

            string? dumpPath = TryWriteReport(packageName, report);
            _logger.LogWarning(
                "{Summary} Dump file: {DumpPath}", report.BuildSummary(), dumpPath ?? "(not written)");
        }
        catch (Exception ex)
        {
            // Diagnostics must never disturb the uninstall flow; swallow everything after logging.
            _logger.LogError(ex, "Failed to capture unload diagnostics for {PackageName}.", packageName);
        }
    }

    private (List<UnloadDiagnosticsObjectGroup> Groups, int Total, HashSet<ulong> Targets, bool Truncated) CensusSurvivingObjects(
        ClrHeap heap, HashSet<string> pluginAssemblies, Stopwatch stopwatch)
    {
        var counts = new Dictionary<(string Assembly, string TypeName), int>();
        var targets = new HashSet<ulong>();
        int total = 0;
        bool truncated = false;

        foreach (ClrObject obj in heap.EnumerateObjects())
        {
            if (stopwatch.Elapsed > s_budget)
            {
                truncated = true;
                break;
            }

            ClrType? type = obj.Type;
            if (type?.Module?.Name is not { } moduleName)
            {
                continue;
            }

            string assembly = Path.GetFileNameWithoutExtension(moduleName);
            if (!pluginAssemblies.Contains(assembly))
            {
                continue;
            }

            total++;
            string typeName = type.Name ?? "<unknown>";
            CountSurvivor(counts, assembly, typeName);

            if (targets.Count < MaxRootPaths && obj.Address != 0)
            {
                targets.Add(obj.Address);
            }
        }

        return (ToObjectGroups(counts), total, targets, truncated);
    }

    // Keyed by (assembly, type) so identical type names from different plugin assemblies stay distinct; keying by
    // type name alone would merge their counts and mislabel the assembly.
    internal static void CountSurvivor(
        Dictionary<(string Assembly, string TypeName), int> counts, string assembly, string typeName)
    {
        (string Assembly, string TypeName) key = (assembly, typeName);
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    internal static List<UnloadDiagnosticsObjectGroup> ToObjectGroups(
        Dictionary<(string Assembly, string TypeName), int> counts) =>
        counts
            .Select(kv => new UnloadDiagnosticsObjectGroup(kv.Key.TypeName, kv.Key.Assembly, kv.Value))
            .ToList();

    private static (List<UnloadDiagnosticsRootPath> Paths, bool Truncated) FindRootPaths(
        ClrHeap heap, HashSet<ulong> targets, Stopwatch stopwatch)
    {
        var results = new List<UnloadDiagnosticsRootPath>();
        if (targets.Count == 0)
        {
            return (results, false);
        }

        var remaining = new HashSet<ulong>(targets);
        // Forward BFS from every GC root. parent maps each visited object to the edge that first reached it,
        // so a path back to its root can be reconstructed without a full reverse reference graph. (ClrMD 4.x has
        // no GCRoot helper.)
        var parent = new Dictionary<ulong, (ulong Parent, string Edge, string Type)>();
        var queue = new Queue<ClrObject>();
        bool truncated = false;

        foreach (ClrRoot root in heap.EnumerateRoots())
        {
            // A root-heavy process can blow the object/time bound during seeding alone, before the walk below runs.
            if (parent.Count >= MaxVisitedObjects || stopwatch.Elapsed > s_budget)
            {
                truncated = true;
                break;
            }

            ClrObject obj = root.Object;
            if (!obj.IsValid || obj.Address == 0 || parent.ContainsKey(obj.Address))
            {
                continue;
            }

            parent[obj.Address] = (0, root.RootKind.ToString(), obj.Type?.Name ?? "<unknown>");
            queue.Enqueue(obj);
        }

        int visited = 0;
        while (queue.Count > 0)
        {
            if (remaining.Count == 0 || results.Count >= MaxRootPaths)
            {
                break;
            }

            if (visited >= MaxVisitedObjects || parent.Count >= MaxVisitedObjects || stopwatch.Elapsed > s_budget)
            {
                truncated = true;
                break;
            }

            ClrObject current = queue.Dequeue();
            visited++;

            if (remaining.Remove(current.Address))
            {
                // Record the path but keep walking this object's children: a selected survivor can be the only route
                // to another selected survivor, which would otherwise get no root path.
                results.Add(BuildPath(current.Address, parent));
            }

            foreach (ClrReference reference in current.EnumerateReferencesWithFields(carefully: true, considerDependantHandles: true))
            {
                // Children are enqueued faster than they are dequeued, and a huge reference array can add no new
                // parent entries at all, so bound by both the map size and the time budget here.
                if (parent.Count >= MaxVisitedObjects || stopwatch.Elapsed > s_budget)
                {
                    truncated = true;
                    break;
                }

                ClrObject child = reference.Object;
                if (!child.IsValid || child.Address == 0 || parent.ContainsKey(child.Address))
                {
                    continue;
                }

                parent[child.Address] = (current.Address, DescribeEdge(reference), child.Type?.Name ?? "<unknown>");
                queue.Enqueue(child);
            }
        }

        return (results, truncated);
    }

    internal static UnloadDiagnosticsRootPath BuildPath(
        ulong targetAddress, Dictionary<ulong, (ulong Parent, string Edge, string Type)> parent)
    {
        const int MaxHops = 128;
        var reversed = new List<string>();
        string targetType = "<unknown>";
        ulong address = targetAddress;
        bool reachedRoot = false;

        for (int guard = 0; guard < MaxHops && parent.TryGetValue(address, out (ulong Parent, string Edge, string Type) node); guard++)
        {
            if (address == targetAddress)
            {
                targetType = node.Type;
            }

            reversed.Add($"{node.Edge} -> {node.Type} (0x{address:x})");
            if (node.Parent == 0)
            {
                reachedRoot = true;
                break;
            }

            address = node.Parent;
        }

        if (!reachedRoot)
        {
            // Hit the hop cap before a GC root; mark it so a cut chain isn't read as reaching the root.
            reversed.Add($"... (path truncated after {MaxHops} hops; GC root not reached)");
        }

        reversed.Reverse();
        return new UnloadDiagnosticsRootPath(targetType, reversed);
    }

    private static string DescribeEdge(ClrReference reference)
    {
        if (reference.IsDependentHandle)
        {
            return "[dependent handle]";
        }

        if (reference.IsField && reference.Field?.Name is { } fieldName)
        {
            return $"field {fieldName}";
        }

        if (reference.IsArrayElement)
        {
            return "[array element]";
        }

        return "references";
    }

    private static (List<UnloadDiagnosticsThreadStack> Threads, bool Truncated) CaptureThreadStacks(
        ClrRuntime runtime, HashSet<string> pluginAssemblies, Stopwatch stopwatch)
    {
        var results = new List<UnloadDiagnosticsThreadStack>();
        bool truncated = false;
        foreach (ClrThread thread in runtime.Threads)
        {
            if (stopwatch.Elapsed > s_budget)
            {
                truncated = true;
                break;
            }

            if (!thread.IsAlive)
            {
                continue;
            }

            var frames = new List<string>();
            foreach (ClrStackFrame frame in thread.EnumerateStackTrace(includeContext: false))
            {
                // A single deep stack can exceed the shared budget on its own, so check per frame, not just per thread.
                if (stopwatch.Elapsed > s_budget)
                {
                    truncated = true;
                    break;
                }

                if (frames.Count >= MaxFramesPerThread)
                {
                    truncated = true;
                    break;
                }

                frames.Add(DescribeFrame(frame, pluginAssemblies));
            }

            // Keep alive threads with no managed frames too: a native-only stack is itself a signal, and the report
            // renders it as "(no managed frames)".
            results.Add(new UnloadDiagnosticsThreadStack(thread.ManagedThreadId, thread.OSThreadId, frames));

            if (truncated)
            {
                break;
            }
        }

        return (results, truncated);
    }

    private static string DescribeFrame(ClrStackFrame frame, HashSet<string> pluginAssemblies)
    {
        ClrMethod? method = frame.Method;
        if (method is null)
        {
            return string.IsNullOrEmpty(frame.FrameName) ? "[unknown frame]" : $"[{frame.FrameName}]";
        }

        string signature = method.Signature ?? method.Name ?? "<unknown method>";
        if (method.Type?.Module?.Name is { } moduleName
            && pluginAssemblies.Contains(Path.GetFileNameWithoutExtension(moduleName)))
        {
            return $"{signature}  <-- plugin";
        }

        return signature;
    }

    internal static string GetDumpDirectory() =>
        Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log", "unload-dumps");

    private string? TryWriteReport(string packageName, UnloadDiagnosticsReport report)
    {
        try
        {
            string dumpDir = GetDumpDirectory();
            Directory.CreateDirectory(dumpDir);
            string safeName = string.Concat(packageName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            // UTC (matching the LastWriteTimeUtc retention sort) with milliseconds and a GUID: concurrent captures
            // for the same package would otherwise share a second-resolution name and overwrite each other.
            string fileName = $"unload-dump-{safeName}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.txt";
            string path = Path.Combine(dumpDir, fileName);
            File.WriteAllText(path, report.BuildReport());
            PruneOldDumps(dumpDir, MaxRetainedDumps);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write unload diagnostics dump for {PackageName}.", packageName);
            return null;
        }
    }

    // Best-effort retention: keep the newest dumps by write time and drop the rest. Never throws, so a failed prune
    // cannot mask the dump that was just written.
    internal static void PruneOldDumps(string logDir, int maxRetained)
    {
        try
        {
            var dir = new DirectoryInfo(logDir);
            if (!dir.Exists)
            {
                return;
            }

            FileInfo[] dumps = dir.GetFiles("unload-dump-*.txt");
            if (dumps.Length <= maxRetained)
            {
                return;
            }

            foreach (FileInfo old in dumps.OrderByDescending(f => f.LastWriteTimeUtc).Skip(maxRetained))
            {
                try
                {
                    old.Delete();
                }
                catch
                {
                    // A racing host may have removed it already; skip and continue.
                }
            }
        }
        catch
        {
            // Retention must never disturb the uninstall flow.
        }
    }
}
