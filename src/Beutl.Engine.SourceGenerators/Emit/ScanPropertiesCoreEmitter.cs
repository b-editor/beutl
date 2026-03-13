using System.Text;

using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ScanPropertiesCoreEmitter
{
    // ジェネリッククラスは型パラメーターの競合するので、ScanPropertiesCore<T>()のTを生成する
    private static string GenerateGenericTypeParameter(ClassInfo info)
    {
        if (info.Symbol.TypeParameters.Length == 0)
            return "T";

        int num = 1;
        string baseName = "T";
        string name = baseName;
        while (info.Symbol.TypeParameters.Any(tp => tp.Name == name))
        {
            name = $"{baseName}{num}";
            num++;
        }
        return name;
    }

    public static void Emit(StringBuilder sb, string indent, ClassInfo info)
    {
        var genericTypeParam = GenerateGenericTypeParameter(info);

        string currentTypeDisplay = info.Symbol.ToDisplayString(EmitHelpers.TypeDisplayFormat);
        bool hasProperties = info.OrderedProperties.Length > 0;

        // 静的フィールドの emit
        EmitFields(sb, indent, info);

        // ScanPropertiesCore<T>() メソッドの emit
        sb.Append(indent).AppendLine($"protected override global::System.Collections.Generic.IEnumerable<global::Beutl.Engine.IProperty> ScanPropertiesCore<{genericTypeParam}>()");
        sb.Append(indent).AppendLine("{");

        string innerIndent = indent + "    ";

        sb.Append(innerIndent).AppendLine($"if (typeof({genericTypeParam}) == typeof({currentTypeDisplay}))");
        sb.Append(innerIndent).AppendLine("{");

        string bodyIndent = innerIndent + "    ";

        if (hasProperties)
        {
            foreach (object prop in info.OrderedProperties)
            {
                switch (prop)
                {
                    case ValuePropertyInfo vp:
                        EmitPropertyScan(sb, bodyIndent, vp.Name);
                        break;
                    case ObjectPropertyInfo op:
                        EmitPropertyScan(sb, bodyIndent, op.Name);
                        break;
                    case ListPropertyInfo lp:
                        EmitPropertyScan(sb, bodyIndent, lp.Name);
                        break;
                }
            }
        }

        sb.Append(innerIndent).AppendLine("}");
        sb.Append(innerIndent).AppendLine("else");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(bodyIndent).AppendLine($"foreach (global::Beutl.Engine.IProperty __prop in base.ScanPropertiesCore<{genericTypeParam}>())");
        sb.Append(bodyIndent).AppendLine("    yield return __prop;");
        sb.Append(innerIndent).AppendLine("}");

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitFields(StringBuilder sb, string indent, ClassInfo info)
    {
        foreach (ValuePropertyInfo prop in info.ValueProperties)
        {
            EmitAttributeField(sb, indent, prop.Name, prop.Attributes);
            EmitValidatorField(sb, indent, prop.Name);
        }

        foreach (ObjectPropertyInfo prop in info.ObjectProperties)
        {
            EmitAttributeField(sb, indent, prop.Name, prop.Attributes);
            EmitValidatorField(sb, indent, prop.Name);
        }

        foreach (ListPropertyInfo prop in info.ListProperties)
        {
            EmitAttributeField(sb, indent, prop.Name, prop.Attributes);
            EmitValidatorField(sb, indent, prop.Name);
        }
    }

    private static void EmitAttributeField(StringBuilder sb, string indent, string propName, IReadOnlyList<AttributeData> attributes)
    {
        var emittedAttrs = new List<string>();
        foreach (AttributeData attr in attributes)
        {
            string? code = TryEmitAttributeInstance(attr);
            if (code != null)
            {
                emittedAttrs.Add(code);
            }
        }

        sb.Append(indent).Append($"private static readonly global::System.Attribute[] __attrs_{propName} = [");
        if (emittedAttrs.Count > 0)
        {
            sb.AppendLine();
            foreach (string attrCode in emittedAttrs)
            {
                sb.Append(indent).AppendLine($"    {attrCode},");
            }
            sb.Append(indent);
        }

        sb.AppendLine("];");
        sb.AppendLine();
    }

    private static void EmitValidatorField(StringBuilder sb, string indent, string propName)
    {
        sb.Append(indent).AppendLine($"private static global::Beutl.Validation.IValidator? __validator_{propName};");
        sb.AppendLine();
    }

    private static void EmitPropertyScan(StringBuilder sb, string indent, string propName)
    {
        sb.Append(indent).AppendLine($"__validator_{propName} ??= {propName}.CreateValidator(__attrs_{propName});");
        sb.Append(indent).AppendLine($"{propName}.SetValidator(__validator_{propName});");
        sb.Append(indent).AppendLine($"{propName}.SetAttributes(\"{propName}\", __attrs_{propName});");
        sb.Append(indent).AppendLine($"{propName}.SetOwnerObject(this);");
        sb.Append(indent).AppendLine($"yield return {propName};");
        sb.AppendLine();
    }

    private static string? TryEmitAttributeInstance(AttributeData attr)
    {
        if (attr.AttributeClass == null) return null;

        string typeName = attr.AttributeClass.ToDisplayString(EmitHelpers.TypeDisplayFormat);

        var ctorArgs = new List<string>();
        foreach (TypedConstant arg in attr.ConstructorArguments)
        {
            string? val = TryEmitTypedConstant(arg);
            if (val == null) return null; // emit できない引数があればスキップ
            ctorArgs.Add(val);
        }

        var namedArgs = new List<string>();
        foreach (KeyValuePair<string, TypedConstant> kvp in attr.NamedArguments)
        {
            string? val = TryEmitTypedConstant(kvp.Value);
            if (val == null) return null;
            namedArgs.Add($"{kvp.Key} = {val}");
        }

        var sb = new StringBuilder();
        sb.Append($"new {typeName}");

        if (ctorArgs.Count > 0)
        {
            sb.Append('(');
            sb.Append(string.Join(", ", ctorArgs));
            sb.Append(')');
        }
        else if (namedArgs.Count == 0)
        {
            sb.Append("()");
        }

        if (namedArgs.Count > 0)
        {
            sb.Append(" { ");
            sb.Append(string.Join(", ", namedArgs));
            sb.Append(" }");
        }

        return sb.ToString();
    }

    private static string? TryEmitTypedConstant(TypedConstant constant)
    {
        if (constant.IsNull) return "null";

        return constant.Kind switch
        {
            TypedConstantKind.Primitive => TryEmitPrimitive(constant.Value),
            TypedConstantKind.Enum => constant.Type != null
                ? $"({constant.Type.ToDisplayString(EmitHelpers.TypeDisplayFormat)}){constant.Value}"
                : null,
            TypedConstantKind.Type => constant.Value is ITypeSymbol typeSymbol
                ? $"typeof({typeSymbol.ToDisplayString(EmitHelpers.TypeDisplayFormat)})"
                : null,
            TypedConstantKind.Array => TryEmitArray(constant),
            _ => null
        };
    }

    private static string? TryEmitPrimitive(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{EscapeString(s)}\"",
            bool b => b ? "true" : "false",
            char c => $"'{EscapeChar(c)}'",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            double d when double.IsNaN(d) => "double.NaN",
            double d => $"{d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}D",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            float f when float.IsNaN(f) => "float.NaN",
            float f => $"{f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}F",
            long l => $"{l}L",
            ulong ul => $"{ul}UL",
            uint ui => $"{ui}U",
            int i => $"{i}",
            short s => $"(short){s}",
            ushort us => $"(ushort){us}",
            byte b => $"(byte){b}",
            sbyte sb2 => $"(sbyte){sb2}",
            decimal dec => $"{dec.ToString(System.Globalization.CultureInfo.InvariantCulture)}M",
            _ => value.ToString()
        };
    }

    private static string? TryEmitArray(TypedConstant constant)
    {
        if (constant.Type == null) return null;

        var elements = new List<string>();
        foreach (TypedConstant element in constant.Values)
        {
            string? val = TryEmitTypedConstant(element);
            if (val == null) return null;
            elements.Add(val);
        }

        string typeName = constant.Type.ToDisplayString(EmitHelpers.TypeDisplayFormat);
        return $"new {typeName} {{ {string.Join(", ", elements)} }}";
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    private static string EscapeChar(char c)
    {
        return c switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => c.ToString()
        };
    }
}
