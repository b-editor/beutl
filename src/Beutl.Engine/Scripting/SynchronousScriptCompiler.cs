using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.Scripting;

internal static class SynchronousScriptCompiler
{
    public static List<string> GetErrors(Script<object> script)
    {
        var errors = script.Compile()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        // Scripts access frame-owned state and their callers wait synchronously. Reject every await
        // form, including those in nested methods/lambdas, before creating an executable delegate.
        foreach (SyntaxTree tree in script.GetCompilation().SyntaxTrees)
        {
            if (tree.GetRoot().DescendantTokens().Any(token => token.IsKind(SyntaxKind.AwaitKeyword)))
            {
                errors.Add("C# rendering scripts are synchronous and do not support 'await', 'await using', or 'await foreach'.");
            }
        }

        return errors;
    }
}
