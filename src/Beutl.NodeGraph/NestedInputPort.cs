using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

/// <summary>An input bound to a property inside a node member's local value.</summary>
public interface INestedInputPort : IInputPort, IEnginePropertyBackedInputPort
{
    Reference<NodeMember> RootMember { get; }

    /// <summary>Property names prefixed by "p:" and stable list item IDs prefixed by "i:".</summary>
    IReadOnlyList<string> PropertyPath { get; }

    void Bind(EngineObject owner, IProperty property);

    void Unbind();
}

public sealed class NestedInputPort<T> : EnginePropertyBackedInputPort<T>, INestedInputPort
{
    public NestedInputPort()
    {
    }

    public NestedInputPort(NodeMember root, string[] path, EngineObject owner, IProperty<T> property)
        : base(owner, property)
    {
        RootMember = new(root);
        PropertyPath = path;
    }

    [NotAutoSerialized]
    [NotTracked]
    public Reference<NodeMember> RootMember { get; private set; }

    [NotAutoSerialized]
    [NotTracked]
    public IReadOnlyList<string> PropertyPath { get; private set; } = [];

    public void Bind(EngineObject owner, IProperty property) => BindProperty(owner, (IProperty<T>)property);

    public void Unbind() => UnbindProperty();

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(RootMember), RootMember);
        context.SetValue(nameof(PropertyPath), PropertyPath.ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        RootMember = context.GetValue<Reference<NodeMember>>(nameof(RootMember));
        PropertyPath = context.GetValue<string[]>(nameof(PropertyPath)) ?? [];
    }
}
