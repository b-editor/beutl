using System.Reflection;

using Beutl.Extensibility;

namespace Beutl.Extensibility.Abstractions.Tests;

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
        // Walk the full reference closure, not just direct references: GetReferencedAssemblies()
        // alone is pruned of unused references, so a heavy dependency added but not directly
        // type-referenced would slip past a direct-only blocklist.
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

        // Positively pin the Beutl-side boundary as a closed allowlist so any NEW Beutl.* assembly
        // entering the closure (Engine, Extensibility, NodeGraph, Editor, ...) fails even if it is
        // not named in the blocklist above.
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
                    // The name is already recorded; an assembly that is not deployed to the test
                    // output simply cannot be walked further, which is fine for the guards above.
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
