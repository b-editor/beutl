using Beutl.Graphics.Effects;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class ScriptEditorViewModel : ValueEditorViewModel<string?>
{
    public enum ScriptType
    {
        CSharp,
        SKSL,
        GLSL,
    }

    public ScriptEditorViewModel(IPropertyAdapter<string?> property)
        : base(property)
    {
        DetectedScriptType = DetectScriptType(property.ImplementedType);

        Value
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Select(script => Observable.Start(() => ValidateScript(script)))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(error => CompileError.Value = error)
            .DisposeWith(Disposables);
    }

    public ReactivePropertySlim<string?> CompileError { get; } = new();

    public ScriptType DetectedScriptType { get; }

    private static ScriptType DetectScriptType(Type implementedType)
    {
        if (implementedType == typeof(GLSLScriptEffect))
            return ScriptType.GLSL;
        if (implementedType == typeof(SKSLScriptEffect))
            return ScriptType.SKSL;
        return ScriptType.CSharp;
    }

    private string? ValidateScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        return DetectedScriptType switch
        {
            ScriptType.CSharp => CSharpScriptEffect.ValidateScript(script),
            ScriptType.SKSL => SKSLScriptEffect.ValidateScript(script),
            ScriptType.GLSL => GLSLScriptEffect.ValidateScript(script),
            _ => null,
        };
    }
}
