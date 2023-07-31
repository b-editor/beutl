using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Framework;

internal interface IPropertyEditorExtensionImpl
{
    IEnumerable<IAbstractProperty> MatchProperty(IReadOnlyList<IAbstractProperty> properties);

    bool TryCreateContext(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateContextForNode(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);
    
    bool TryCreateContextForListItem(PropertyEditorExtension extension, IAbstractProperty property, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control);

    bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control);

    bool TryCreateControlForListItem(IPropertyEditorContext context, [NotNullWhen(true)] out IListItemEditor? control);
}

[PrimitiveImpl]
public class PropertyEditorExtension : Extension
{
    public static readonly PropertyEditorExtension Instance = new();
    private IPropertyEditorExtensionImpl? _impl;

    public override string Name => "";

    public override string DisplayName => "";

    public virtual IEnumerable<IAbstractProperty> MatchProperty(IReadOnlyList<IAbstractProperty> properties)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.MatchProperty(properties);
    }

    public virtual bool TryCreateContext(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateContext(this, properties, out context);
    }

    public virtual bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateControl(context, out control);
    }

    public virtual bool TryCreateContextForNode(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateContextForNode(this, properties, out context);
    }

    public virtual bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateControlForNode(context, out control);
    }

    public virtual bool TryCreateContextForListItem(
        IAbstractProperty property,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateContextForListItem(this, property, out context);
    }

    public virtual bool TryCreateControlForListItem(
        IPropertyEditorContext context,
        [NotNullWhen(true)] out IListItemEditor? control)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateControlForListItem(context, out control);
    }
}

public interface IPropertyEditorContextVisitor
{
    void Visit(IPropertyEditorContext context);
}

public interface IPropertyEditorContext : IDisposable, IJsonSerializable
{
    PropertyEditorExtension Extension { get; }

    void Accept(IPropertyEditorContextVisitor visitor);
}
