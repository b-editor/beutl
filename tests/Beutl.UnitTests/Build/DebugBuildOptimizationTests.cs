using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Beutl.Graphics;

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
        string project = Path.Combine(FindRepositoryRoot(), "src", "Beutl.Utilities", "Beutl.Utilities.csproj");
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "msbuild", project, "-getProperty:Configuration", "-getProperty:Optimize" })
            startInfo.ArgumentList.Add(argument);
        // MSBuild reads environment variables as properties; an inherited one would hide the default.
        startInfo.Environment.Remove("Configuration");

        using Process process = Process.Start(startInfo)!;
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token);

            Assert.That(process.ExitCode, Is.Zero, await error);
            JsonNode properties = JsonNode.Parse(await output)!["Properties"]!;
            Assert.Multiple(() =>
            {
                Assert.That(properties["Configuration"]!.GetValue<string>(), Is.EqualTo("Debug"));
                Assert.That(properties["Optimize"]!.GetValue<string>(), Is.EqualTo("false"));
            });
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
    }
}
