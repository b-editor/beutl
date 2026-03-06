using System.Collections.Immutable;

using Beutl.Engine.SourceGenerators.Analysis;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Beutl.Engine.SourceGenerators.Emit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators;

[Generator]
public sealed class FallbackTypeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntaxContext, cancellationToken) => FallbackClassAnalyzer.TryExtract(syntaxContext, cancellationToken))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!.Value);

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (productionContext, pair) =>
            Execute(productionContext, pair.Right));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<FallbackClassInfo> classes)
    {
        if (classes.IsDefaultOrEmpty)
        {
            return;
        }

        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (FallbackClassInfo info in classes)
        {
            if (!processed.Add(info.Symbol))
            {
                continue;
            }

            if (!info.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.FallbackMissingPartial,
                    info.Symbol.Locations.FirstOrDefault(),
                    info.Symbol.ToDisplayString()));
                continue;
            }

            string source = FallbackClassEmitter.Emit(info);
            string hintName = GetHintName(info.Symbol);
            context.AddSource(hintName, source);
        }
    }

    private static string GetHintName(INamedTypeSymbol symbol)
    {
        string name = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var sb = new System.Text.StringBuilder(name.Length + 32);
        foreach (char c in name)
        {
            sb.Append(c switch
            {
                '<' or '>' or ',' or '.' or ' ' or ':' => '_',
                _ => c,
            });
        }

        sb.Append("_Fallback.g.cs");
        return sb.ToString();
    }
}
