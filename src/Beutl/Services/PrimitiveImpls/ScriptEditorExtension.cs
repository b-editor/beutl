using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Services.PrimitiveImpls;

public sealed class ScriptEditorExtension : PropertyEditorExtension
{
    public static new readonly ScriptEditorExtension Instance = new();

    // Only the panel shows the script editor; a node shows the built-in multiline string editor.
    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<string?, ScriptEditorViewModel>(
            p => new ScriptEditorViewModel(p),
            _ => new ScriptEditor(),
            new PropertyEditorOptions { Match = IsScriptProperty, PanelOnly = true });
    }

    private static bool IsScriptProperty(IPropertyAdapter p)
    {
        IProperty? engineProperty = p.GetEngineProperty();
        return p.PropertyType == typeof(string)
            && ((p.ImplementedType == typeof(GLSLScriptEffect) && engineProperty?.Name == nameof(GLSLScriptEffect.FragmentShader))
            || (p.ImplementedType == typeof(SKSLScriptEffect) && engineProperty?.Name == nameof(SKSLScriptEffect.Script))
            || (p.ImplementedType == typeof(CSharpScriptEffect) && engineProperty?.Name == nameof(CSharpScriptEffect.Script)));
    }
}
