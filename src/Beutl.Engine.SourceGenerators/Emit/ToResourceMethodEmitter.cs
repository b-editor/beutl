using System.Text;

using Beutl.Engine.SourceGenerators.Models;

namespace Beutl.Engine.SourceGenerators.Emit;

public static class ToResourceMethodEmitter
{
    public static void Emit(StringBuilder sb, string indent, string currentTypeDisplay, ClassInfo info)
    {
        string renderContextType = "global::Beutl.Graphics.Rendering.RenderContext";

        if (info.Symbol.IsAbstract)
        {
            sb.Append(indent).AppendLine($"public abstract override {currentTypeDisplay}.Resource ToResource({renderContextType} context);");
        }
        else
        {
            sb.Append(indent).AppendLine($"public override {currentTypeDisplay}.Resource ToResource({renderContextType} context)");
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).AppendLine($"    var resource = new {currentTypeDisplay}.Resource();");
            sb.Append(indent).AppendLine("    bool updateOnly = true;");
            sb.Append(indent).AppendLine("    resource.Update(this, context, ref updateOnly);");
            sb.Append(indent).AppendLine($"    return resource;");
            sb.Append(indent).AppendLine("}");
        }
    }
}
