using System.Text;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class EmitHelpers
{
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

    public static string GetTypeConstraintClauses(INamedTypeSymbol symbol, string indent)
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

            sb.Append(indent).Append("    where ").Append(typeParameter.Name).Append(" : ");
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
        if (string.IsNullOrEmpty(propertyName))
        {
            return "_value";
        }

        if (propertyName.Length == 1)
        {
            return "_" + propertyName.ToLowerInvariant();
        }

        return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }

    public static string GetResourceTypeName(INamedTypeSymbol symbol)
    {
        var name = symbol.ToDisplayString(TypeDisplayFormat);
        if (name.EndsWith("?"))
        {
            return name.Substring(0, name.Length - 1) + ".Resource?";
        }
        return symbol.ToDisplayString(TypeDisplayFormat) + ".Resource";
    }

    public static string GetHintName(INamedTypeSymbol symbol)
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

        sb.Append("_Resource.g.cs");
        return sb.ToString();
    }
}
