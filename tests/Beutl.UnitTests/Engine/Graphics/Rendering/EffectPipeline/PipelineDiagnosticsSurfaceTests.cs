using System.Reflection;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Locks the public surface of <see cref="PipelineDiagnostics"/> to "observation only": authoring callbacks
/// (<c>GeometrySession.Diagnostics</c> / <c>PassUniformContext.Diagnostics</c>) hand the live counters to plugin
/// code, so a plugin must be able to READ every counter but must NOT be able to forge one or reset the set — that
/// would make <c>IRenderer.Diagnostics</c> untrustworthy. Counter mutation and reset are engine-internal
/// (execution-plan.md §C8, observability.md O1).
/// </summary>
[TestFixture]
public class PipelineDiagnosticsSurfaceTests
{
    private static readonly string[] s_readableCounters =
    [
        nameof(PipelineDiagnostics.GpuPasses),
        nameof(PipelineDiagnostics.TargetAllocations),
        nameof(PipelineDiagnostics.PoolAcquires),
        nameof(PipelineDiagnostics.PoolMisses),
        nameof(PipelineDiagnostics.FullFrameMaterializations),
        nameof(PipelineDiagnostics.FlushSyncs),
        nameof(PipelineDiagnostics.PlanCompilations),
        nameof(PipelineDiagnostics.ProgramCreations),
        nameof(PipelineDiagnostics.PrefixCacheHits),
    ];

    [Test]
    public void NoWritablePublicFields()
    {
        var writable = typeof(PipelineDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly)
            .Select(f => f.Name);
        Assert.That(writable, Is.Empty,
            "PipelineDiagnostics must expose no writable public fields; counter mutation is engine-internal.");
    }

    [Test]
    public void NoPubliclySettableProperties()
    {
        var settable = typeof(PipelineDiagnostics)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name);
        Assert.That(settable, Is.Empty,
            "PipelineDiagnostics counters must be read-only through the public surface.");
    }

    [Test]
    public void NoPublicMutationMethods()
    {
        // Allowed public instance methods: the counter getters, Snapshot(), and inherited object members. Anything
        // else (a public Reset, an Add/Increment) would let plugin code forge counters.
        var allowed = new HashSet<string>(s_readableCounters.Select(c => "get_" + c))
        {
            nameof(PipelineDiagnostics.Snapshot),
            nameof(ToString),
            nameof(Equals),
            nameof(GetHashCode),
            nameof(GetType),
        };
        var unexpected = typeof(PipelineDiagnostics)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => !allowed.Contains(n));
        Assert.That(unexpected, Is.Empty,
            "PipelineDiagnostics must expose no public mutation method (e.g. Reset); mutation is engine-internal.");
    }

    [Test]
    public void CountersStayPubliclyReadable()
    {
        Assert.Multiple(() =>
        {
            foreach (string name in s_readableCounters)
            {
                MemberInfo[] members = typeof(PipelineDiagnostics)
                    .GetMember(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.That(members, Is.Not.Empty,
                    $"{name} must stay publicly readable for IRenderer.Diagnostics consumers.");
            }
        });
    }
}
