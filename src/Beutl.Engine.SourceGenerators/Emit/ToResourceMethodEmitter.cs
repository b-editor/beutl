using System.Text;

using Beutl.Engine.SourceGenerators.Models;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ToResourceMethodEmitter
{
    public static void Emit(StringBuilder sb, string indent, string currentTypeDisplay, ClassInfo info)
    {
        if (info.SuppressedResourceGeneration) return;

        string renderContextType = "global::Beutl.Composition.CompositionContext";

        if (info.Symbol.IsAbstract)
        {
            sb.Append(indent).AppendLine($"public abstract override {currentTypeDisplay}.Resource ToResource({renderContextType} context);");
        }
        else
        {
            sb.Append(indent).AppendLine($"public override {currentTypeDisplay}.Resource ToResource({renderContextType} context)");
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).AppendLine($"    var resource = new {currentTypeDisplay}.Resource();");
            sb.Append(indent).AppendLine("    try");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        bool updateOnly = true;");
            sb.Append(indent).AppendLine("        resource.Update(this, context, ref updateOnly);");
            sb.Append(indent).AppendLine("        return resource;");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("    catch");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        try");
            sb.Append(indent).AppendLine("        {");
            sb.Append(indent).AppendLine("            resource.Dispose();");
            sb.Append(indent).AppendLine("        }");
            sb.Append(indent).AppendLine("        catch");
            sb.Append(indent).AppendLine("        {");
            sb.Append(indent).AppendLine("            // Preserve the Update failure while releasing the partially initialized resource.");
            sb.Append(indent).AppendLine("        }");
            sb.AppendLine();
            sb.Append(indent).AppendLine("        throw;");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");
        }
    }
}
