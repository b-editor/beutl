namespace Beutl.Extensibility;

// Adds property editors: RegisterEditors registers them, and Beutl chooses among the editors of all extensions.
public class PropertyEditorExtension : Extension
{
    // Holds the built-in editors.
    public static readonly PropertyEditorExtension Instance = new();

    private PropertyEditorRegistry? _registry;

    public override string Name => "";

    public override string DisplayName => "";

    // Registers this extension's editors; called once, before the host first uses them.
    protected internal virtual void RegisterEditors(PropertyEditorRegistry registry)
    {
    }

    internal PropertyEditorRegistry Registry => LazyInitializer.EnsureInitialized(ref _registry, () =>
    {
        var registry = new PropertyEditorRegistry(this);
        RegisterEditors(registry);
        return registry;
    });
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
