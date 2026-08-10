using System.Text;

using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ResourceDefaultValuesEmitter
{
    public static void Emit(
        StringBuilder sb,
        string indent,
        string currentTypeDisplay,
        ClassInfo info,
        string? defaultsProviderMethod)
    {
        if (info.SuppressedResourceGeneration) return;

        if (defaultsProviderMethod is not null)
        {
            EmitProviderFactory(sb, indent, currentTypeDisplay, defaultsProviderMethod);
            return;
        }

        string constructorAccessibility = info.Symbol.IsSealed ? "private" : "protected";
        string constructionType =
            "global::Beutl.Engine.EngineObject.ResourceDefaultValuesConstruction";

        IMethodSymbol? implicitParameterlessConstructor = info.Symbol.InstanceConstructors
            .FirstOrDefault(static constructor =>
                constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 0);
        if (implicitParameterlessConstructor is not null)
        {
            sb.Append(indent)
                .Append(EmitHelpers.GetAccessibility(implicitParameterlessConstructor.DeclaredAccessibility))
                .Append(' ')
                .Append(info.Symbol.Name)
                .AppendLine("()");
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).AppendLine("}");
            sb.AppendLine();
        }

        sb.Append(indent)
            .AppendLine("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        sb.Append(indent)
            .Append(constructorAccessibility)
            .Append(' ')
            .Append(info.Symbol.Name)
            .Append('(')
            .Append(constructionType)
            .AppendLine(" construction)");
        sb.Append(indent).AppendLine("    : base(construction)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("}");

        if (!info.Symbol.IsAbstract)
        {
            sb.AppendLine();
            sb.Append(indent)
                .Append("private static ")
                .Append(currentTypeDisplay)
                .AppendLine(" __CreateResourceDefaultValues()");
            sb.Append(indent)
                .Append("    => new(default(")
                .Append(constructionType)
                .AppendLine("));");
        }
    }

    private static void EmitProviderFactory(
        StringBuilder sb,
        string indent,
        string currentTypeDisplay,
        string defaultsProviderMethod)
    {
        string escapedMethodName = SyntaxFacts.GetKeywordKind(defaultsProviderMethod) == SyntaxKind.None
            ? defaultsProviderMethod
            : "@" + defaultsProviderMethod;
        sb.Append(indent)
            .Append("private static ")
            .Append(currentTypeDisplay)
            .AppendLine(" __CreateResourceDefaultValues()");
        sb.Append(indent)
            .Append("    => ")
            .Append(currentTypeDisplay)
            .Append('.')
            .Append(escapedMethodName)
            .AppendLine("();");
    }
}
