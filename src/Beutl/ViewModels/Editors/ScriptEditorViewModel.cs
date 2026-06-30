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

    private readonly IScriptCompilableEffect? _validator;

    public ScriptEditorViewModel(IPropertyAdapter<string?> property)
        : base(property)
    {
        DetectedScriptType = DetectScriptType(property.ImplementedType);
        _validator = Activator.CreateInstance(property.ImplementedType) as IScriptCompilableEffect;

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
        if (string.IsNullOrWhiteSpace(script) || _validator is null)
            return null;

        ScriptCompilationResult result = _validator.ValidateScript(script);
        return result.Status == ScriptCompilationStatus.Failed ? result.Error : null;
    }
}
