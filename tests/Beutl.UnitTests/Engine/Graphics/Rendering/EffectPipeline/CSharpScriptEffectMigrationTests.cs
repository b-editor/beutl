using Beutl.Graphics.Effects;
using Beutl.Logging;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The CSharpScriptEffect breaking-change gate (feature 004, T046, contracts/breaking-changes.md). The imperative
/// <c>FilterEffectContext</c> globals surface was removed in favour of a <c>GeometrySession</c>; a legacy script
/// that references the removed <c>Context</c> must fail at script compile time with a diagnostic pointing at the
/// migration guide (never silently wrong output), while a migrated script that draws through <c>Session</c>
/// compiles. This is a pure Roslyn compile check, so it needs no GPU.
/// </summary>
[TestFixture]
public class CSharpScriptEffectMigrationTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    [Test]
    public void LegacyScript_FailsWithMigrationDiagnostic()
    {
        ScriptCompilationResult result =
            new CSharpScriptEffect().ValidateScript("Context.Blur(new Beutl.Graphics.Size(4, 4));");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed),
            "A legacy Context-based script must fail to compile.");
        Assert.That(result.Error, Does.Contain("breaking-changes.md"),
            "The diagnostic must point the author at the migration guide.");
        Assert.That(result.Error, Does.Contain("Session"),
            "The diagnostic must name the replacement GeometrySession surface.");
    }

    [Test]
    public void MigratedScript_CompilesCleanly()
    {
        string script =
            "var canvas = Session.OpenCanvas();\n"
            + "canvas.Clear();\n"
            + "using (canvas.PushOpacity(0.5f))\n"
            + "using (canvas.PushDeviceSpace())\n"
            + "    Session.Inputs[0].Draw(canvas, default);";

        ScriptCompilationResult result = new CSharpScriptEffect().ValidateScript(script);

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled),
            $"A migrated GeometrySession script must compile. Diagnostic: {result.Error}");
    }

    [Test]
    public void EmptyScript_IsAccepted()
    {
        Assert.That(new CSharpScriptEffect().ValidateScript(string.Empty).Status,
            Is.EqualTo(ScriptCompilationStatus.Compiled));
    }
}
