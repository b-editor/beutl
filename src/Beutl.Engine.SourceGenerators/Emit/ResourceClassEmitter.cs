using System.Text;

using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ResourceClassEmitter
{
    public static void Emit(StringBuilder sb, string indent, string currentTypeDisplay, ClassInfo info)
    {
        if (info.SuppressedResourceGeneration) return;

        string renderContextType = "global::Beutl.Composition.CompositionContext";
        string engineObjectType = "global::Beutl.Engine.EngineObject";

        if (info.Symbol.IsAbstract)
        {
            sb.Append(indent).Append("public new abstract partial class Resource");
        }
        else
        {
            sb.Append(indent).Append("public new partial class Resource");
        }

        if (info.BaseResourceOwner is INamedTypeSymbol baseOwner)
        {
            sb.Append($" : {baseOwner.ToDisplayString(EmitHelpers.TypeDisplayFormat)}.Resource");
        }
        else
        {
            sb.Append($" : {engineObjectType}.Resource");
        }

        sb.AppendLine();
        sb.Append(indent).AppendLine("{");

        string innerIndent = indent + "    ";

        EmitFields(sb, innerIndent, info);
        EmitProperties(sb, innerIndent, info);
        EmitGetOriginal(sb, innerIndent, currentTypeDisplay);
        EmitBindNodePortValues(sb, innerIndent, info);
        EmitReconcileMethod(sb, innerIndent, currentTypeDisplay, renderContextType, engineObjectType, info);
        EmitDisposeMethod(sb, innerIndent, info);

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitFields(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        foreach (ValuePropertyInfo property in info.ValueProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string valueTypeDisplay = property.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"private {valueTypeDisplay} {fieldName} = default!;");
            sb.AppendLine();
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ValueType);
            string fieldType = resourceType.EndsWith("?", StringComparison.Ordinal)
                ? resourceType
                : resourceType + "?";
            sb.Append(innerIndent).AppendLine($"private {fieldType} {fieldName};");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent)
                .AppendLine($"private global::System.Collections.Generic.List<{resourceType}> {fieldName} = [];");
            sb.AppendLine();
        }

        foreach (NodePortPropertyInfo port in info.NodePortProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
            string valueTypeDisplay = port.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"private global::Beutl.NodeGraph.Composition.ItemValue<{valueTypeDisplay}>? {fieldName};");
            sb.AppendLine();
        }
    }

    private static void EmitProperties(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        foreach (ValuePropertyInfo property in info.ValueProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string valueTypeDisplay = property.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            AppendFieldBackedProperty(sb, innerIndent, valueTypeDisplay, property.Name, fieldName);
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ValueType);
            bool isNullable = property.ValueType.NullableAnnotation == NullableAnnotation.Annotated;
            sb.Append(innerIndent).AppendLine($"public {resourceType} {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            if (isNullable)
            {
                sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            }
            else
            {
                sb.Append(innerIndent)
                    .Append("    get => ")
                    .Append(fieldName)
                    .Append(" ?? throw new global::System.InvalidOperationException(\"")
                    .Append(property.Name)
                    .AppendLine(" did not contain an owned resource.\");");
            }
            sb.Append(innerIndent).AppendLine($"    set => {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            AppendFieldBackedProperty(
                sb, innerIndent, $"global::System.Collections.Generic.List<{resourceType}>", property.Name, fieldName);
        }

        foreach (NodePortPropertyInfo port in info.NodePortProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
            string valueTypeDisplay = port.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"public {valueTypeDisplay} {port.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName}?.Value ?? default!;");
            sb.Append(innerIndent).AppendLine($"    set {{ if ({fieldName} != null) {fieldName}.Value = value; }}");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void AppendFieldBackedProperty(
        StringBuilder sb, string innerIndent, string type, string name, string field)
    {
        sb.Append(innerIndent).AppendLine($"public {type} {name}");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    get => {field};");
        sb.Append(innerIndent).AppendLine($"    set => {field} = value;");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitGetOriginal(StringBuilder sb, string innerIndent, string currentTypeDisplay)
    {
        sb.Append(innerIndent).AppendLine($"public new {currentTypeDisplay}? GetOriginal()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    return ({currentTypeDisplay}?)base.GetOriginal();");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine($"public new {currentTypeDisplay} RequireOriginal()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    return ({currentTypeDisplay})base.RequireOriginal();");
        sb.Append(innerIndent).AppendLine("}");
    }

    private static void EmitBindNodePortValues(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        if (info.NodePortProperties.Length > 0)
        {
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine("public override void BindNodePortValues()");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    base.BindNodePortValues();");
            sb.Append(innerIndent).AppendLine("    var node = RequireOriginal();");

            for (int i = 0; i < info.NodePortProperties.Length; i++)
            {
                NodePortPropertyInfo port = info.NodePortProperties[i];
                string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
                string valueTypeDisplay = port.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
                string idxVar = $"__idx{i}";
                sb.Append(innerIndent).AppendLine($"    if (ItemIndexMap.TryGetValue(node.{port.Name}, out int {idxVar}))");
                sb.Append(innerIndent).AppendLine($"        {fieldName} = (global::Beutl.NodeGraph.Composition.ItemValue<{valueTypeDisplay}>)ItemValues[{idxVar}];");
            }

            sb.Append(innerIndent).AppendLine("}");
        }
    }

    private static void EmitReconcileMethod(StringBuilder sb, string innerIndent, string currentTypeDisplay, string renderContextType, string engineObjectType, ClassInfo info)
    {
        bool hasAdditionalMembers = info.ValueProperties.Length > 0
            || info.ObjectProperties.Length > 0
            || info.ListProperties.Length > 0;

        sb.Append(innerIndent).AppendLine($"partial void PreReconcile({currentTypeDisplay} obj, {renderContextType} context);");
        sb.Append(innerIndent).AppendLine($"partial void PostReconcile({currentTypeDisplay} obj, {renderContextType} context);");
        sb.Append(innerIndent).AppendLine($"public override void Reconcile({engineObjectType} obj, {renderContextType} context, ref bool versionBumped)");
        sb.Append(innerIndent).AppendLine("{");

        sb.Append(innerIndent).AppendLine($"    this.PreReconcile(({currentTypeDisplay})obj, context);");
        sb.Append(innerIndent).AppendLine("    base.Reconcile(obj, context, ref versionBumped);");

        bool wroteSection = false;

        if (hasAdditionalMembers)
        {
            sb.AppendLine();

            if (info.ValueProperties.Length > 0)
            {
                foreach (ValuePropertyInfo property in info.ValueProperties)
                {
                    if (property.ExcludeFromResource) continue;

                    AppendReconcileCall(sb, innerIndent, "ReconcileValue", currentTypeDisplay, property.Name);
                }

                wroteSection = true;
            }

            if (info.ListProperties.Length > 0)
            {
                if (wroteSection)
                {
                    sb.AppendLine();
                }

                AppendSeparatedReconciles(
                    sb,
                    innerIndent,
                    "ReconcileChildren",
                    currentTypeDisplay,
                    info.ListProperties.Where(property => !property.ExcludeFromResource).Select(property => property.Name));

                wroteSection = true;
            }

            if (info.ObjectProperties.Length > 0)
            {
                if (wroteSection)
                {
                    sb.AppendLine();
                }

                AppendSeparatedReconciles(
                    sb,
                    innerIndent,
                    "ReconcileChild",
                    currentTypeDisplay,
                    info.ObjectProperties.Where(property => !property.ExcludeFromResource).Select(property => property.Name));
            }
        }

        sb.Append(innerIndent).AppendLine($"    this.PostReconcile(({currentTypeDisplay})obj, context);");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendSeparatedReconciles(
        StringBuilder sb, string innerIndent, string method, string currentTypeDisplay, IEnumerable<string> propertyNames)
    {
        bool first = true;
        foreach (string propertyName in propertyNames)
        {
            if (!first)
            {
                sb.AppendLine();
            }

            first = false;
            AppendReconcileCall(sb, innerIndent, method, currentTypeDisplay, propertyName);
        }
    }

    private static void AppendReconcileCall(
        StringBuilder sb, string innerIndent, string method, string currentTypeDisplay, string propertyName)
    {
        string fieldName = EmitHelpers.ToFieldName(propertyName);
        sb.Append(innerIndent).AppendLine($"    global::Beutl.Engine.ResourceReconciler.{method}(this, context, (({currentTypeDisplay})obj).{propertyName}, ref {fieldName}, ref versionBumped);");
    }

    private static void EmitDisposeMethod(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        sb.Append(innerIndent).AppendLine($"partial void PreDispose(bool disposing);");
        sb.Append(innerIndent).AppendLine($"partial void PostDispose(bool disposing);");
        sb.Append(innerIndent).AppendLine("protected override void Dispose(bool disposing)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    this.PreDispose(disposing);");
        sb.Append(innerIndent).AppendLine("    if (disposing)");
        sb.Append(innerIndent).AppendLine("    {");
        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        {fieldName}?.Dispose();");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        if ({fieldName} != null)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine($"            foreach (var item in {fieldName})");
            sb.Append(innerIndent).AppendLine("            {");
            sb.Append(innerIndent).AppendLine("                item?.Dispose();");
            sb.Append(innerIndent).AppendLine("            }");
            sb.Append(innerIndent).AppendLine("            ");
            sb.Append(innerIndent).AppendLine("        }");
        }
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    this.PostDispose(disposing);");
        sb.Append(innerIndent).AppendLine("    base.Dispose(disposing);");
        sb.Append(innerIndent).AppendLine("}");
    }
}
