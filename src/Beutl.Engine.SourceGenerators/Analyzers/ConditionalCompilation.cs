using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>Answers whether a call survives into the program the compiler emits.</summary>
/// <remarks>
/// <see cref="ConditionalAttribute"/> is a binding-time omission and not a preprocessor one, so the call
/// site is written and parsed the same either way and nothing in the syntax says which build keeps it.
/// Every analyzer that reasons about what a body runs has to ask this question, and has to ask it the same
/// way, or the two rules disagree about which program the author is shipping.
/// </remarks>
internal static class ConditionalCompilation
{
    private const string ConditionalAttributeMetadataName = "System.Diagnostics.ConditionalAttribute";

    /// <summary>Whether a call to <paramref name="callee"/> written in <paramref name="tree"/> is emitted.</summary>
    /// <remarks>
    /// The attribute is inherited, so the overridden methods are read too, and one defined symbol among
    /// however many the chain names is enough to keep the call.
    /// </remarks>
    public static bool IsCallCompiled(Compilation compilation, IMethodSymbol callee, SyntaxTree tree)
    {
        bool conditional = false;

        for (IMethodSymbol? current = callee; current is not null; current = current.OverriddenMethod)
        {
            foreach (AttributeData attribute in current.OriginalDefinition.GetAttributes())
            {
                // The name test is what keeps this affordable: it costs a string compare per attribute and
                // leaves the symbol lookup to the attributes that could be this one.
                if (attribute.AttributeClass is not { Name: nameof(ConditionalAttribute) } declared
                    || !SymbolEqualityComparer.Default.Equals(
                        declared,
                        compilation.GetTypeByMetadataName(ConditionalAttributeMetadataName)))
                {
                    continue;
                }

                conditional = true;
                if (attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string defined
                    && tree.Options is CSharpParseOptions options
                    && options.PreprocessorSymbolNames.Contains(defined))
                {
                    return true;
                }
            }
        }

        return !conditional;
    }
}
