using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

public sealed class ScriptCompilableEffectTests
{
    [Test]
    public void CSharp_empty_script_is_treated_as_compiled()
    {
        var effect = new CSharpScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript("   ");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled));
    }

    [Test]
    public void CSharp_valid_script_compiles()
    {
        var effect = new CSharpScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript("var x = 1 + 1;");

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public void CSharp_broken_script_fails_with_compiler_message()
    {
        var effect = new CSharpScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript("this is not valid c#");

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Glsl_reports_unavailable_when_no_graphics_context()
    {
        if (GraphicsContextFactory.SharedContext is not null)
        {
            Assert.Ignore("A graphics context is available; the Unavailable path is not exercised here.");
        }

        var effect = new GLSLScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript("void main() { this does not compile }");

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Unavailable));
    }

    [TestCase("half4 apply(half4 color) { return color; } /* forgot to close")]
    [TestCase("half4 /* forgot to close apply(half4 color) { return color; }")]
    public void Sksl_unterminated_block_comment_is_reported_as_a_failure(string script)
    {
        var effect = new SKSLScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript(script);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Sksl_unterminated_block_comment_does_not_throw_while_building_a_resource()
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = "half4 apply(half4 color) { return color; } /* forgot to close";

        Assert.DoesNotThrow(() => effect.ToResource(CompositionContext.Default).Dispose(),
            "The resource update runs on the render path, where a lexer throw tears down the frame "
            + "instead of surfacing the mistake on the effect.");
    }

    [Test]
    public void Sksl_current_pixel_apply_script_compiles()
    {
        var effect = new SKSLScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript(
            """
            half4 apply(half4 color) {
                return half4(color.rgb * 0.5, color.a);
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Compiled));
            Assert.That(result.Error, Is.Null);
        });
    }
}
