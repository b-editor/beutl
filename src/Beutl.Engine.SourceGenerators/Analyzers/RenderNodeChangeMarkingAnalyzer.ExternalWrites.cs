using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class RenderNodeChangeMarkingAnalyzer
{
    private static void ReportExternallyWritableState(
        SymbolAnalysisContext context,
        TypeAnalysis analysis,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType,
        ImmutableHashSet<ISymbol> readState)
    {
        // A body declared on Base<T> binds to the original member while a derived type sees the constructed
        // Base<int> member. Both name one declaration, which is the unit this rule reports.
        var unreportedDeclarations = new HashSet<ISymbol>(
            readState.Select(static state => state.OriginalDefinition),
            SymbolEqualityComparer.Default);

        // A source base whose own Process reads a declaration already reports it when that type is analyzed.
        // Leave that diagnostic there instead of repeating it once for every derived node that reads it.
        foreach (INamedTypeSymbol declaring in EnumerateTypeChain(type.BaseType, renderNodeType))
        {
            if (IsDeclaredInCompilation(context.Compilation, declaring))
            {
                foreach (ISymbol state in analysis.ReadStateOfProcessFor(declaring, renderNodeType))
                    unreportedDeclarations.Remove(state.OriginalDefinition);
            }
        }

        foreach (INamedTypeSymbol declaring in EnumerateTypeChain(type, renderNodeType))
        {
            // Metadata exposes accessibility but not whether a property or event is compiler-backed, so
            // only declarations whose source this compilation owns participate in this rule.
            if (!IsDeclaredInCompilation(context.Compilation, declaring))
                continue;

            foreach (ISymbol member in declaring.GetMembers())
            {
                ISymbol declaration = member.OriginalDefinition;
                if (!unreportedDeclarations.Contains(declaration)
                    || GetExternalWrite(declaration) is not { } write)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnmarkedRenderNodeMutation,
                    write.Location,
                    type.Name,
                    write.Writer,
                    declaration.Name,
                    write.Fix));
            }
        }
    }

    private static bool IsDeclaredInCompilation(Compilation compilation, INamedTypeSymbol type)
        => type.DeclaringSyntaxReferences.Any(
            reference => compilation.ContainsSyntaxTree(reference.SyntaxTree));

    private readonly record struct ExternalWrite(string Writer, string Fix, Location Location);

    private static ExternalWrite? GetExternalWrite(ISymbol member) => member switch
    {
        IPropertySymbol property when IsAutoProperty(property)
            && property.SetMethod is
            {
                IsInitOnly: false,
                DeclaredAccessibility: not Accessibility.Private,
            } setter
            => new ExternalWrite(
                property.Name + ".set",
                MarkFromTheSetter,
                setter.Locations.FirstOrDefault() ?? property.Locations.FirstOrDefault() ?? Location.None),

        IEventSymbol { DeclaredAccessibility: not Accessibility.Private } @event when IsFieldLikeEvent(@event)
            => new ExternalWrite(
                @event.Name + ".add",
                MarkFromTheAccessors,
                @event.Locations.FirstOrDefault() ?? Location.None),

        IFieldSymbol
        {
            IsReadOnly: false,
            AssociatedSymbol: null,
            DeclaredAccessibility: not Accessibility.Private,
        } field
            => new ExternalWrite(
                field.Name,
                NarrowTheField,
                field.Locations.FirstOrDefault() ?? Location.None),

        _ => null,
    };

    private const string MarkFromTheSetter =
        "Give the setter a body that assigns the value and calls MarkChanged(), or make it private or "
        + "init-only so that only this node's own code can assign it";

    private const string MarkFromTheAccessors =
        "Give the event add and remove accessors that subscribe and call MarkChanged(), or make it private "
        + "so that only this node's own code can subscribe to it";

    private const string NarrowTheField =
        "Make the field private or readonly so that code outside this node cannot assign it, or replace it "
        + "with a property whose setter assigns the value and calls MarkChanged()";

    private static bool IsAutoProperty(IPropertySymbol property)
    {
        foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax
                {
                    ExpressionBody: null,
                    AccessorList: { } accessors,
                })
            {
                return false;
            }

            foreach (AccessorDeclarationSyntax accessor in accessors.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                    return false;
            }
        }

        return property.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool IsFieldLikeEvent(IEventSymbol @event)
        => @event.AddMethod is { IsImplicitlyDeclared: true };
}
