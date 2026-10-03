using Beutl.Benchmarks.Rendering;

namespace Beutl.UnitTests.Benchmarks.Rendering;

public sealed class RenderPipelineBenchmarkConfigTests
{
    [Test]
    public void SkiaSharpArguments_PointTheGeneratedBuildAtBeutlsTargetsAndNativeRoot()
    {
        string[] arguments = RenderPipelineBenchmarkConfig.CreateSkiaSharpArguments()
            .Select(static argument => argument.TextRepresentation)
            .ToArray();

        Assert.That(arguments, Has.Length.EqualTo(2));
        string targets = ReadQuotedValue(arguments[0], "/p:CustomAfterMicrosoftCommonTargets=");
        string nativeRoot = ReadQuotedValue(arguments[1], "/p:BeutlSkiaSharpNativeRoot=");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(targets), Is.EqualTo("Beutl.Engine.targets"));
            Assert.That(File.Exists(targets), Is.True, targets);
            Assert.That(nativeRoot, Does.EndWith("/"), "A trailing backslash would escape the closing quote on Windows.");
            Assert.That(Directory.Exists(Path.Combine(nativeRoot, "runtimes")), Is.True, nativeRoot);
        });
    }

    // Each value is quoted because BenchmarkDotNet joins the arguments unquoted and paths may contain spaces.
    private static string ReadQuotedValue(string argument, string prefix)
    {
        Assert.That(argument, Does.StartWith(prefix + "\"").And.EndWith("\""));
        return argument[(prefix.Length + 1)..^1];
    }
}
