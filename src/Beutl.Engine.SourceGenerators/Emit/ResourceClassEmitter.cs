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
        EmitLifecycleMethods(sb, innerIndent, info);
        EmitProperties(sb, innerIndent, info);
        EmitGetOriginal(sb, innerIndent, currentTypeDisplay);
        EmitBindNodePortValues(sb, innerIndent, info);
        EmitUpdateMethod(sb, innerIndent, currentTypeDisplay, renderContextType, engineObjectType, info);
        EmitDisposeMethod(sb, innerIndent, info);

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitFields(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        sb.Append(innerIndent).AppendLine("private readonly object __resourceLifecycleGate = new();");
        sb.Append(innerIndent).AppendLine("private bool __resourceOperationInProgress;");
        sb.Append(innerIndent).AppendLine("private bool __resourceCleanupRequested;");
        sb.Append(innerIndent).AppendLine("private bool __resourceCustomCleanupPreparationStarted;");
        sb.Append(innerIndent).AppendLine("private bool __resourceCleanupCompleted;");
        sb.AppendLine();

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
            sb.Append(innerIndent).AppendLine($"private {resourceType} {fieldName} = default!;");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent)
                .AppendLine($"private global::System.Collections.Generic.List<{resourceType}> {fieldName} = [];");
            sb.Append(innerIndent)
                .AppendLine($"private global::System.Collections.Generic.IReadOnlyList<{resourceType}> {fieldName}Snapshot = global::System.Array.Empty<{resourceType}>();");
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

    private static void EmitLifecycleMethods(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        sb.Append(innerIndent).AppendLine("private global::Beutl.Engine.EngineObject.Resource.GeneratedResourceOperationLease __BeginResourceOperation(global::Beutl.Engine.EngineObject? original = null, bool exclusive = false)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__resourceCleanupRequested || __resourceCleanupCompleted || IsDisposed, this);");
        sb.Append(innerIndent).AppendLine("        if (__resourceOperationInProgress)");
        sb.Append(innerIndent).AppendLine("            throw new global::System.InvalidOperationException(\"A resource update or node-port bind operation cannot run concurrently or re-enter the same resource.\");");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("        __resourceOperationInProgress = true;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        return exclusive");
        sb.Append(innerIndent).AppendLine("            ? base.BeginExclusiveResourceOperation(original)");
        sb.Append(innerIndent).AppendLine("            : base.BeginGeneratedResourceOperation(original);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    catch");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("        {");
        sb.Append(innerIndent).AppendLine("            __resourceOperationInProgress = false;");
        sb.Append(innerIndent).AppendLine("        }");
        sb.Append(innerIndent).AppendLine("        throw;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();

        sb.Append(innerIndent).AppendLine("private void __EndResourceOperation(ref global::Beutl.Engine.EngineObject.Resource.GeneratedResourceOperationLease operation)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        operation.Dispose();");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    finally");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("        {");
        sb.Append(innerIndent).AppendLine("            __resourceOperationInProgress = false;");
        sb.Append(innerIndent).AppendLine("        }");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();

        sb.Append(innerIndent).AppendLine("private bool __IsResourceOperationInvalid()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        return __resourceCleanupRequested || __resourceCleanupCompleted || IsDisposed;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent).AppendLine($"private void __Refresh{property.Name}Snapshot()");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine($"        bool changed = {fieldName}Snapshot.Count != {fieldName}.Count;");
            sb.Append(innerIndent).AppendLine("        if (!changed)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine($"            for (int i = 0; i < {fieldName}.Count; i++)");
            sb.Append(innerIndent).AppendLine("            {");
            sb.Append(innerIndent).AppendLine($"                if (!global::System.Object.ReferenceEquals({fieldName}Snapshot[i], {fieldName}[i]))");
            sb.Append(innerIndent).AppendLine("                {");
            sb.Append(innerIndent).AppendLine("                    changed = true;");
            sb.Append(innerIndent).AppendLine("                    break;");
            sb.Append(innerIndent).AppendLine("                }");
            sb.Append(innerIndent).AppendLine("            }");
            sb.Append(innerIndent).AppendLine("        }");
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine("        if (changed)");
            sb.Append(innerIndent).AppendLine($"            {fieldName}Snapshot = global::System.Array.AsReadOnly({fieldName}.ToArray());");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("}");
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
            sb.Append(innerIndent).AppendLine("    get");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            return {fieldName};");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("    set");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ValueType);
            sb.Append(innerIndent).AppendLine($"public {resourceType} {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    get");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            return {fieldName};");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("    set");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        var operation = __BeginResourceOperation(exclusive: true);");
            sb.Append(innerIndent).AppendLine("        try");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("            {");
            sb.Append(innerIndent).AppendLine("                global::System.ObjectDisposedException.ThrowIf(__resourceCleanupRequested || __resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"                if ({fieldName} != null && !global::System.Object.ReferenceEquals({fieldName}, value))");
            sb.Append(innerIndent).AppendLine("                    throw new global::System.InvalidOperationException(\"An owned generated resource property can only be initialized once. Update the source EngineObject to replace its resource safely.\");");
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine($"                {fieldName} = value;");
            sb.Append(innerIndent).AppendLine("            }");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("        finally");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            __EndResourceOperation(ref operation);");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent).AppendLine($"public global::System.Collections.Generic.IReadOnlyList<{resourceType}> {property.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    get");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            return {fieldName}Snapshot;");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("}");
            sb.AppendLine();
        }

        foreach (NodePortPropertyInfo port in info.NodePortProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
            string valueTypeDisplay = port.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
            sb.Append(innerIndent).AppendLine($"public {valueTypeDisplay} {port.Name}");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    get");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            return {fieldName}?.Value ?? default!;");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("    set");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            base.ValidateGeneratedResourceAccess();");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);");
            sb.Append(innerIndent).AppendLine($"            if ({fieldName} != null) {fieldName}.Value = value;");
            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
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

    private static void EmitBindNodePortValues(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        if (info.NodePortProperties.Length > 0)
        {
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine("protected override void BindNodePortValuesCore()");
            sb.Append(innerIndent).AppendLine("{");
            sb.Append(innerIndent).AppendLine("    var __resourceOperation = __BeginResourceOperation();");
            sb.Append(innerIndent).AppendLine("    global::System.Exception? operationFailure = null;");
            sb.Append(innerIndent).AppendLine("    try");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        base.BindNodePortValuesCore();");
            sb.Append(innerIndent).AppendLine("        var node = GetOriginal();");
            sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            global::System.ObjectDisposedException.ThrowIf(__resourceCleanupRequested || __resourceCleanupCompleted || IsDisposed, this);");

            for (int i = 0; i < info.NodePortProperties.Length; i++)
            {
                NodePortPropertyInfo port = info.NodePortProperties[i];
                string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
                string valueTypeDisplay = port.ValueType.ToDisplayString(EmitHelpers.TypeDisplayFormat);
                string idxVar = $"__idx{i}";
                sb.Append(innerIndent).AppendLine($"            if (ItemIndexMap.TryGetValue(node.{port.Name}, out int {idxVar}))");
                sb.Append(innerIndent).AppendLine($"                {fieldName} = (global::Beutl.NodeGraph.Composition.ItemValue<{valueTypeDisplay}>)ItemValues[{idxVar}];");
            }

            sb.Append(innerIndent).AppendLine("        }");
            sb.Append(innerIndent).AppendLine("    }");
            sb.Append(innerIndent).AppendLine("    catch (global::System.Exception ex)");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        operationFailure = ex;");
            sb.Append(innerIndent).AppendLine("    }");
            EmitOperationCompletion(sb, innerIndent + "    ");
            sb.Append(innerIndent).AppendLine("}");
        }
    }

    private static void EmitUpdateMethod(StringBuilder sb, string innerIndent, string currentTypeDisplay, string renderContextType, string engineObjectType, ClassInfo info)
    {
        bool hasAdditionalMembers = info.ValueProperties.Length > 0
            || info.ObjectProperties.Length > 0
            || info.ListProperties.Length > 0;
        string? typedUpdateOwner = info.InheritedTypedResourceUpdateOwner?
            .ToDisplayString(EmitHelpers.TypeDisplayFormat);

        sb.Append(innerIndent).AppendLine($"partial void PreUpdate({currentTypeDisplay} obj, {renderContextType} context);");
        sb.Append(innerIndent).AppendLine($"partial void PostUpdate({currentTypeDisplay} obj, {renderContextType} context);");
        if (typedUpdateOwner != null)
        {
            sb.Append(innerIndent).AppendLine($"protected override bool IsCompatibleUpdateOwner({typedUpdateOwner} obj) => obj is {currentTypeDisplay};");
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine($"protected override void UpdateCore({typedUpdateOwner} obj, {renderContextType} context, ref bool updateOnly)");
        }
        else
        {
            sb.Append(innerIndent).AppendLine($"public override void Update({engineObjectType} obj, {renderContextType} context, ref bool updateOnly)");
        }
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine($"    var __typedObject = ({currentTypeDisplay})obj;");
        sb.Append(innerIndent).AppendLine(typedUpdateOwner != null
            ? "    var __resourceOperation = __BeginResourceOperation();"
            : "    var __resourceOperation = __BeginResourceOperation(__typedObject);");
        sb.Append(innerIndent).AppendLine("    global::System.Exception? operationFailure = null;");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        this.PreUpdate(__typedObject, context);");
        sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");
        sb.Append(innerIndent).AppendLine(typedUpdateOwner != null
            ? "        base.UpdateCore(obj, context, ref updateOnly);"
            : "        base.Update(obj, context, ref updateOnly);");
        sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");

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
                    sb.Append(innerIndent).AppendLine($"        CompareAndUpdate(context, __typedObject.{property.Name}, ref {fieldName}, ref updateOnly);");
                    sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");
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
                    sb.Append(innerIndent).AppendLine("        try");
                    sb.Append(innerIndent).AppendLine("        {");
                    sb.Append(innerIndent).AppendLine($"            CompareAndUpdateList(context, __typedObject.{property.Name}, ref {fieldName}, ref updateOnly);");
                    sb.Append(innerIndent).AppendLine("        }");
                    sb.Append(innerIndent).AppendLine("        finally");
                    sb.Append(innerIndent).AppendLine("        {");
                    sb.Append(innerIndent).AppendLine($"            __Refresh{property.Name}Snapshot();");
                    sb.Append(innerIndent).AppendLine("        }");
                    sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");
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
                    sb.Append(innerIndent).AppendLine($"        CompareAndUpdateObject(context, __typedObject.{property.Name}, ref {fieldName}, ref updateOnly);");
                    sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");
                }
            }
        }

        sb.Append(innerIndent).AppendLine("        this.PostUpdate(__typedObject, context);");
        sb.Append(innerIndent).AppendLine("        global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    catch (global::System.Exception ex)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        operationFailure = ex;");
        sb.Append(innerIndent).AppendLine("    }");
        EmitOperationCompletion(sb, innerIndent + "    ");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitOperationCompletion(StringBuilder sb, string indent)
    {
        sb.Append(indent).AppendLine("try");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    __EndResourceOperation(ref __resourceOperation);");
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("catch (global::System.Exception ex)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    operationFailure ??= ex;");
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("if (operationFailure != null)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operationFailure).Throw();");
        sb.Append(indent).AppendLine("}");
    }

    private static void EmitDisposeMethod(StringBuilder sb, string innerIndent, ClassInfo info)
    {
        sb.Append(innerIndent).AppendLine("partial void PreDispose(bool disposing);");
        sb.Append(innerIndent).AppendLine("partial void PostDispose(bool disposing);");
        sb.Append(innerIndent).AppendLine("partial void PrepareResourceDispose(bool disposing, global::Beutl.Engine.EngineObject.Resource.GeneratedResourceCleanupContext context);");
        sb.Append(innerIndent).AppendLine("partial void RollbackResourceDisposePreparation();");
        sb.Append(innerIndent).AppendLine("protected override void PrepareGeneratedResourceCleanupCore(bool disposing, global::Beutl.Engine.EngineObject.Resource.GeneratedResourceCleanupContext context)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    __PrepareGeneratedResourceCleanup(disposing, context);");
        sb.Append(innerIndent).AppendLine("    base.PrepareGeneratedResourceCleanupCore(disposing, context);");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("protected override void RollbackGeneratedResourceCleanupCore()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        base.RollbackGeneratedResourceCleanupCore();");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    finally");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        __RollbackGeneratedResourceCleanup();");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("protected override void CleanupGeneratedResourceCore(bool disposing, global::Beutl.Engine.EngineObject.Resource.GeneratedResourceCleanupContext context)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        __CleanupGeneratedResource(disposing, context);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    finally");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        base.CleanupGeneratedResourceCore(disposing, context);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("private void __PrepareGeneratedResourceCleanup(bool disposing, global::Beutl.Engine.EngineObject.Resource.GeneratedResourceCleanupContext context)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        if (__resourceCleanupCompleted)");
        sb.Append(innerIndent).AppendLine("            return;");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("        __resourceCleanupRequested = true;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    if (!disposing)");
        sb.Append(innerIndent).AppendLine("        return;");

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine($"    context.Reserve({fieldName});");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.AppendLine();
            sb.Append(innerIndent).AppendLine($"    foreach (var item in {fieldName})");
            sb.Append(innerIndent).AppendLine("    {");
            sb.Append(innerIndent).AppendLine("        context.Reserve(item);");
            sb.Append(innerIndent).AppendLine("    }");
        }

        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        __resourceCustomCleanupPreparationStarted = true;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    this.PrepareResourceDispose(disposing, context);");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();

        sb.Append(innerIndent).AppendLine("private void __RollbackGeneratedResourceCleanup()");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    bool rollbackCustomCleanup;");
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        if (!__resourceCleanupRequested || __resourceCleanupCompleted)");
        sb.Append(innerIndent).AppendLine("            return;");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("        rollbackCustomCleanup = __resourceCustomCleanupPreparationStarted;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        if (rollbackCustomCleanup)");
        sb.Append(innerIndent).AppendLine("            this.RollbackResourceDisposePreparation();");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    finally");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("        {");
        sb.Append(innerIndent).AppendLine("            __resourceCustomCleanupPreparationStarted = false;");
        sb.Append(innerIndent).AppendLine("            __resourceCleanupRequested = false;");
        sb.Append(innerIndent).AppendLine("        }");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
        sb.AppendLine();

        sb.Append(innerIndent).AppendLine("private void __CleanupGeneratedResource(bool disposing, global::Beutl.Engine.EngineObject.Resource.GeneratedResourceCleanupContext context)");
        sb.Append(innerIndent).AppendLine("{");
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        if (__resourceCleanupCompleted)");
        sb.Append(innerIndent).AppendLine("            return;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        this.PreDispose(disposing);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    catch (global::System.Exception ex)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        context.Capture(ex);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("    if (disposing)");
        sb.Append(innerIndent).AppendLine("    {");

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        context.DisposeOwned({fieldName});");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        foreach (var item in {fieldName})");
            sb.Append(innerIndent).AppendLine("        {");
            sb.Append(innerIndent).AppendLine("            context.DisposeOwned(item);");
            sb.Append(innerIndent).AppendLine("        }");
        }

        sb.Append(innerIndent).AppendLine("    }");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("    try");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        this.PostDispose(disposing);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("    catch (global::System.Exception ex)");
        sb.Append(innerIndent).AppendLine("    {");
        sb.Append(innerIndent).AppendLine("        context.Capture(ex);");
        sb.Append(innerIndent).AppendLine("    }");
        sb.AppendLine();
        sb.Append(innerIndent).AppendLine("    lock (__resourceLifecycleGate)");
        sb.Append(innerIndent).AppendLine("    {");

        foreach (ValuePropertyInfo property in info.ValueProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        {fieldName} = default!;");
        }

        foreach (ObjectPropertyInfo property in info.ObjectProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            sb.Append(innerIndent).AppendLine($"        {fieldName} = default!;");
        }

        foreach (ListPropertyInfo property in info.ListProperties)
        {
            if (property.ExcludeFromResource) continue;

            string fieldName = EmitHelpers.ToFieldName(property.Name);
            string resourceType = EmitHelpers.GetResourceTypeName(property.ElementType);
            sb.Append(innerIndent).AppendLine($"        {fieldName}.Clear();");
            sb.Append(innerIndent).AppendLine($"        {fieldName}Snapshot = global::System.Array.Empty<{resourceType}>();");
        }

        foreach (NodePortPropertyInfo port in info.NodePortProperties)
        {
            string fieldName = EmitHelpers.ToFieldName(port.Name) + "_ItemValue";
            sb.Append(innerIndent).AppendLine($"        {fieldName} = null;");
        }

        sb.Append(innerIndent).AppendLine("        __resourceCleanupCompleted = true;");
        sb.Append(innerIndent).AppendLine("    }");
        sb.Append(innerIndent).AppendLine("}");
    }


}
