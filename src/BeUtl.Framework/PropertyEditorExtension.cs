using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Framework;

internal interface IPropertyEditorExtensionImpl
{
    IEnumerable<CoreProperty> MatchProperty(IReadOnlyList<CoreProperty> properties);

    bool TryCreateContext(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context);

    bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out IControl? control);
}

[PrimitiveImpl]
public class PropertyEditorExtension : Extension
{
    public static readonly PropertyEditorExtension Instance = new();
    private IPropertyEditorExtensionImpl? _impl;

    public override string Name => "";

    public override string DisplayName => "";

    public virtual IEnumerable<CoreProperty> MatchProperty(IReadOnlyList<CoreProperty> properties)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.MatchProperty(properties);
    }

    public virtual bool TryCreateContext(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateContext(this, properties, out context);
    }

    public virtual bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out IControl? control)
    {
        _impl ??= ServiceLocator.Current.GetRequiredService<IPropertyEditorExtensionImpl>();
        return _impl.TryCreateControl(context, out control);
    }
}

public interface IPropertyEditorContext : IDisposable, IJsonSerializable
{
    PropertyEditorExtension Extension { get; }
}
