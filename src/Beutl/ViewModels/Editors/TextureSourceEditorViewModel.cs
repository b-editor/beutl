using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
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

        ChildContext = Value.Select(v => v)
            .Select(x => x != null ? new PropertiesEditorViewModel(x) : null)
            .DisposePreviousValue()
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        DrawableName = Value.Select(v => v as DrawableTextureSource)
            .Select(x => x?.Drawable.CurrentValue?.GetType())
            .Select(GetDrawableDisplayName)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
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
        if (obj != null)
        {
            var visitor = new Visitor(this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }
        }
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
        if (!EqualityComparer<TextureSource?>.Default.Equals(oldValue, newValue))
        {
            PropertyAdapter.SetValue(newValue);
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

    public void SetDrawableType(Type type)
    {
        if (Value.Value is DrawableTextureSource drawableSource)
        {
            var drawable = (Drawable?)Activator.CreateInstance(type);
            drawableSource.Drawable.CurrentValue = drawable;
            Commit();
        }
    }

    public void SetDrawableTarget(Drawable target)
    {
        Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(Drawable));
        if (presenterType != null
            && Activator.CreateInstance(presenterType) is Drawable presenterDrawable
            && presenterDrawable is IPresenter<Drawable> presenterInterface
            && Value.Value is DrawableTextureSource drawableSource)
        {
            var expression = Expression.CreateReference<Drawable>(target.Id);
            presenterInterface.Target.Expression = expression;
            drawableSource.Drawable.CurrentValue = presenterDrawable;
            Commit();
        }
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
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out JsonNode? isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }

            ChildContext.Value?.ReadFromJson(json);
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            ChildContext.Value?.WriteToJson(json);
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ChildContext.Value?.Dispose();
    }

    private sealed record Visitor(TextureSourceEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
