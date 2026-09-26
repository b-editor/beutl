using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.CSharpScriptEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CSharpScriptEffect : FilterEffect, IScriptCompilableEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<CSharpScriptEffect>();
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    public CSharpScriptEffect()
    {
        ScanProperties<CSharpScriptEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Script), ResourceType = typeof(GraphicsStrings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string> Script { get; } = Property.Create(GetDefaultScript());

    private static string GetDefaultScript()
    {
        return """
               // Available variables:
               // Context - FilterEffectContext
               // Progress - 0.0 to 1.0
               // Duration - total duration in seconds
               // Time - current time in seconds

               // Example: Apply a blur effect
               // Context.Blur(new Size(10, 10));

               // Prefer built-in composition and declarative shaders when they express the required effect.
               // Low-level GLSL fallback (inside a Context.CustomEffect callback; requires C#):
               // using var shader = CreateGlslShader(fragmentSource, inputCount: 2);
               // CreateGlslShader reuses compiled programs within this effect; dispose each returned wrapper.
               // shader.Render(execution, new[] { source, mask }, outputBounds, pushConstants)
               // returns an owned EffectTarget without changing its inputs. Dispose intermediate targets;
               // return the final target from execution.ForEach. If IsEmpty, dispose it and keep the source.
               // Use the Render overload with a destination callback for size/scale-dependent constants.
               // GLSL sampler2D inputs use set=0, bindings 0..inputCount-1; constants use layout(push_constant).
               // Constants must match the GLSL layout and occupy a multiple of 4 bytes, at most 128 bytes.
               """;
    }

    public ScriptCompilationResult ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return ScriptCompilationResult.Compiled;

        try
        {
            var roslynScript = CSharpScript.Create<object>(
                script,
                s_scriptOptions,
                typeof(CSharpScriptEffectGlobals));

            var diagnostics = roslynScript.Compile();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            return errors.Count > 0
                ? ScriptCompilationResult.Fail(string.Join(Environment.NewLine, errors.Select(e => e.GetMessage())))
                : ScriptCompilationResult.Compiled;
        }
        catch (Exception ex)
        {
            return ScriptCompilationResult.Fail(ex.Message);
        }
    }

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(CoreObject).Assembly,
                typeof(FilterEffectContext).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Beutl.Media",
                "Beutl.Engine",
                "Beutl.Graphics",
                "Beutl.Graphics.Rendering",
                "Beutl.Graphics.Effects",
                "Beutl.Graphics.Shaders");
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._scriptRunner == null)
            return;

        var globals = new CSharpScriptEffectGlobals(context, r.Progress, r.Duration, r.Time, r.GlslPrograms);
        r._scriptRunner(globals).GetAwaiter().GetResult();
    }

    public new partial class Resource
    {
        internal ScriptRunner<object>? _scriptRunner;
        internal string? _compiledScript;
        internal string? _compileError;
        internal ScriptGlslProgramCache GlslPrograms { get; } = new();

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(CSharpScriptEffect obj, CompositionContext context)
        {
            float duration = (float)obj.TimeRange.Duration.TotalSeconds;
            float time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            float progress = duration > 0 ? time / duration : 0;

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (Duration != duration || Time != time || Progress != progress)
            {
                Version++;
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator

            Duration = duration;
            Time = time;
            Progress = progress;

            CompileScript(Script);
        }

        partial void PostDispose(bool disposing)
        {
            GlslPrograms.Dispose();
        }

        private void CompileScript(string script)
        {
            if (_compiledScript == script)
                return;

            _scriptRunner = null;
            _compileError = null;
            _compiledScript = script;

            if (string.IsNullOrWhiteSpace(script))
                return;

            try
            {
                var roslynScript = CSharpScript.Create<object>(
                    script,
                    s_scriptOptions,
                    typeof(CSharpScriptEffectGlobals));

                var diagnostics = roslynScript.Compile();
                var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                if (errors.Count > 0)
                {
                    _compileError = string.Join(Environment.NewLine, errors.Select(e => e.GetMessage()));
                    s_logger.LogError("Failed to compile C# script: {ErrorText}", _compileError);
                }
                else
                {
                    _scriptRunner = roslynScript.CreateDelegate();
                }
            }
            catch (Exception ex)
            {
                _compileError = $"Compilation error: {ex.Message}";
                s_logger.LogError(ex, "Failed to compile C# script");
            }
        }

    }
}
