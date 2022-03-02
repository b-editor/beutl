using BeUtl.Framework;
using BeUtl.ProjectSystem;

namespace PackageSample;

public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        LayerOperationRegistry.RegisterOperation<SampleOp>("S.SamplePackage.SampleExtension.SampleOp");
    }
}
