using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

public sealed partial class PackageApiCheckerTests
{
    private const string EffectsV1 = """
        namespace HostLib;
        public abstract class Effect
        {
            public virtual int GetLatency(int sampleRate) => 0;
            public virtual void Reset() { }
            public abstract void Process(float[] samples);
            protected virtual void OnChanged() { }
        }
        public abstract class Filter<T>
        {
            public abstract T Apply(T value);
            public virtual T Clamp(T value) => value;
        }
        """;

    // Gives GetLatency another parameter, as #2740 did, adds the abstract Flush, and makes Clamp non-virtual.
    private const string EffectsV2 = """
        namespace HostLib;
        public abstract class Effect
        {
            public virtual int GetLatency(int sampleRate, int channels) => 0;
            public virtual void Reset() { }
            public abstract void Process(float[] samples);
            public abstract void Flush();
            protected virtual void OnChanged() { }
        }
        public abstract class Filter<T>
        {
            public abstract T Apply(T value);
            public T Clamp(T value) => value;
        }
        """;

    private const string PluginOverridingV1 = """
        using HostLib;
        public class Echo : Effect
        {
            public override int GetLatency(int sampleRate) => 1;
            public override void Reset() { }
            public override void Process(float[] samples) { }
            protected override void OnChanged() { }
        }
        internal sealed class Quiet : Effect
        {
            public override void Process(float[] samples) { }
        }
        public abstract class Partial : Effect;
        public sealed class Doubler : Filter<int>
        {
            public override int Apply(int value) => value * 2;
            public override int Clamp(int value) => value;
        }
        """;

    [Test]
    public void Reports_overrides_that_lost_their_base_and_abstract_methods_left_unimplemented()
    {
        string v1 = Compile("v1", "HostLib", "1.0.0.0", EffectsV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginOverridingV1, v1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiChecker.Check([plugin], Host(v1)), Is.Empty);
            Assert.That(PackageApiChecker.Check([plugin], Host(Compile("v2", "HostLib", "2.0.0.0", EffectsV2))), Is.EqualTo(new PackageApiProblem[]
            {
                new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Effect.GetLatency(int)"),
                new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Filter`1.Clamp(int)"),
                new(PackageApiProblemKind.UnimplementedMethod, "HostLib", "HostLib.Effect.Flush()"),
            }));
        }
    }

    [Test]
    public void Reports_the_base_methods_that_a_package_built_for_a_newer_host_overrides_and_the_host_lacks()
    {
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", """
            public class Echo : HostLib.Effect
            {
                public override int GetLatency(int sampleRate, int channels) => 1;
                public override void Process(float[] samples) { }
                public override void Flush() { }
            }
            """, Compile("v2", "HostLib", "2.0.0.0", EffectsV2));

        Assert.That(PackageApiChecker.Check([plugin], Host(Compile("v1", "HostLib", "1.0.0.0", EffectsV1))), Is.EqualTo(new PackageApiProblem[]
        {
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Effect.Flush()"),
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Effect.GetLatency(int, int)"),
        }));
    }
}
