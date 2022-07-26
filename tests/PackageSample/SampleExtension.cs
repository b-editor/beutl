using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Streaming;

namespace PackageSample;

public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        OperatorRegistry.RegisterOperation<SampleOp>("S.SamplePackage.SampleExtension.SampleOp");
    }
}
