using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Threading;
using Beutl.UnitTests.TestInfrastructure;

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

    [TestCase("await System.Threading.Tasks.Task.Delay(1);")]
    [TestCase("await System.Threading.Tasks.Task.Delay(-1);")]
    [TestCase("await System.Threading.Tasks.Task.Yield();")]
    [TestCase("async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Delay(1); } Run();")]
    [TestCase("System.Func<System.Threading.Tasks.Task> run = async () => { await System.Threading.Tasks.Task.Delay(1); }; run();")]
    [TestCase("await foreach (var x in Values()) { } async System.Collections.Generic.IAsyncEnumerable<int> Values() { yield return 1; }")]
    [TestCase("await foreach (var (x, y) in Values()) { } async System.Collections.Generic.IAsyncEnumerable<(int, int)> Values() { yield return (1, 2); }")]
    [TestCase("{ await using var x = new Resource(); } class Resource : System.IAsyncDisposable { public System.Threading.Tasks.ValueTask DisposeAsync() => default; }")]
    [TestCase("await using (var x = new Resource()) { } class Resource : System.IAsyncDisposable { public System.Threading.Tasks.ValueTask DisposeAsync() => default; }")]
    public void CSharp_all_await_forms_are_rejected(string script)
    {
        var effect = new CSharpScriptEffect();

        ScriptCompilationResult result = effect.ValidateScript(script);

        Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed));
        Assert.That(result.Error, Does.Contain("synchronous").And.Contain("await"));
    }

    [TestCase("var text = \"await System.Threading.Tasks.Task.Delay(1);\";")]
    [TestCase("// await System.Threading.Tasks.Task.Delay(1);\nvar x = 1;")]
    [TestCase("var @await = 1;")]
    public void CSharp_await_in_text_or_identifiers_is_allowed(string script)
    {
        var effect = new CSharpScriptEffect();

        Assert.That(effect.ValidateScript(script).Status, Is.EqualTo(ScriptCompilationStatus.Compiled));
    }

    [TestCase(1)]
    [TestCase(-1)]
    public async Task CSharp_resource_rejects_await_without_blocking_the_dispatcher(int delay)
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                var effect = new CSharpScriptEffect();
                effect.Script.Value = $"Context.Blur(new Size(2, 2)); await System.Threading.Tasks.Task.Delay({delay});";
                using var resource = effect.ToResource(CompositionContext.Default);
                using var context = new FilterEffectContext(new Rect(0, 0, 10, 10));

                Assert.That(resource._scriptRunner, Is.Null);
                context.ApplyTransactional(resource);
                Assert.That(context._items, Is.Empty, "Rejected scripts must not apply a partial effect.");

                effect.Script.Value = "Context.Blur(new Size(2, 2));";
                bool versionBumped = false;
                resource.Reconcile(effect, CompositionContext.Default, ref versionBumped);
                Assert.That(resource._scriptRunner, Is.Not.Null);
                context.ApplyTransactional(resource);
                Assert.That(context._items, Has.Count.EqualTo(1));
            }).WaitAsync(TimeSpan.FromSeconds(10));

            await dispatcher.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [Test]
    [NonParallelizable]
    public void Glsl_validates_without_a_graphics_context()
    {
        using var scope = InstalledGraphicsScope.Install(
            new InstalledGraphics(null, null, null, FailedToInitialize: false));
        var effect = new GLSLScriptEffect();

        ScriptCompilationResult broken = effect.ValidateScript("void main() { this does not compile }");
        ScriptCompilationResult defaultScript = effect.ValidateScript(effect.FragmentShader.Value);

        Assert.Multiple(() =>
        {
            Assert.That(broken.Status, Is.EqualTo(ScriptCompilationStatus.Failed));
            Assert.That(broken.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(defaultScript.Status, Is.EqualTo(ScriptCompilationStatus.Compiled));
        });
    }

    // Valid GLSL that the effect's pass cannot bind: a second sampler, or more push constants than it provides.
    [TestCase("layout(set = 0, binding = 1) uniform sampler2D mask;", "texture(mask, fragCoord)")]
    [TestCase("layout(push_constant) uniform PushConstants { float progress; float duration; float time; "
        + "float width; float height; float scale; float extra; } pc;", "pc.extra")]
    public void Glsl_validation_fails_for_a_script_the_effect_cannot_run(string declaration, string factor)
    {
        string script = $$"""
            #version 450
            layout(location = 0) in vec2 fragCoord;
            layout(location = 0) out vec4 outColor;
            layout(set = 0, binding = 0) uniform sampler2D srcTexture;
            {{declaration}}
            void main() { outColor = texture(srcTexture, fragCoord) * {{factor}}; }
            """;
        var effect = new GLSLScriptEffect();
        effect.FragmentShader.Value = script;
        using var resource = (GLSLScriptEffect.Resource)effect.ToResource(CompositionContext.Default);

        ScriptCompilationResult result = effect.ValidateScript(script);

        Assert.Multiple(() =>
        {
            Assert.That(resource._program, Is.Null, "The effect stays inactive for this script.");
            Assert.That(result.Status, Is.EqualTo(ScriptCompilationStatus.Failed));
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        });
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
        effect.Script.Value = "half4 apply(half4 color) { return color; } /* forgot to close";

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
