using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static (string Kind, string Reason)? DescribeUnprovenStaticState(
        SyntaxNodeAnalysisContext context,
        ISymbol? symbol)
    {
        return symbol switch
        {
            // A const can only ever hold a primitive, a string, an enum member, or null, and the value is
            // burned into every read of it.
            IFieldSymbol { IsStatic: true, IsConst: true } => null,

            IFieldSymbol { IsStatic: true, IsReadOnly: false } =>
                ("static field", "a static field that is neither const nor readonly can be assigned "
                    + "between two recordings while the plan key stays the same"),

            IFieldSymbol { IsStatic: true } field when !IsImmutableType(field.Type) =>
                ("static field", DescribeUnprovableFieldType(field.Type)),

            IPropertySymbol { IsStatic: true } property => DescribeUnprovenGetter(context, property),

            // An event whose accessors this rule can read is routed to those accessors before this is
            // asked, so only these two reach here. Metadata says nothing about which of the two an event
            // declared elsewhere is, and reading its unread accessors as storing nothing would clear every
            // static event in every referenced assembly.
            IEventSymbol { IsStatic: true, DeclaringSyntaxReferences.IsEmpty: true } =>
                ("static event", "its accessors have no source in this compilation, so what a subscription "
                    + "does with the handler cannot be seen, and an event that stores it holds a subscriber "
                    + "list any += or -= anywhere rewrites"),

            // A field-like event is the delegate field its subscriber list lives in, and += and -= are that
            // field's assignments. Reading the list back is legal only inside the declaring type - where a
            // callback written beside the event, and any helper the walk follows into it, both sit - while
            // writing it binds from anywhere, so the one case covers both spellings.
            IEventSymbol { IsStatic: true } =>
                ("static event", "a field-like static event is the delegate field its subscriber list lives "
                    + "in, and any += or -= anywhere rewrites that list, so what the callback reads off it, "
                    + "or does to it, can differ between two recordings while the plan key stays the same"),

            _ => null,
        };
    }

    private static string DescribeUnprovableFieldType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true } named
            && !HasCompleteFieldList(named)
            && HasOnlyImmutableFields(named, MaxImmutableFieldDepth))
        {
            return "readonly stops the field being assigned and not the value it holds being mutated, and "
                + "this field's type is a class declared outside this compilation, which imports its public "
                + "and protected members and not the private state behind them, so the fields visible here "
                + "are a floor and not the type, and nothing shows whether its instances hold something "
                + "writable";
        }

        return "readonly stops the field being assigned and not the value it holds being mutated, and this "
            + "field's type is not one whose instances this rule can prove carry no writable state, so what "
            + "the callback reads through it can change between two recordings while the plan key stays the "
            + "same";
    }

    private static (string Kind, string Reason)? DescribeUnprovenGetter(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        if (property.SetMethod is not null)
            return ("static property", "its setter can change what it answers");

        ImmutableArray<SyntaxReference> declarations = property.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
        {
            return ("static property", "its getter has no source in this compilation, so what the getter "
                + "reads cannot be seen, and having no setter is not on its own evidence that it answers "
                + "the same way twice");
        }

        foreach (SyntaxReference declaration in declarations)
        {
            if (declaration.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax syntax
                && GetInvariantCandidate(syntax) is { } value
                && IsProvenConstant(context, value))
            {
                return null;
            }
        }

        return ("static property", "its getter is not a shape this rule can prove yields the same value "
            + "on every read, and having no setter is not on its own evidence that it does");
    }

    private static ExpressionSyntax? GetInvariantCandidate(PropertyDeclarationSyntax syntax)
    {
        if (syntax.ExpressionBody is { } propertyBody)
            return propertyBody.Expression;

        AccessorDeclarationSyntax? getter = syntax.AccessorList?.Accessors
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter is null)
            return null;

        if (getter.ExpressionBody is { } getterBody)
            return getterBody.Expression;

        if (getter.Body is { Statements.Count: 1 } block
            && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
        {
            return returned;
        }

        // An auto-implemented get-only getter reads a backing field only the initialiser and the static
        // constructor can write, so the initialiser is the whole of what the getter can be shown to answer.
        return getter.Body is null ? syntax.Initializer?.Value : null;
    }

    private static bool IsProvenConstant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        SemanticModel model = GetSemanticModel(context, expression.SyntaxTree);

        // Covers a literal, a const, an enum member, and any expression the compiler folds out of them.
        if (model.GetConstantValue(expression, context.CancellationToken).HasValue)
            return true;

        ExpressionSyntax value = Unwrap(expression);

        // default on a struct is not a constant value to the compiler and is still the same value each read.
        if (value is DefaultExpressionSyntax
            || (value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.DefaultLiteralExpression)))
        {
            return true;
        }

        return model.GetSymbolInfo(value, context.CancellationToken).Symbol
                is IFieldSymbol { IsStatic: true, IsReadOnly: true } field
            && IsImmutableType(field.Type);
    }

    private static bool IsImmutableType(ITypeSymbol type) => IsImmutableType(type, MaxImmutableFieldDepth);

    private const int MaxImmutableFieldDepth = 8;

    private static bool IsImmutableType(ITypeSymbol type, int depth) => type switch
    {
        { TypeKind: TypeKind.Enum } => true,

        // The framework types with no mutable state at all, decided before the walk below because the walk
        // has nothing to tell about them: every primitive carries an instance of itself, string's own
        // fields are not readonly, and IntPtr carries a pointer at memory the type says nothing about.
        {
            SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte
                or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Decimal or SpecialType.System_String or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr or SpecialType.System_DateTime
        } => true,

        // The engine's own resource address, which the BESG004 message tells authors to move a
        // per-recording value onto. The slot is an identity and nothing else, and outside Beutl.Engine it is
        // a metadata class the walk below is not allowed to read. Leaving it to the walk would have the rule
        // rejecting the fix it recommends, which is the state authors suppress a rule over.
        INamedTypeSymbol { Name: "RenderResourceSlot", IsSealed: true } slot
            when slot.ContainingNamespace.ToDisplayString() == "Beutl.Graphics.Rendering" => true,

        // A readonly struct has no instance member that can write it and a sealed class has no derived
        // instance that can add one, so in both the declared fields are the whole of what an instance
        // carries - which is what makes the walk mean anything, once the fields can be read at all.
        // Holding a reference is not itself an answer, so the fields are put the same question their type
        // just was.
        INamedTypeSymbol named when named is { IsValueType: true, IsReadOnly: true }
                or { TypeKind: TypeKind.Class, IsSealed: true } =>
            depth > 0 && HasCompleteFieldList(named) && HasOnlyImmutableFields(named, depth),

        // Anything else is reported. A struct without the modifier can be written through an instance
        // method; an unsealed class is a base a subclass can add state to; a delegate carries a target this
        // rule can no more read than the static method it already says it cannot follow. A pointer, a type
        // parameter, an array, and a type that failed to bind land here too, rather than in the walk, where
        // having no fields to read would pass for having no state.
        _ => false,
    };

    private static bool HasCompleteFieldList(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (!IsFieldListReadable(current))
                return false;
        }

        return true;
    }

    private static bool IsFieldListReadable(INamedTypeSymbol type)
        => type.IsValueType
            || type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType
                or SpecialType.System_Enum
            || !type.DeclaringSyntaxReferences.IsEmpty;

    private static bool HasOnlyImmutableFields(INamedTypeSymbol type, int depth)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                // A field-like event is a delegate field that is not readonly, written by the compiler
                // and left out of a source type's member list, which carries the event and its
                // accessors in its place. Asking about fields alone therefore let a type whose whole
                // mutable state is an event pass for one carrying none, while the same state spelled
                // as a plain delegate field failed. An event declared with its own accessors stores
                // nothing of itself, and whatever those accessors do write is a field this loop sees.
                if (member is IEventSymbol { IsStatic: false, AddMethod.IsImplicitlyDeclared: true })
                    return false;

                // An auto-property's backing field is implicitly declared and is still part of the value,
                // so every instance field counts regardless of how it was written.
                if (member is not IFieldSymbol { IsStatic: false } field)
                    continue;

                if (!field.IsReadOnly || !IsImmutableType(field.Type, depth - 1))
                    return false;
            }
        }

        return true;
    }
}
