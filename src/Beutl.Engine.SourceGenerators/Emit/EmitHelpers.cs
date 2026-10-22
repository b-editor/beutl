using System.Text;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class EmitHelpers
{
    // A property exposed as XxxProperty is registered, saved and recorded as Xxx, as CoreObject.ScanPropertiesCore does.
    public static string ToPropertyName(string memberName)
        => memberName.Length > "Property".Length && memberName.EndsWith("Property", StringComparison.Ordinal)
            ? memberName.Substring(0, memberName.Length - "Property".Length)
            : memberName;

    public static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string GetAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }

    public static string GetTypeParameterList(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append('<');
        for (int i = 0; i < symbol.TypeParameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(symbol.TypeParameters[i].Name);
        }

        builder.Append('>');
        return builder.ToString();
    }

    public static string GetTypeConstraintClauses(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (ITypeParameterSymbol typeParameter in symbol.TypeParameters)
        {
            var constraints = new List<string>();
            foreach (ITypeSymbol constraintType in typeParameter.ConstraintTypes)
            {
                constraints.Add(constraintType.ToDisplayString(TypeDisplayFormat));
            }

            if (typeParameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (typeParameter.HasReferenceTypeConstraint)
            {
                constraints.Add("class");
            }

            if (typeParameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            if (typeParameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count == 0)
            {
                continue;
            }

            sb.Append("    where ").Append(typeParameter.Name).Append(" : ");
            for (int i = 0; i < constraints.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(constraints[i]);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToFieldName(string propertyName)
    {
        return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }

    public static string GetResourceTypeName(ITypeSymbol symbol)
    {
        var name = symbol.ToDisplayString(TypeDisplayFormat);
        if (name.EndsWith("?"))
        {
            return name.Substring(0, name.Length - 1) + ".Resource?";
        }
        return name + ".Resource";
    }

    public static string GetHintName(INamedTypeSymbol symbol)
        => BuildHintName(symbol, "_Resource.g.cs");

    internal static string BuildHintName(INamedTypeSymbol symbol, string suffix)
    {
        string name = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var sb = new StringBuilder(name.Length + 32);
        foreach (char c in name)
        {
            sb.Append(c switch
            {
                '<' or '>' or ',' or '.' or ' ' or ':' => '_',
                _ => c,
            });
        }

        sb.Append(suffix);
        return sb.ToString();
    }

    internal static string? GetNamespaceName(INamedTypeSymbol symbol)
        => symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;
}
