using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.Scripting;

internal static class SynchronousScriptCompiler
{
    public static List<string> GetErrors(Script<object> script)
    {
        Compilation compilation = script.GetCompilation();
        // Scripts access frame-owned state and their callers wait synchronously. Reject every await
        // form, including those in nested methods/lambdas, before creating an executable delegate.
        bool containsAwait = compilation.SyntaxTrees.Any(tree =>
            tree.GetRoot().DescendantTokens().Any(token => token.IsKind(SyntaxKind.AwaitKeyword)));

        // Compile uses the cached compilation and includes emit diagnostics that GetDiagnostics omits.
        // Keep that full validation for synchronous scripts, but do not emit rejected scripts.
        var diagnostics = containsAwait ? compilation.GetDiagnostics() : script.Compile();
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        if (containsAwait)
            errors.Add("C# scripts in this context are synchronous and do not support 'await', 'await using', or 'await foreach'.");

        return errors;
    }
}
