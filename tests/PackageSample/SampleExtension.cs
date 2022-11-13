using Beutl.Framework;
using Beutl.Streaming;

namespace PackageSample;

[Export]
public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        OperatorRegistry.RegisterOperation<SampleOp>("Sample Operator");
    }
}
