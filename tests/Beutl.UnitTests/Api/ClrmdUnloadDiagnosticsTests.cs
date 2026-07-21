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

            string[] dumps = Directory.GetFiles(Path.Combine(home, "log"), "unload-dump-ClrmdProbe-*.txt");
            Assert.That(dumps, Is.Not.Empty, "a dump file should have been written");

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
