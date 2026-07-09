using System.Reflection;

using Beutl.Extensibility;

namespace Beutl.Extensibility.Abstractions.Tests;

[TestFixture]
public class ExtensibilityAbstractionsAssemblyTests
{
    [Test]
    public void BaseExtensionContracts_LiveInAbstractionsAssembly()
    {
        Assert.That(typeof(Extension).Assembly.GetName().Name, Is.EqualTo("Beutl.Extensibility.Abstractions"));
        Assert.That(typeof(ExtensionSettings).Assembly, Is.SameAs(typeof(Extension).Assembly));
        Assert.That(typeof(ExportAttribute).Assembly, Is.SameAs(typeof(Extension).Assembly));
    }

    [Test]
    public void MinimalExtension_CanUseOnlyAbstractionsProject()
    {
        var extension = new MinimalExtension();

        Assert.That(extension.Name, Is.EqualTo(nameof(MinimalExtension)));
        Assert.That(extension.Settings, Is.TypeOf<MinimalSettings>());
    }

    [Test]
    public void AbstractionsAssembly_DoesNotPullInHeavyImplementationDependencies()
    {
        // Walk the full closure, not just direct references: GetReferencedAssemblies() is pruned of
        // unused references, so a heavy but not-type-referenced dependency would slip a direct-only check.
        HashSet<string> closure = CollectReferencedAssemblyClosure(typeof(Extension).Assembly);

        // The thin layer must never reach the heavy UI/media layer, directly or transitively.
        string[] heavyAssemblies =
        [
            "Beutl.Extensibility",
            "Beutl.Engine",
            "Avalonia",
            "FluentAvaloniaUI",
            "Microsoft.CodeAnalysis.CSharp.Scripting",
            "SkiaSharp",
            "SkiaSharp.HarfBuzz",
            "Vortice.XAudio2",
        ];
        foreach (string heavy in heavyAssemblies)
        {
            Assert.That(closure, Does.Not.Contain(heavy), $"Abstractions must not depend on {heavy}.");
        }

        // Closed allowlist so any new Beutl.* assembly entering the closure fails even when the
        // blocklist above doesn't name it.
        string[] allowedBeutlAssemblies =
        [
            "Beutl.Extensibility.Abstractions",
            "Beutl.Core",
            "Beutl.Configuration",
            "Beutl.Utilities",
            "Beutl.Language",
        ];
        string[] beutlInClosure = [.. closure.Where(x => x.StartsWith("Beutl.", StringComparison.Ordinal))];
        Assert.That(beutlInClosure, Is.SubsetOf(allowedBeutlAssemblies));
    }

    private static HashSet<string> CollectReferencedAssemblyClosure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.GetName().Name! };
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            foreach (AssemblyName reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name!))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // Name is already recorded; an assembly absent from the test output just can't be walked further.
                }
            }
        }

        return seen;
    }

    private sealed class MinimalExtension : Extension
    {
        public override ExtensionSettings Settings { get; } = new MinimalSettings();
    }

    private sealed class MinimalSettings : ExtensionSettings
    {
    }
}
