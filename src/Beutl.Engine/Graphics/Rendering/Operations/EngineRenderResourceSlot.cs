namespace Beutl.Graphics.Rendering;

internal sealed class EngineRenderResourceSlot(Type valueType) : RenderResourceSlot
{
    private readonly Type _valueType = valueType ?? throw new ArgumentNullException(nameof(valueType));

    internal override Type ValueType => _valueType;

    internal override bool Accepts(RenderResource resource) => true;
}
