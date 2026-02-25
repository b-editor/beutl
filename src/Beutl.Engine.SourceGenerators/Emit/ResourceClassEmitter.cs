using System.Text;

using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ResourceClassEmitter
{
    public static void Emit(StringBuilder sb, string indent, string currentTypeDisplay, ClassInfo info)
    {
        string renderContextType = "global::Beutl.Graphics.Rendering.RenderContext";
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
        EmitBindSocketValues(sb, innerIndent, info);
        EmitUpdateMethod(sb, innerIndent, currentTypeDisplay, renderContextType, engineObjectType, info);
        EmitDisposeMethod(sb, innerIndent, info);

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitFields(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        foreach (ValuePropertyInfo property in info.ValueProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string valueTypeDisplay = property.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"private {valueTypeDisplay} {fieldName};");
            sb.AppendLine();
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ValueType);
            sb.Append(innerIndent).AppendLine($"private {resourceType} {fieldName};");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent)
                .AppendLine($"private global::System.Collections.Generic.List<{resourceType}> {fieldName} = [];");
            sb.AppendLine();
        }

        foreach (SocketPropertyInfo socket in info.SocketProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(socket.Name) + "_ItemValue";
            string valueTypeDisplay = socket.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"private global::Beutl.NodeTree.Rendering.ItemValue<{valueTypeDisplay}>? {fieldName};");
            sb.AppendLine();
        }
    }

    private static void EmitProperties(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        foreach (ValuePropertyInfo property in info.ValueProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string valueTypeDisplay = property.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"public {valueTypeDisplay} {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            sb.Append(innerIndent).AppendLine($"    set => {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ValueType);
            sb.Append(innerIndent).AppendLine($"public {resourceType} {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            sb.Append(innerIndent).AppendLine($"    set => {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent).AppendLine($"public global::System.Collections.Generic.List<{resourceType}> {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            sb.Append(innerIndent).AppendLine($"    set => {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (SocketPropertyInfo socket in info.SocketProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(socket.Name) + "_ItemValue";
            string valueTypeDisplay = socket.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"public {valueTypeDisplay} {socket.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName}?.Value ?? default!;");
            sb.Append(innerIndent).AppendLine($"    set {{ if ({fieldName} != null) {fieldName}.Value = value; }}");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void EmitGetOriginal(StringBuilder sb, string innerIndent, string currentTypeDisplay)
    {
        sb.Append(innerIndent).AppendLine($"public new {currentTypeDisplay} GetOriginal()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    return ({currentTypeDisplay})base.GetOriginal();");
        sb.Append(innerIndent).AppendLine("}");
    }

    private static void EmitBindSocketValues(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        if (info.SocketProperties.Length > 0)
        {
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine("public override void BindSocketValues()");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    base.BindSocketValues();");
            sb.Append(innerIndent).AppendLine("    var node = GetOriginal();");

            for (int i = 0; i < info.SocketProperties.Length; i++)
            {
                SocketPropertyInfo socket = info.SocketProperties[i];
                string fieldName = EmitHelpers.ToFieldName(socket.Name) + "_ItemValue";
                string valueTypeDisplay = socket.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
                string idxVar = $"__idx{i}";
                sb.Append(innerIndent).AppendLine($"    if (ItemIndexMap.TryGetValue(node.{socket.Name}, out int {idxVar}))");
                sb.Append(innerIndent).AppendLine($"        {fieldName} = (global::Beutl.NodeTree.Rendering.ItemValue<{valueTypeDisplay}>)ItemValues[{idxVar}];");
            }

            sb.Append(innerIndent).AppendLine("}");
        }
    }

    private static void EmitUpdateMethod(StringBuilder sb, string innerIndent, string currentTypeDisplay, string renderContextType, string engineObjectType, ClassInfo info)
    {
        bool hasAdditionalMembers = info.ValueProperties.Length > 0
            || info.ObjectProperties.Length > 0
            || info.ListProperties.Length > 0;

        sb.Append(innerIndent).AppendLine($"partial void PreUpdate({currentTypeDisplay} obj, {renderContextType} context);");
        sb.Append(innerIndent).AppendLine($"partial void PostUpdate({currentTypeDisplay} obj, {renderContextType} context);");
        sb.Append(innerIndent).AppendLine($"public override void Update({engineObjectType} obj, {renderContextType} context, ref bool updateOnly)");
        sb.Append(innerIndent).AppendLine("{");

        sb.Append(innerIndent).AppendLine($"    this.PreUpdate(({currentTypeDisplay})obj, context);");
        sb.Append(innerIndent).AppendLine("    base.Update(obj, context, ref updateOnly);");

        bool wroteSection = false;

        if (hasAdditionalMembers)
        {
            sb.AppendLine();

            if (info.ValueProperties.Length > 0)
            {
                foreach (ValuePropertyInfo property in info.ValueProperties)
                {
                    string fieldName = EmitHelpers.ToFieldName(property.Name);
                    sb.Append(innerIndent).AppendLine($"    CompareAndUpdate(context, (({currentTypeDisplay})obj).{property.Name}, ref {fieldName}, ref updateOnly);");
                }

                wroteSection = true;
            }

            if (info.ListProperties.Length > 0)
            {
                if (wroteSection)
                {
                    sb.AppendLine();
                }

                int listIndex = 0;
                foreach (ListPropertyInfo property in info.ListProperties)
                {
                    if (listIndex > 0)
                    {
                        sb.AppendLine();
                    }

                    listIndex++;
                    string fieldName = EmitHelpers.ToFieldName(property.Name);
                    sb.Append(innerIndent).AppendLine($"    CompareAndUpdateList(context, (({currentTypeDisplay})obj).{property.Name}, ref {fieldName}, ref updateOnly);");
                }

                wroteSection = true;
            }

            if (info.ObjectProperties.Length > 0)
            {
                if (wroteSection)
                {
                    sb.AppendLine();
                }

                int objectIndex = 0;
                foreach (ObjectPropertyInfo property in info.ObjectProperties)
                {
                    if (objectIndex > 0)
                    {
                        sb.AppendLine();
                    }

                    objectIndex++;
                    string fieldName = EmitHelpers.ToFieldName(property.Name);
                    sb.Append(innerIndent).AppendLine($"    CompareAndUpdateObject(context, (({currentTypeDisplay})obj).{property.Name}, ref {fieldName}, ref updateOnly);");
                }
            }
        }

        sb.Append(innerIndent).AppendLine($"    this.PostUpdate(({currentTypeDisplay})obj, context);");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
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
            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        {fieldName}?.Dispose();");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
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
