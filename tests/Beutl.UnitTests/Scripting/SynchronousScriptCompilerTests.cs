using Beutl.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.UnitTests.Scripting;

[TestFixture]
public sealed class SynchronousScriptCompilerTests
{
    [Test]
    public void LoadedAwaitIsReportedOnlyOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "loaded.csx"), "await System.Threading.Tasks.Task.Delay(1);");
            var options = ScriptOptions.Default
                .AddReferences(typeof(Task).Assembly)
                .WithSourceResolver(new SourceFileResolver([], directory));
            var script = CSharpScript.Create<object>(
                "#load \"loaded.csx\"\nawait System.Threading.Tasks.Task.Delay(1);", options);
            Assert.That(script.GetCompilation().SyntaxTrees.Count(), Is.EqualTo(2));

            var errors = SynchronousScriptCompiler.GetErrors(script);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("synchronous").And.Contain("await"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadedSynchronousScriptCanStillRun()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "loaded.csx"), "int loadedValue = 21;");
            var options = ScriptOptions.Default.WithSourceResolver(new SourceFileResolver([], directory));
            var script = CSharpScript.Create<object>("#load \"loaded.csx\"\nloadedValue * 2", options);

            Assert.That(SynchronousScriptCompiler.GetErrors(script), Is.Empty);
            Assert.That(script.CreateDelegate()().GetAwaiter().GetResult(), Is.EqualTo(42));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void BindingErrorsAreReportedAlongsideAwait()
    {
        var options = ScriptOptions.Default.AddReferences(typeof(Task).Assembly);
        var script = CSharpScript.Create<object>("await System.Threading.Tasks.Task.Delay(1); Missing();", options);

        var errors = SynchronousScriptCompiler.GetErrors(script);

        Assert.That(errors, Has.Some.Contains("Missing"));
        Assert.That(errors, Has.Some.Contains("synchronous"));
    }

    [Test]
    public void SynchronousValidationIncludesEmitErrors()
    {
        var options = ScriptOptions.Default
            .WithFilePath("script.csx")
            .WithFileEncoding(null)
            .WithEmitDebugInformation(true);
        var script = CSharpScript.Create<object>("21 * 2", options);
        Assert.That(script.GetCompilation().GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);

        var errors = SynchronousScriptCompiler.GetErrors(script);

        Assert.That(errors, Has.Some.Contains("encoding"));
    }
}
