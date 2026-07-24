using System.Runtime.CompilerServices;

using Beutl.Api.Services;

using Microsoft.Diagnostics.Runtime;

namespace Beutl.UnitTests.Api;

// Explicit: it snapshots the whole test-host process (slow, and CreateSnapshotAndAttach needs `createdump`,
// which is unavailable in some sandboxes). Run it manually to verify the ClrMD path end to end; it self-skips
// when snapshotting is not supported. This is the manual-verification companion to UnloadDiagnosticsReportTests.
[TestFixture]
[Explicit]
[NonParallelizable]
public class ClrmdUnloadDiagnosticsTests
{
    private sealed class LeakyProbe
    {
        public byte[] Payload { get; } = new byte[64];
    }

    // Static so the probes are GC-rooted and therefore guaranteed to survive into the snapshot.
    private static readonly List<LeakyProbe> s_rooted = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateRootedProbes()
    {
        for (int i = 0; i < 32; i++)
        {
            s_rooted.Add(new LeakyProbe());
        }
    }

    [Test]
    public void CaptureUnloadFailure_WritesDump_NamingSurvivingProbeType()
    {
        if (!CanSnapshotSelf())
        {
            Assert.Ignore("CreateSnapshotAndAttach is not supported in this environment.");
        }

        string home = Path.Combine(Path.GetTempPath(), "beutl-clrmd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        string? previousHome = Environment.GetEnvironmentVariable("BEUTL_HOME");
        try
        {
            Environment.SetEnvironmentVariable("BEUTL_HOME", home);
            AllocateRootedProbes();

            var diagnostics = new ClrmdLoadContextUnloadDiagnostics();
            diagnostics.CaptureUnloadFailure("ClrmdProbe", ["Beutl.UnitTests"]);

            string[] dumps = Directory.GetFiles(
                ClrmdLoadContextUnloadDiagnostics.GetDumpDirectory(), "unload-dump-ClrmdProbe-*.txt");
            Assert.Multiple(() =>
            {
                Assert.That(dumps, Is.Not.Empty, "a dump file should have been written");
                Assert.That(
                    Directory.Exists(Path.Combine(home, "log", "unload-dumps")),
                    "dumps should live in the dedicated log/unload-dumps subdirectory");
            });

            string content = File.ReadAllText(dumps[0]);
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("LeakyProbe"), "the surviving probe type should be listed");
                Assert.That(content, Does.Contain("Managed thread stacks"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("BEUTL_HOME", previousHome);
            s_rooted.Clear();
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // Rooted so the collectible context survives into the snapshot; no plugin instance is kept, which is the "only the
    // load context is retained" path the heap census cannot explain on its own. A plain collectible AssemblyLoadContext
    // stands in for PluginLoadContext here: re-hosting this framework-dependent test assembly in PluginLoadContext's
    // custom resolver fails to bind the runtime facades, and FindLoadContextTargets reads AssemblyLoadContextAddress the
    // same way for any load-context subtype.
    private static System.Runtime.Loader.AssemblyLoadContext? s_leakedContext;

    // Verifies the load-context anchoring in isolation: an assembly loaded into a live collectible context must resolve
    // to that context object even with no surviving instance of its types. Scoped to FindLoadContextTargets (module
    // metadata only) rather than a full CaptureUnloadFailure so it does not depend on the whole-heap walk, which can
    // exceed the capture budget on a large test host.
    [Test]
    public void FindLoadContextTargets_ResolvesTheCollectibleContext_WithNoSurvivingInstance()
    {
        if (!CanSnapshotSelf())
        {
            Assert.Ignore("CreateSnapshotAndAttach is not supported in this environment.");
        }

        string location = typeof(ClrmdUnloadDiagnosticsTests).Assembly.Location;
        var context = new System.Runtime.Loader.AssemblyLoadContext("unload-diag-probe", isCollectible: true);
        var collectible = context.LoadFromAssemblyPath(location);
        // Construct a type so its MethodTable exists (ResolveLoadContextAddress reads the ALC off a type), but keep no
        // instance; then collect any transient garbage so only the context roots the collectible module.
        Assert.That(collectible.CreateInstance(typeof(ContextProbe).FullName!), Is.Not.Null);
        s_leakedContext = context;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            using DataTarget dataTarget = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
            using ClrRuntime runtime = dataTarget.ClrVersions[0].CreateRuntime();

            HashSet<ulong> targets = ClrmdLoadContextUnloadDiagnostics.FindLoadContextTargets(
                runtime, new HashSet<string>(["Beutl.UnitTests"], StringComparer.OrdinalIgnoreCase));

            var resolved = targets
                .Select(address => runtime.Heap.GetObject(address))
                .Where(o => o.IsValid)
                .ToList();

            // Every resolved anchor must be a real AssemblyLoadContext object, and the collectible context this test
            // added (a base AssemblyLoadContext, not the runtime's DefaultAssemblyLoadContext) must be among them.
            Assert.That(resolved, Is.Not.Empty, "the loaded plugin module should resolve to at least one load context");
            Assert.That(
                resolved.All(o => DerivesFromAssemblyLoadContext(o.Type)), Is.True,
                "every resolved anchor should be an AssemblyLoadContext object");
            Assert.That(
                resolved.Any(o => o.Type?.Name == "System.Runtime.Loader.AssemblyLoadContext"), Is.True,
                "the collectible context that loaded the assembly should be among the resolved anchors");
        }
        finally
        {
            s_leakedContext = null;
        }
    }

    private static bool DerivesFromAssemblyLoadContext(ClrType? type)
    {
        for (ClrType? t = type; t is not null; t = t.BaseType)
        {
            if (t.Name == "System.Runtime.Loader.AssemblyLoadContext")
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanSnapshotSelf()
    {
        try
        {
            using DataTarget dataTarget = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
            return !dataTarget.ClrVersions.IsDefaultOrEmpty;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// Top-level and dependency-free so a collectible context can construct its MethodTable by loading only this assembly;
// nesting it in the fixture would drag in the test's NUnit / ClrMD references and fail to resolve in that context.
internal sealed class ContextProbe;
