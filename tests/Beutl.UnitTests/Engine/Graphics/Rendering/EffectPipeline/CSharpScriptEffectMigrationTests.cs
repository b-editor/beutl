using Beutl.Graphics.Effects;
using Beutl.Logging;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The CSharpScriptEffect breaking-change gate (feature 004, T046, contracts/breaking-changes.md). Script globals
/// now author the declarative surface through an <c>EffectGraphBuilder</c> (<c>Builder</c>): the removed imperative
/// <c>Context</c> and the interim <c>Session</c> (<c>GeometrySession</c>) globals both fail at script compile time
/// with a diagnostic pointing at the migration guide (never silently wrong output), while a migrated script that
/// appends nodes through <c>Builder</c> — convenience filters and <c>Builder.Geometry(...)</c> drawing — compiles.
/// This is a pure Roslyn compile check, so it needs no GPU.
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
    public void LegacyContextScript_FailsWithMigrationDiagnostic()
    {
        ScriptCompilationResult result =
            new CSharpScriptEffect().ValidateScript("Context.Blur(new Beutl.Graphics.Size(4, 4));");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed),
            "A legacy Context-based script must fail to compile.");
        Assert.That(result.Error, Does.Contain("breaking-changes.md"),
            "The diagnostic must point the author at the migration guide.");
        Assert.That(result.Error, Does.Contain("Builder"),
            "The diagnostic must name the replacement EffectGraphBuilder surface.");
    }

    [Test]
    public void InterimSessionScript_FailsWithMigrationDiagnostic()
    {
        ScriptCompilationResult result =
            new CSharpScriptEffect().ValidateScript("var canvas = Session.OpenCanvas();");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed),
            "A script written against the interim GeometrySession global must fail to compile.");
        Assert.That(result.Error, Does.Contain("breaking-changes.md"),
            "The diagnostic must point the author at the migration guide.");
        Assert.That(result.Error, Does.Contain("Builder"),
            "The diagnostic must name the replacement EffectGraphBuilder surface.");
    }

    [Test]
    public void MigratedFilterScript_CompilesCleanly()
    {
        ScriptCompilationResult result =
            new CSharpScriptEffect().ValidateScript("Builder.Blur(new Size(4, 4));");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled),
            $"A migrated Builder filter script must compile. Diagnostic: {result.Error}");
    }

    [Test]
    public void MigratedGeometryScript_CompilesCleanly()
    {
        string script =
            "Builder.Geometry(session =>\n"
            + "{\n"
            + "    var canvas = session.OpenCanvas();\n"
            + "    using (canvas.PushDeviceSpace())\n"
            + "        session.Inputs[0].Draw(canvas, default);\n"
            + "});";

        ScriptCompilationResult result = new CSharpScriptEffect().ValidateScript(script);

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled),
            $"A migrated Builder.Geometry drawing script must compile. Diagnostic: {result.Error}");
    }

    [Test]
    public void EmptyScript_IsAccepted()
    {
        Assert.That(new CSharpScriptEffect().ValidateScript(string.Empty).Status,
            Is.EqualTo(ScriptCompilationStatus.Compiled));
    }
}
