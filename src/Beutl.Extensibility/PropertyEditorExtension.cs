using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

namespace Beutl.Extensibility;

internal interface IPropertyEditorExtensionImpl
{
    IEnumerable<IAbstractProperty> MatchProperty(IReadOnlyList<IAbstractProperty> properties);

    bool TryCreateContext(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateContextForNode(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateContextForListItem(PropertyEditorExtension extension, IAbstractProperty property, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateContextForSettings(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control);

    bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control);

    bool TryCreateControlForListItem(IPropertyEditorContext context, [NotNullWhen(true)] out IListItemEditor? control);

    bool TryCreateControlForSettings(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control);
}

[PrimitiveImpl]
public class PropertyEditorExtension : Extension
{
    public static readonly PropertyEditorExtension Instance = new();

    private static IPropertyEditorExtensionImpl? s_defaultHandler;

    internal static IPropertyEditorExtensionImpl DefaultHandler
    {
        get => s_defaultHandler!;
        set => s_defaultHandler ??= value;
    }

    public override string Name => "";

    public override string DisplayName => "";

    public virtual IEnumerable<IAbstractProperty> MatchProperty(IReadOnlyList<IAbstractProperty> properties)
    {
        return DefaultHandler.MatchProperty(properties);
    }

    public virtual bool TryCreateContext(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        return DefaultHandler.TryCreateContext(this, properties, out context);
    }

    public virtual bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        return DefaultHandler.TryCreateControl(context, out control);
    }

    public virtual bool TryCreateContextForNode(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        return DefaultHandler.TryCreateContextForNode(this, properties, out context);
    }

    public virtual bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        return DefaultHandler.TryCreateControlForNode(context, out control);
    }

    public virtual bool TryCreateContextForListItem(
        IAbstractProperty property,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        return DefaultHandler.TryCreateContextForListItem(this, property, out context);
    }

    public virtual bool TryCreateControlForListItem(
        IPropertyEditorContext context,
        [NotNullWhen(true)] out IListItemEditor? control)
    {
        return DefaultHandler.TryCreateControlForListItem(context, out control);
    }

    public virtual bool TryCreateContextForSettings(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        return DefaultHandler.TryCreateContextForSettings(this, properties, out context);
    }

    public virtual bool TryCreateControlForSettings(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        return DefaultHandler.TryCreateControlForSettings(context, out control);
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
