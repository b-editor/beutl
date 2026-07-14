using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.CSharpScriptEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CSharpScriptEffect : FilterEffect, IScriptCompilableEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<CSharpScriptEffect>();
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();
    private const string ContextMigrationDiagnostic =
        "CSharpScriptEffect no longer exposes FilterEffectContext. Author the declarative effect graph through "
        + "'Builder' (EffectGraphBuilder): e.g. 'Context.Blur(...)' becomes 'Builder.Blur(...)'. See the migration "
        + "guide at docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md.";
    private const string SessionMigrationDiagnostic =
        "The 'Session' (GeometrySession) global was replaced by 'Builder' (EffectGraphBuilder). Draw through "
        + "'Builder.Geometry(session => { ... })'. See the migration guide at "
        + "docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md.";

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
               // Builder  - EffectGraphBuilder (Blur, DropShadow, Saturate, ColorMatrix, Transform, Geometry(...), ...)
               // Progress - 0.0 to 1.0
               // Duration - total duration in seconds
               // Time     - current time in seconds

               // Append declarative effect nodes, exactly like a compiled effect author:
               // Builder.Blur(new Size(4, 4));

               // Custom canvas drawing (full-frame bounds by default; draw the input first to keep it as a baseline):
               // Builder.Geometry(session =>
               // {
               //     var canvas = session.OpenCanvas();
               //     using (canvas.PushDeviceSpace())
               //         session.Inputs[0].Draw(canvas, default);
               //     canvas.DrawEllipse(new Rect(20, 20, 40, 40), Brushes.Resource.White, null);
               // });
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
                ? ScriptCompilationResult.Fail(FormatCompilationErrors(roslynScript, errors))
                : ScriptCompilationResult.Compiled;
        }
        catch (Exception ex)
        {
            return ScriptCompilationResult.Fail(ex.Message);
        }
    }

    private static string FormatCompilationErrors(Script<object> script, IReadOnlyList<Diagnostic> errors)
    {
        var messages = new List<string>();
        Compilation compilation = script.GetCompilation();
        HashSet<string> unresolvedIdentifiers = compilation.SyntaxTrees
            .SelectMany(tree =>
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                return tree.GetRoot().DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(IsUnqualifiedIdentifier)
                    .Where(identifier => model.GetSymbolInfo(identifier).Symbol == null)
                    .Select(identifier => identifier.Identifier.ValueText);
            })
            .ToHashSet(StringComparer.Ordinal);

        if (unresolvedIdentifiers.Contains("Context"))
            messages.Add(ContextMigrationDiagnostic);
        if (unresolvedIdentifiers.Contains("Session"))
            messages.Add(SessionMigrationDiagnostic);
        messages.AddRange(errors.Select(static error => error.GetMessage()));
        return string.Join(Environment.NewLine, messages);
    }

    private static bool IsUnqualifiedIdentifier(IdentifierNameSyntax identifier)
    {
        return identifier.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == identifier => false,
            MemberBindingExpressionSyntax binding when binding.Name == identifier => false,
            _ => true,
        };
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
        var globals = new CSharpScriptEffectGlobals(builder, r.Progress, r.Duration, r.Time);

        // The script authors the declarative graph at describe time, exactly like a compiled effect. A runtime throw
        // must neither crash the render nor corrupt the shared builder a chain's other effects also append to: the
        // isolation unit discards this effect's partial appends and logs, so the effect degrades to identity
        // (pass-through) — the same outcome as an empty or failed-to-compile script.
        builder.AppendIsolated(
            () => runner(globals).GetAwaiter().GetResult(),
            ex => s_logger.LogError(ex, "C# script effect threw while describing; rendering identity."));
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
                    _compileError = FormatCompilationErrors(roslynScript, errors);
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
