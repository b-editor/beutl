using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

public sealed partial class CSharpScriptEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<CSharpScriptEffect>();

    public CSharpScriptEffect()
    {
        ScanProperties<CSharpScriptEffect>();
    }

    [Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
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
               """;
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._scriptRunner == null)
            return;

        var globals = new CSharpScriptEffectGlobals(context, r.Progress, r.Duration, r.Time);
        r._scriptRunner(globals).GetAwaiter().GetResult();
    }

    public new partial class Resource
    {
        private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();
        internal ScriptRunner<object>? _scriptRunner;
        internal string? _compiledScript;
        internal string? _compileError;

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(CSharpScriptEffect obj, RenderContext context)
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
                    "Beutl.Graphics.Effects");
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
