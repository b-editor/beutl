using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Graphics3D.Textures;
using Beutl.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class TextureSourceEditorViewModel : BaseEditorViewModel
{
    public TextureSourceEditorViewModel(IPropertyAdapter<TextureSource?> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        IsImageTextureSource = Value.Select(v => v is ImageTextureSource)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsDrawableTextureSource = Value.Select(v => v is DrawableTextureSource)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ChildContext = Value
            .CombineLatest(ObserveExtensionProvider())
            .Select(t => t.First != null ? new PropertiesEditorViewModel(t.First, t.Second) : null)
            .DisposePreviousValue()
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        DrawableName = Value.Select(v => v as DrawableTextureSource)
            .Select(x => x?.Drawable.CurrentValue?.GetType())
            .Select(GetDrawableDisplayName)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

    }

    public ReadOnlyReactiveProperty<TextureSource?> Value { get; }

    public ReadOnlyReactivePropertySlim<bool> IsImageTextureSource { get; }

    public ReadOnlyReactivePropertySlim<bool> IsDrawableTextureSource { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> ChildContext { get; }

    public ReadOnlyReactivePropertySlim<string?> DrawableName { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    private static string GetDrawableDisplayName(Type? type)
    {
        if (type == null)
        {
            return Strings.CreateNew;
        }

        return LibraryService.Current.FindItem(type)?.DisplayName ?? type.Name;
    }

    private void AcceptChildren(PropertiesEditorViewModel? obj)
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), null, obj);
    }

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (TextureSource?)defaultValue);
        }
    }

    public void SetValue(TextureSource? oldValue, TextureSource? newValue)
    {
        if (!CanReplace(oldValue, newValue)) return;
        if (!EqualityComparer<TextureSource?>.Default.Equals(oldValue, newValue))
        {
            PropertyAdapter.SetValue(newValue);
            CompleteElementRepair();
            Commit();
        }
    }

    public void ChangeToImageTextureSource()
    {
        var oldValue = Value.Value;
        var newValue = new ImageTextureSource();
        SetValue(oldValue, newValue);
    }

    public void ChangeToDrawableTextureSource()
    {
        var oldValue = Value.Value;
        var newValue = new DrawableTextureSource();
        SetValue(oldValue, newValue);
    }

    public void ChangeToNull()
    {
        var oldValue = Value.Value;
        SetValue(oldValue, null);
    }


    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            AcceptChildren(ChildContext.Value);
        }
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        NestedEditorContextHelper.ReadNestedJson(json, IsExpanded, ChildContext.Value);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        NestedEditorContextHelper.WriteNestedJson(json, IsExpanded.Value, ChildContext.Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ChildContext.Value?.Dispose();
    }
}
