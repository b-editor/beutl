using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ScriptEditorExtension : PropertyEditorExtension
{
    public static new readonly ScriptEditorExtension Instance = new();

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        return [.. properties.Where(MatchProperty)];
    }

    private static bool MatchProperty(IPropertyAdapter p)
    {
        IProperty? engineProperty = p.GetEngineProperty();
        return p.PropertyType == typeof(string)
            && ((p.ImplementedType == typeof(GLSLScriptEffect) && engineProperty?.Name == nameof(GLSLScriptEffect.FragmentShader))
            || (p.ImplementedType == typeof(SKSLScriptEffect) && engineProperty?.Name == nameof(SKSLScriptEffect.Script))
            || (p.ImplementedType == typeof(CSharpScriptEffect) && engineProperty?.Name == nameof(CSharpScriptEffect.Script)));
    }

    public override bool TryCreateContext(IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        if (properties.Count == 1
            && properties[0] is IPropertyAdapter<string?> stringProp
            && MatchProperty(stringProp))
        {
            context = new ScriptEditorViewModel(stringProp)
            {
                Extension = this
            };
            return true;
        }

        context = null;
        return false;
    }

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        if (context is ScriptEditorViewModel)
        {
            control = new ScriptEditor();
            return true;
        }

        control = null;
        return false;
    }
}
