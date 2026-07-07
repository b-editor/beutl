using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
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
               // Session - GeometrySession (OpenCanvas(), Inputs, WorkingScale)
               // Progress - 0.0 to 1.0
               // Duration - total duration in seconds
               // Time - current time in seconds

               // The canvas already holds the input; draw on top of it.
               // var canvas = Session.OpenCanvas();
               // canvas.Canvas.DrawCircle(0, 0, 20, new SkiaSharp.SKPaint());
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
                typeof(EffectGraphBuilder).Assembly,
                typeof(SkiaSharp.SKCanvas).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Beutl.Media",
                "Beutl.Engine",
                "Beutl.Graphics",
                "Beutl.Graphics.Rendering",
                "Beutl.Graphics.Effects",
                "SkiaSharp");
    }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r._scriptRunner == null)
            return;

        ScriptRunner<object> runner = r._scriptRunner;
        float progress = r.Progress;
        float duration = r.Duration;
        float time = r.Time;

        // The executor clears the pass output, so the callback first draws the input as the passthrough baseline
        // (a no-op script leaves the content unchanged), then runs the script to draw on top through the session.
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                EffectInput input = session.Inputs[0];
                ImmediateCanvas canvas = session.OpenCanvas();
                using (canvas.PushDeviceSpace())
                {
                    input.Draw(canvas, default);
                }

                var globals = new CSharpScriptEffectGlobals(session, progress, duration, time);
                runner(globals).GetAwaiter().GetResult();
            },
            BoundsContract.Identity,
            structuralToken: nameof(CSharpScriptEffect)));
    }

    public new partial class Resource
    {
        internal ScriptRunner<object>? _scriptRunner;
        internal string? _compiledScript;
        internal string? _compileError;

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
