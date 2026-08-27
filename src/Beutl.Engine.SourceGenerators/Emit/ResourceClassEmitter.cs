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
        EmitFoldChildVersions(sb, innerIndent, info);
        EmitGetOriginal(sb, innerIndent, currentTypeDisplay);
        EmitBindNodePortValues(sb, innerIndent, info);
        EmitUpdateMethod(sb, innerIndent, currentTypeDisplay, renderContextType, engineObjectType, info);
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
            sb.Append(innerIndent).AppendLine($"public {valueTypeDisplay} {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            EmitVersionBumpingSetter(
                sb,
                innerIndent,
                fieldName,
                $"global::System.Collections.Generic.EqualityComparer<{valueTypeDisplay}>.Default.Equals({fieldName}, value)");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
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
            EmitVersionBumpingSetter(
                sb,
                innerIndent,
                fieldName,
                $"global::System.Object.ReferenceEquals({fieldName}, value)");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent).AppendLine($"public global::System.Collections.Generic.List<{resourceType}> {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine($"    get => {fieldName};");
            EmitVersionBumpingSetter(
                sb,
                innerIndent,
                fieldName,
                $"global::System.Object.ReferenceEquals({fieldName}, value)");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
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

    /// <summary>
    /// Emits a setter that moves <c>Version</c> whenever the stored value actually changes.
    /// </summary>
    /// <remarks>
    /// Version is the only invalidation signal a resource has: the path, stroke, mesh and render-node caches
    /// all key on it. The reconcile path reaches the backing fields through <c>CompareAndUpdate*</c>, so a
    /// resource mutated directly - which is the only way a detached resource ever changes - would otherwise
    /// keep serving what it built before. The guard mirrors <c>CompareAndUpdate</c> so that assigning the
    /// value already stored stays free, and node-port setters are excluded because they write through into
    /// the node graph's shared item value rather than into resource state.
    /// </remarks>
    private static void EmitVersionBumpingSetter(
        StringBuilder sb, string innerIndent, string fieldName, string unchangedCondition)
    {
        sb.Append(innerIndent).AppendLine("    set");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine($"        if ({unchangedCondition}) return;");
        sb.Append(innerIndent).AppendLine($"        {fieldName} = value;");
        sb.Append(innerIndent).AppendLine("        Version++;");
        sb.Append(innerIndent).AppendLine("    }");
    }

    private static bool OwnsAnyResource(ClassInfo info)
    {
        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (!property.ExcludeFromResource) return true;
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (!property.ExcludeFromResource) return true;
        }

        return false;
    }

    /// <summary>
    /// Emits the override that folds the owned resources' versions into this one's effective version.
    /// </summary>
    /// <remarks>
    /// A resource list is handed out as a plain <c>List&lt;T&gt;</c>, so adding, removing, reordering or
    /// replacing an entry never reaches the setter that moves <c>Version</c>, and mutating a child already
    /// stored moves only that child's. Every cache a detached resource can reach keys on
    /// <c>EffectiveVersion</c> instead, which is what this fold feeds. Reconciling covers the same ground
    /// for an attached resource, which is why the fold never runs for one.
    /// </remarks>
    private static void EmitFoldChildVersions(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        if (!OwnsAnyResource(info)) return;

        sb.Append(innerIndent).AppendLine("protected override int FoldChildVersions(int seed)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    seed = base.FoldChildVersions(seed);");

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent)
                .AppendLine($"    seed = unchecked(seed * 31 + ({fieldName}?.EffectiveVersion ?? 0));");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"    if ({fieldName} != null)");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine($"        seed = unchecked(seed * 31 + {fieldName}.Count);");
            sb.Append(innerIndent).AppendLine($"        foreach (var __item in {fieldName})");
            sb.Append(innerIndent).AppendLine("        {");
            // Replacing an entry with a resource that happens to carry the same version is a change too.
            sb.Append(innerIndent)
                .AppendLine("            seed = unchecked(seed * 31 + global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__item));");
            sb.Append(innerIndent)
                .AppendLine("            seed = unchecked(seed * 31 + (__item?.EffectiveVersion ?? 0));");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
        }

        sb.Append(innerIndent).AppendLine("    return seed;");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitGetOriginal(StringBuilder sb, string innerIndent, string currentTypeDisplay)
    {
        sb.Append(innerIndent).AppendLine($"public new {currentTypeDisplay}? GetOriginal()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    return ({currentTypeDisplay}?)base.GetOriginal();");
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
            sb.Append(innerIndent).AppendLine("    var node = GetOriginal()!;");

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
                    if (property.ExcludeFromResource) continue;

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
                    if (property.ExcludeFromResource) continue;

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
                    if (property.ExcludeFromResource) continue;

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
