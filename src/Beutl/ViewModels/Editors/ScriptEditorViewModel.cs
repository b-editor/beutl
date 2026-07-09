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
            .Subscribe(result =>
            {
                CompileError.Value = result.Status == ScriptCompilationStatus.Failed ? result.Error : null;
                ValidationNotice.Value = result.Status == ScriptCompilationStatus.Unavailable
                    ? MessageStrings.ScriptValidationUnavailable
                    : null;
            })
            .DisposeWith(Disposables);
    }

    public ReactivePropertySlim<string?> CompileError { get; } = new();

    // Distinct from CompileError: "could not check" must not read as a failure or a success.
    public ReactivePropertySlim<string?> ValidationNotice { get; } = new();

    public ScriptType DetectedScriptType { get; }

    private static ScriptType DetectScriptType(Type implementedType)
    {
        if (implementedType == typeof(GLSLScriptEffect))
            return ScriptType.GLSL;
        if (implementedType == typeof(SKSLScriptEffect))
            return ScriptType.SKSL;
        return ScriptType.CSharp;
    }

    private ScriptCompilationResult ValidateScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script) || _validator is null)
            return ScriptCompilationResult.Compiled;

        return _validator.ValidateScript(script);
    }
}
