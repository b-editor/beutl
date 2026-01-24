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

[SuppressResourceClassGeneration]
public class CSharpScriptEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<CSharpScriptEffect>();
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    private ScriptRunner<object>? _scriptRunner;
    private string? _compiledScript;
    private string? _compileError;

    public CSharpScriptEffect()
    {
        ScanProperties<CSharpScriptEffect>();
    }

    [Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
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

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(FilterEffectContext).Assembly,
                typeof(CSharpScriptEffectGlobals).Assembly,
                typeof(Size).Assembly,
                typeof(Media.Color).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Beutl.Media",
                "Beutl.Graphics",
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        CompileScript(r.Script);

        if (_scriptRunner == null)
            return;

        try
        {
            var globals = new CSharpScriptEffectGlobals(context, r.Progress, r.Duration, r.Time);
            _scriptRunner(globals).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to execute C# script");
        }
    }

    public override Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new class Resource : FilterEffect.Resource
    {
        private string _script = string.Empty;
        private float _progress;
        private float _duration;
        private float _time;

        public string Script => _script;

        public float Progress => _progress;

        public float Duration => _duration;

        public float Time => _time;

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            CompareAndUpdate(context, ((CSharpScriptEffect)obj).Script, ref _script, ref updateOnly);

            float duration = (float)obj.TimeRange.Duration.TotalSeconds;
            float time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            float progress = duration > 0 ? time / duration : 0;

            PostUpdate(duration, time, progress, ref updateOnly);
        }

        private void PostUpdate(float duration, float time, float progress, ref bool updateOnly)
        {
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (!updateOnly)
            {
                if (_duration != duration || _time != time || _progress != progress)
                {
                    Version++;
                    updateOnly = true;
                }
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator

            _duration = duration;
            _time = time;
            _progress = progress;
        }
    }
}
