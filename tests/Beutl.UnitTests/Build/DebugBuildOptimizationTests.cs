using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Build;

[TestFixture]
public class DebugBuildOptimizationTests
{
#if DEBUG
    // A project built on its own without -c used to see an empty Configuration in Directory.Build.props
    // and compile Debug with Optimize=true, so its IL differed from a solution build's.
    [Test]
    public void DebugBuildsAreCompiledWithoutOptimizations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IsJitOptimizerDisabled(typeof(DebugBuildOptimizationTests).Assembly), Is.True);
            Assert.That(IsJitOptimizerDisabled(typeof(Rect).Assembly), Is.True);
        });
    }

    private static bool IsJitOptimizerDisabled(Assembly assembly)
        => assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
#endif

    // CI tests what its solution build produced, and a solution build passes Configuration=Debug globally, so the
    // assemblies alone cannot show a standalone build regressing. Evaluate a project as a plain `dotnet build` would.
    [Test]
    public async Task AProjectEvaluatedWithoutAConfigurationIsUnoptimizedDebug()
    {
        string project = Path.Combine(RepositoryPaths.Root, "src", "Beutl.Utilities", "Beutl.Utilities.csproj");
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        foreach (string argument in new[] { "msbuild", project, "-getProperty:Configuration", "-getProperty:Optimize" })
            startInfo.ArgumentList.Add(argument);
        // MSBuild reads environment variables as properties; an inherited one would hide the default.
        startInfo.Environment.Remove("Configuration");

        (int exitCode, string output, string error) = await TestProcesses.RunAsync(startInfo, TimeSpan.FromMinutes(2));

        Assert.That(exitCode, Is.Zero, error);
        JsonNode properties = JsonNode.Parse(output)!["Properties"]!;
        Assert.Multiple(() =>
        {
            Assert.That(properties["Configuration"]!.GetValue<string>(), Is.EqualTo("Debug"));
            Assert.That(properties["Optimize"]!.GetValue<string>(), Is.EqualTo("false"));
        });
    }
}
