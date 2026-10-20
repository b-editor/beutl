using SkiaSharp;

namespace Beutl.Graphics.Shaders;

// Binds a description's uniforms and child shaders, for the request executor and the fallback executor alike.
internal static class ShaderArgumentBinder
{
    public static void BindUniform(
        ShaderDescription description,
        int index,
        string name,
        ShaderExecutionContext? context,
        SKRuntimeEffectUniforms uniforms)
    {
        ShaderUniformBinding binding = description.Uniforms[index];
        if (!description.Source.Uniforms.TryGetValue(binding.Name, out SkslUniformDeclaration declaration))
            throw new InvalidOperationException($"Shader uniform '{binding.Name}' was not declared.");

        SkslUniformAssignment.SetUniform(uniforms, name, declaration, binding.Bind(declaration, context));
    }

    public static void BindResource(
        ShaderDescription description,
        int index,
        string name,
        ShaderExecutionContext? context,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children)
    {
        SKShader child = description.Resources[index].Bind(context
            ?? throw new InvalidOperationException("A shader resource binding requires an execution context."));
        children.Add(child);
        runtimeChildren[name] = child;
    }

    // A standalone program declares each argument under the name the description binds it by.
    public static void BindStandalone(
        ShaderDescription description,
        ShaderExecutionContext? context,
        SKRuntimeEffectUniforms uniforms,
        SKRuntimeEffectChildren runtimeChildren,
        List<SKShader> children)
    {
        for (int index = 0; index < description.Uniforms.Count; index++)
            BindUniform(description, index, description.Uniforms[index].Name, context, uniforms);

        for (int index = 0; index < description.Resources.Count; index++)
            BindResource(description, index, description.Resources[index].Name, context, runtimeChildren, children);
    }
}
