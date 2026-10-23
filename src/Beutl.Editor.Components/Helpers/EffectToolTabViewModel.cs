using System.Text.Json.Nodes;
using Beutl.Graphics.Effects;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

// A tool tab on one effect: the effect, its pin and header, and closing the tab when the effect leaves the scene.
public abstract class EffectToolTabViewModel<TEffect> : IPinnableToolContext
    where TEffect : FilterEffect
{
    private readonly ToolTabPin _pin;

    private protected EffectToolTabViewModel(IEditorContext editorContext, string title)
    {
        EditorContext = editorContext;

        Effect.Subscribe(SetEditors)
            .DisposeWith(Disposables);

        _pin = new ToolTabPin(Effect.Select(effect => effect != null)).DisposeWith(Disposables);

        HasEffect = Effect
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables)!;

        Header = Effect
            .Select(ToolTabHeaderHelper.ObserveObjectLabel)
            .Switch()
            .Select(label => ToolTabHeaderHelper.Compose(title, label))
            .ToReadOnlyReactivePropertySlim(title)
            .DisposeWith(Disposables)!;
    }

    public IReadOnlyReactiveProperty<string> Header { get; }

    public abstract ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public ReadOnlyReactivePropertySlim<bool> HasEffect { get; }

    public ReactivePropertySlim<TEffect?> Effect { get; } = new();

    public IReactiveProperty<bool> IsPinned => _pin.IsPinned;

    public IReadOnlyReactiveProperty<bool> HasTarget => _pin.HasTarget;

    private protected CompositeDisposable Disposables { get; } = [];

    private protected CompositeDisposable EffectDisposables { get; } = [];

    private protected IEditorContext EditorContext { get; }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Effect.Value?.FindHierarchicalParent<Element>();

        if (serviceType == typeof(TEffect))
            return Effect.Value;

        return EditorContext.GetService(serviceType);
    }

    public void Dispose()
    {
        ClearEditors();
        Disposables.Dispose();
    }

    public abstract void ReadFromJson(JsonObject json);

    public abstract void WriteToJson(JsonObject json);

    private protected abstract void SetEditors(TEffect? effect);

    private protected abstract void ClearEditors();

    private protected void ReadEffectFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue("effectId", out string? effectIdStr)
            && Guid.TryParse(effectIdStr, out Guid effectId))
        {
            Effect.Value = EditorContext.GetService<Scene>()?.FindById(effectId) as TEffect;
        }
    }

    private protected void WriteEffectToJson(JsonObject json)
    {
        json["effectId"] = Effect.Value?.Id.Value;
    }

    private protected void ReadPinFromJson(JsonObject json) => _pin.ReadFromJson(json);

    private protected void WritePinToJson(JsonObject json) => _pin.WriteToJson(json);

    private protected void CloseWhenDetached(TEffect effect)
    {
        effect.DetachedFromHierarchy += OnEffectDetached;
        EffectDisposables.Add(Disposable.Create(() => effect.DetachedFromHierarchy -= OnEffectDetached));
    }

    private void OnEffectDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        EditorContext.CloseToolTab(this);
    }
}
